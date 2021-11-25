﻿using System.Collections.Generic;
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
using System;
using OutFitPatcher.NPC;
using Mutagen.Bethesda.Plugins;

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
                .SetTypicalOpen(GameRelease.SkyrimSE, "ZZZ Patcher1 - Bashed Patch.esp")
                .Run(args);
        }

        private static void RunPacher(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            WarmupAll.Init();
            //foreach (var mod in state.LoadOrder.PriorityOrder.Select(x => x.Mod))
            //{
            //    Console.WriteLine("Processing Mod: " + mod.ModKey.FileName);
            //    ReferenceCaching.BuildReferenceCache(mod);
            //}
            //Dictionary<FormKey, List<FormKey>> a = ReferenceCaching.LoadReferenceCache();


            foreach (var npc in state.LoadOrder.PriorityOrder
               .WinningOverrides<INpcGetter>()
               .Where(x=>x.EditorID.ToLower().Contains("lvl"))) { 
                
            }

            //// Reading and Parsing setting file
            //Init(state);

                //if (!RequirementsFullfilled(state)) return;
                //// Morphs.create();

                ////Distribute Jewellaries and Sleeping outfits, and outfits
                //JewelaryManager.ProcessAndDistributeJewelary(state);
                //new SleepingOutfitManager(state).ProcessSlepingOutfits();
                //new OutfitManager(state).Process();

                ////// Little house keeping 
                //PatchHighPolyHead(state);
                //CreateBashPatchForLVLI(state);
                //CreateBashPatchForLVLN(state);

                //// Saving all the patches to disk
                //Logger.InfoFormat("Saving all the patches to disk.....");
                //Patches.TryAdd(state.PatchMod.ModKey.FileName.String, state.PatchMod);
                //Patches.Values.ForEach(p => FileUtils.SaveMod(state, p));
        }

        private static void CreateBashPatchForLVLI(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            Logger.InfoFormat("Creating Leveled List bash patch....");
            foreach (ILeveledItemGetter lvli in Cache.WrappedImmutableCache.PriorityOrder
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

        private static void CreateBashPatchForLVLN(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            Logger.InfoFormat("Creating Leveled List bash patch....");
            foreach (ILeveledNpcGetter lvli in Cache.WrappedImmutableCache.PriorityOrder
                .WinningOverrides<ILeveledNpcGetter>())
            {
                List<ILeveledNpcGetter> lvlis = Cache.ResolveAll<ILeveledNpcGetter>(lvli.FormKey).ToList();
                if (lvlis.Count > 1)
                {
                    List<LeveledNpcEntry> entries = new();
                    lvlis.ForEach(x => entries.AddRange(x.Entries.EmptyIfNull().Select(entry => entry.DeepCopy())));
                    LeveledNpc lvl = state.PatchMod.LeveledNpcs.GetOrAddAsOverride(lvlis.First());
                    lvl.Entries = new ();
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
                if (modKey.FileName.String.StartsWith(Patcher.PatcherPrefix))
                {
                    Logger.ErrorFormat("Disable or delete mod to continue: " + modKey.FileName);
                    patchExists = true;
                }
            }

            // Copying the scripts
            var src = Path.Combine(state.ExtraSettingsDataPath, "Scripts");
            var des = Path.Combine(state.DataFolderPath, "Scripts");
            FileUtils.CopyDirectory(src, des);

            Logger.DebugFormat("All the requirements are validated...");
            return !patchExists;
        }
        
        private static void PatchHighPolyHead(IPatcherState<ISkyrimMod, ISkyrimModGetter> state) {
            foreach (var npc in state.LoadOrder.PriorityOrder
                .WinningOverrides<INpcGetter>())
            {
                var contxt = Cache.ResolveAllContexts<INpc, INpcGetter>(npc.FormKey);
                string esps = "High Poly NPC";
                ISkyrimMod patchedMod = FileUtils.GetOrAddPatch(Patcher.PatcherPrefix + "High Poly NPC.esp");

                var first = contxt.First();
                var mods = contxt.Where(x => x.ModKey.FileName.String.Contains(esps));
                if (mods.Any() && mods.First().ModKey != first.ModKey) {
                    var winner = contxt.First().Record;
                    var looser = mods.First().Record;
                    var newNPC = patchedMod.Npcs.GetOrAddAsOverride(winner);
                    newNPC.HeadParts.Clear();
                    newNPC.HeadParts.AddRange(looser.HeadParts);
                    Logger.InfoFormat("Patched Brown Head for: " + newNPC.EditorID);
                }
            }
        } 
    }
}
