using System.Collections.Generic;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using System.Threading.Tasks;
using Noggog;
using System.IO;
using System.Text.RegularExpressions;
using System;
using System.Threading.Tasks.Dataflow;
using System.Collections.Concurrent;
using OutFitPatcher.Utils;
using static OutFitPatcher.Config.Settings;
using log4net;
using OutFitPatcher.Armor;
using OutFitPatcher.Config;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;

namespace OutFitPatcher.Managers
{
    public class SleepingOutfitManager
    {
        private readonly Random Random = new();
        private ISkyrimMod? PatchedMod;
        private readonly HashSet<FormKey> SleepingLLs;
        //private readonly IEnumerable<IItemGetter> LowerGarments;
        private readonly IPatcherState<ISkyrimMod, ISkyrimModGetter> State;
        private readonly int MaleMeshCount = Settings.PatcherSettings.MaleSleepingWears.Count;
        private static readonly ILog Logger = LogManager.GetLogger(typeof(SleepingOutfitManager));

        public SleepingOutfitManager(IPatcherState<ISkyrimMod, ISkyrimModGetter> State)
        {
            this.State = State;
            SleepingLLs = new();
        }

        public void ProcessSlepingOutfits()
        {
            if (!Settings.UserSettings.SleepingOutfitMods.Any()) return;

            PatchedMod = FileUtils.GetOrAddPatch(Settings.PatcherSettings.PatcherPrefix + "SleepTight.esp");
            string sleeptight = Path.Combine(State.DataFolderPath, "SleepTight.esp");
            if (ModKey.TryFromNameAndExtension("SleepTight.esp", out var modKey) && State.LoadOrder.ContainsKey(modKey))
            {
                Logger.InfoFormat("Generating Leveled List Records for Sleeping Outfits...");
                CreateLLs();

                // Distributing using SleepTight mod
                Logger.InfoFormat("Distributing sleeping outfits using SleepTight...");
                var llList = SleepingLLs.Select(x => (FormLink<IItemGetter>)x);

                ISkyrimModGetter SleepTight = State.LoadOrder.GetIfEnabledAndExists(modKey);
                IFormListGetter flist = SleepTight.FormLists.Where(x => x.EditorID.Equals("_SLPRobesList")).First();
                FormList formList = PatchedMod.FormLists.GetOrAddAsOverride(flist);
                formList.Items.Clear();
                formList.Items.AddRange(llList);

                // Adding sleeping outfits to formList
                //if (Settings.AddSleepingOutfitsToMannequin)
                //    AddArmorsToMannequin();

                // Saving
                SleepingLLs.Clear();
                Logger.InfoFormat("Creation of Sleeping outfits is completed...\n\n");
            }
            else
            {
                Logger.WarnFormat("Skipping sleeping outfits distribution, 'SleepTight.esp' not found...\n\n");
            }
        }

        private void CreateLLs()
        {
            // For each armor mod getting armor records
            foreach (IModListing<ISkyrimModGetter> modlist in State.LoadOrder.PriorityOrder
                .Where<IModListing<ISkyrimModGetter>>(x => (Settings.UserSettings.SleepingOutfitMods.Contains(x.ModKey.FileName))))
            {
                ISkyrimModGetter mod = modlist.Mod;
                string modName = mod.ModKey.FileName;
                string llPrefix = Settings.PatcherSettings.SLPLeveledListPrefix;

                // Getting Armors
                IEnumerable<IArmorGetter> armors = mod.Armors
                .Where(x =>
                    ArmorUtils.IsValidArmor(x)
                    && x.Keywords != null);
                if (!armors.Any()) return;

                List<IArmorGetter> upperArmors = new ();
                List<IArmorGetter> nonBodies = new ();

                armors.ForEach(x => {
                    if (ArmorUtils.IsUpperArmor(x)) upperArmors.Add(x);
                    else nonBodies.Add(x);
                });

                               
                for (int i = 0; i < upperArmors.Count; i++)
                {
                    var body = upperArmors.ElementAtOrDefault(i);
                    AddMissingGenderMeshes(body);

                    TArmorSet armorSet = new(body, PatchedMod);
                    armorSet.CreateMatchingSetFrom(nonBodies);
                    FormKey llKey = armorSet.CreateLeveledList(PatchedMod);

                    if (llKey == FormKey.Null) continue;
                    SleepingLLs.Add(llKey);
                }
                Logger.InfoFormat("Created ({0}) Sleeping outfit Record(s) for {1}", upperArmors.Count.ToString("D3"), modName);
            }
        }

        private void AddMissingGenderMeshes(IArmorGetter armor)
        {
            IArmorAddonGetter addon = armor.Armature.FirstOrDefault().Resolve(Cache);
            if (addon.WorldModel == null) return;

            // Mapping Male Models with Female only Armor
            if (addon.WorldModel.Male == null)
            {
                // Getting male robes                
                int idx = Random.Next(0, MaleMeshCount);
                FormKey key = FormKey.Factory(Settings.PatcherSettings.MaleSleepingWears.ElementAt(idx));
                IArmorGetter robe = Cache.Resolve<IArmorGetter>(key);
                IArmorAddonGetter robeAddon = robe.Armature.FirstOrDefault().Resolve(Cache);

                IArmorAddon localAddon = PatchedMod.ArmorAddons.GetOrAddAsOverride(addon);
                localAddon.WorldModel.Male = new();
                localAddon.WorldModel.Male.File = robeAddon.WorldModel.Male.File;
            }
        }

    }
}
