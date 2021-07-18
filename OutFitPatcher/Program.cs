using System.Collections.Generic;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using System.Threading.Tasks;
using Noggog;
using System.IO;
using OutFitPatcher.Utils;
using log4net.Config;
using log4net;
using System.Reflection;
using static OutFitPatcher.Config.Configuration;
using OutFitPatcher.Managers;
using OutFitPatcher.Config;
using System.Text.RegularExpressions;
using System;
using OutFitPatcher.Bodyslide;
using Mutagen.Bethesda.Plugins.Cache;

namespace OutFitPatcher
{
    class Program
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(Program));

        public static async Task<int> Main(string[] args)
        {
            // Init logger
            var logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
            XmlConfigurator.Configure(logRepository, new FileInfo("log4net.config"));

            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPacher)
                .SetTypicalOpen(GameRelease.SkyrimSE, "ZZZ Outfit Bashed Patch.esp")
                .Run(args);
        }

        private static void RunPacher(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            // Reading and Parsing setting file
            Init(state);

            if (!RequirementsFullfilled(state)) return;
            Morphs.create();

            //Distribute Jewellaries and Sleeping outfits, and outfits
            //JewelaryManager.ProcessAndDistributeJewelary(state);
            //new SleepingOutfitManager(state).ProcessSlepingOutfits();
            new OutfitManager(state).Process();

            // Little house keeping 
            //CreateBashPatchForLVLI(state);
            
            // Saving all the patches to disk
            Logger.InfoFormat("Saving all the patches to disk.....");
            Patches.TryAdd(State.PatchMod.ModKey.Name, State.PatchMod);
            Patches.Values.ForEach(p => FileUtils.SaveMod(state, p));
        }

        private static void CreateBashPatchForLVLI(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            Logger.InfoFormat("Creating Leveled List bash patch....");
            ILinkCache cache = Cache;
            Logger.InfoFormat("Creating Leveled List bash patch....");
            
            foreach (ILeveledItemGetter lvli in cache.PriorityOrder
                .WinningOverrides<ILeveledItemGetter>())
            {
                List<ILeveledItemGetter> lvlis = Cache.ResolveAll<ILeveledItemGetter>(lvli.FormKey).ToList();
                if (lvlis.Count > 1)
                {
                    List<LeveledItemEntry> entries = new();
                    lvlis.ForEach(x => entries.AddRange(x.Entries.EmptyIfNull().Select(entry => entry.DeepCopy())));
                    LeveledItem lvl = state.PatchMod.LeveledItems.GetOrAddAsOverride(lvlis.First());
                    lvl.Entries = new ExtendedList<LeveledItemEntry>();
                    OutfitUtils.AddEntriesToLeveledList(state.PatchMod, lvl, entries.Distinct());
                }
            }
            Logger.InfoFormat("Leveled List Bash Patch Created...\n\n");
        }

        private static bool RequirementsFullfilled(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            Logger.InfoFormat("Validating requirements...");
            string spidLoc = Path.Combine(state.DataFolderPath, "skse", "plugins", "po3_SpellPerkItemDistributor.dll");
            if (!File.Exists(spidLoc))
            {
                Logger.ErrorFormat("Spell Perk Item Distributer mod is not found. Install it properly and re-run the patcher...");
                return false;
            }

            // Checking patched mods are enabled in load order
            bool patchExists = false;
            foreach (var modKey in state.LoadOrder.PriorityOrder
                .Where(x => x.ModKey != state.PatchMod.ModKey)
                .Select(x => x.ModKey))
            {
                if (modKey.FileName.String.Contains(Configuration.Patcher.PatcherSuffix))
                {
                    Logger.ErrorFormat("Disable or delete mod to continue: " + modKey.FileName);
                    patchExists = true;
                }
            }
            Logger.DebugFormat("All the requirements are validated...");
            return !patchExists;
        }
    }
}
