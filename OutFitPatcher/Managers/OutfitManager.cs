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
using System.Collections.Generic;
using System.Linq;
using OutFitPatcher.Armor;
using log4net;
using OutFitPatcher.NPC;
using OutFitPatcher.Config;
using Mutagen.Bethesda.Plugins;

namespace OutFitPatcher.Managers
{
    public class OutfitManager
    {
        private ISkyrimMod? PatchedMod;
        readonly IPatcherState<ISkyrimMod, ISkyrimModGetter> State;

        private List<string> DividableFactions = new();
        private ConcurrentDictionary<string, TArmorGroupable> GrouppedArmorSets = new();
        private readonly Dictionary<string, string> Regex4outfits = Settings.PatcherSettings.OutfitRegex;
        private static readonly ILog Logger = LogManager.GetLogger(typeof(OutfitManager));
        private ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<FormKey, float>>> ArmorsWithSlot = new();

        public OutfitManager(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            State = state;
            DividableFactions = Settings.PatcherSettings.DividableFactions.ToLower().Split("|").ToList();
        }

        public void Process()
        {
            var outfits = GetPatchableOutfits();
            GroupOutfits(outfits);

            GenerateArmorSlotData();
            CreateArmorsSets();

            ResolveOutfitOverrides();
            FilterGaurdAndMaterialBasedOutfits();
            CreateNewOutfits();
            ProcessNpcsForOutfits();
        }

        private Dictionary<string, int> GetPatchableOutfits()
        {
            Logger.InfoFormat("Fetching outfit records...");
            Dictionary<string, int> allowedOutfits = new();
            State.LoadOrder.PriorityOrder
                .Where(x => !Settings.UserSettings.ModsToSkip.Contains(x.ModKey.FileName))
                .WinningOverrides<INpcGetter>().EmptyIfNull()
                .ForEach(npc =>
                {
                    //mapping outfits
                    if (npc.DefaultOutfit.TryResolve<IOutfitGetter>(Settings.Cache, out IOutfitGetter otft))
                    {
                        string otfteid = otft.EditorID;
                        int val = allowedOutfits.ContainsKey(otfteid)
                            ? allowedOutfits.GetValueOrDefault(otfteid) : 0;
                        allowedOutfits[otfteid] = val + 1;
                    }
                });
            Logger.InfoFormat("Outfit records grouped...\n\n");
            return allowedOutfits;
        }

        private void GroupOutfits(Dictionary<string, int> AllowedOutfits)
        {
            Logger.InfoFormat("Getting outfits to be patched based on armor materials....");
            // Filtering outfits with more than 3 references
            var dict = AllowedOutfits
                .Where(x => AllowedOutfits.GetValueOrDefault(x.Key) > 3
                    && ArmorUtils.IsValidOutfit(x.Key));
            var allowedOutfits = new Dictionary<string, int>(dict);
            Logger.InfoFormat("Patchable outfit records: {0}", allowedOutfits.Count);

            var block = new ActionBlock<IOutfitGetter>(
                outfit =>
                {
                    AddOutfitToGroup(outfit);
                }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 10 });

            foreach (IOutfitGetter outfit in State.LoadOrder.PriorityOrder
                .Where(x => !Settings.UserSettings.ModsToSkip.Contains(x.ModKey.FileName)
                    && !Settings.UserSettings.SleepingOutfitMods.Contains(x.ModKey.FileName))
                .WinningOverrides<IOutfitGetter>()
                .Where(x => ArmorUtils.IsValidOutfit(x)
                    && allowedOutfits.ContainsKey(x.EditorID)))
            {
                block.Post(outfit);
            }
            block.Complete();
            block.Completion.Wait();

            Logger.InfoFormat("Outfits are categorized for patching....\n\n");
        }

        private void CreateArmorsSets()
        {
            Logger.InfoFormat("Grouping Armors....");
            // For each armor mod creating armor sets, skipping mods which have the outfit records.
            // If these outfit records are overriden it will be resolved later in the patch
            var modlists = State.LoadOrder.PriorityOrder
                .Where(x => Settings.UserSettings.ArmorModsForOutfits.ContainsKey(x.ModKey.FileName)
                    && x.Mod.Armors.Count > 0
                    && (!x.Mod.Outfits.Any() 
                    || x.Mod.Outfits.Where(x=> !Settings.PatcherSettings.Masters.Contains(x.FormKey.ModKey.FileName)).Any()));


            for(int m= 0; m < modlists.Count(); m++)
            {
                ISkyrimModGetter mod = modlists.ElementAt(m).Mod;
                List<IArmorGetter> bodies = new();
                List<IArmorGetter> others = new();

                var patchName = Settings.PatcherSettings.PatcherPrefix+"Armors " + (m / 150)+".esp";
                ISkyrimMod patch = FileUtils.GetOrAddPatch(patchName);                

                mod.Armors
                    .Where(x => ArmorUtils.IsValidArmor(x) && x.Keywords != null)
                    .ForEach(armor =>
                    {
                        if (ArmorUtils.IsBodyArmor(armor)) bodies.Add(armor);
                        else others.Add(armor);
                        //AddMissingGenderMeshes(patch, armor);
                    });

                int bodyCount = bodies.Count;
                for (int i = 0; i < bodyCount; i++)
                {
                    // Creating armor sets and LLs
                    var body = bodies.ElementAt(i);
                    TArmorSet armorSet = new(body, patch);
                    armorSet.CreateMatchingSetFrom(others, bodyCount == 1);

                    // Distributing weapons as well
                    if (bodyCount > 1 && mod.Weapons != null && mod.Weapons.Count > 0)
                        armorSet.CreateMatchingSetFrom(mod.Weapons, bodyCount);

                    // Checking for Boots
                    if (!armorSet.Armors.Where(x => x.BodySlots.Contains(TBodySlot.Feet)).Any())
                    {
                        string type = body.BodyTemplate.ArmorType.ToString();
                        FormKey feet = ArmorsWithSlot[type][TBodySlot.Feet.ToString()]
                            .Where(x => x.Value <= body.Value)
                            .First().Key;
                        armorSet.AddArmor(new TArmor(Settings.Cache.Resolve<IArmorGetter>(feet), armorSet.Material));                               
                    }

                    armorSet.CreateLeveledList();
                    List<string> modsFactions = Settings.UserSettings.ArmorModsForOutfits[mod.ModKey.FileName];
                    modsFactions.Remove("Generic");

                    // to be distributed using materials
                    var group = armorSet.Material;
                   // if(armorSet.Gender=="C")
                        AddArmorSetToGroup(group, armorSet);

                    foreach (string fgroup in modsFactions)
                    {
                        var fg = DividableFactions.Contains(fgroup.ToLower())
                            ? fgroup + armorSet.Type : fgroup;
                        AddArmorSetToGroup(fg, armorSet);
                    }

                    if (i > 0 && (i + 1) % 100 == 0)
                        Logger.InfoFormat("Created {0}/{1} armor-set for: {2}", i + 1, bodyCount, mod.ModKey.FileName);
                }
                Logger.InfoFormat("Created {0}/{0} armor-set for: {1}", bodyCount, mod.ModKey.FileName);
            }
            Logger.InfoFormat("Created armor sets for armor mods based on materials....\n\n");
        }
        
        private void CreateNewOutfits()
        {
            PatchedMod = FileUtils.GetOrAddPatch(Settings.PatcherSettings.PatcherPrefix + "Outfits.esp");
            Logger.InfoFormat("Creating Outfit records...");

            GrouppedArmorSets["Unknown"].Armors.ForEach(set =>
            {
                if (set.Type == TArmorType.Heavy && GrouppedArmorSets.ContainsKey("BanditHeavy") && GrouppedArmorSets.ContainsKey("MercenaryHeavy"))
                {
                    GrouppedArmorSets["BanditHeavy"].Armors.Add(set);
                    GrouppedArmorSets["MercenaryHeavy"].Armors.Add(set);
                }
                if (set.Type == TArmorType.Light && GrouppedArmorSets.ContainsKey("BanditLight") && GrouppedArmorSets.ContainsKey("MercenaryLight"))
                {
                    GrouppedArmorSets["BanditLight"].Armors.Add(set);
                    GrouppedArmorSets["MercenaryLight"].Armors.Add(set);
                }
                else if (set.Type == TArmorType.Wizard && GrouppedArmorSets.ContainsKey("BanditWizard") && GrouppedArmorSets.ContainsKey("MercenaryWizard"))
                {
                    GrouppedArmorSets["BanditWizard"].Armors.Add(set);
                    GrouppedArmorSets["MercenaryWizard"].Armors.Add(set);
                }
            });

            GrouppedArmorSets.TryRemove("Unknown", out var temp);
            GrouppedArmorSets = new(GrouppedArmorSets.Where(x => !x.Value.Armors.IsEmpty));
            GrouppedArmorSets.ForEach(rec =>
            {
                rec.Value.CreateGenderSpecificOutfits(PatchedMod);
                Logger.InfoFormat("Created new outfit record for: " + rec.Key);
            });
            Logger.InfoFormat("Added new outfits records...\n\n");
        }

        private void ResolveOutfitOverrides() {
            var outfitContext = State.LoadOrder.PriorityOrder.Outfit()
               .WinningContextOverrides().Where(x => ArmorUtils.IsValidOutfit(x.Record));

            PatchedMod = FileUtils.GetOrAddPatch(Settings.PatcherSettings.PatcherPrefix + "Outfits.esp");
            foreach (var outfit in State.LoadOrder.PriorityOrder
                .Where(x=> Settings.UserSettings.ArmorModsForOutfits.ContainsKey(x.ModKey.FileName))
                .WinningOverrides<IOutfitGetter>()) {
                
                var winningOtfts = outfitContext.Where(c => c.Record.FormKey == outfit.FormKey).EmptyIfNull();
                if (winningOtfts.Any())
                {
                    var winningOtft = winningOtfts.First().Record;
                    
                    // Reverting Overriden outfit by armor mods added in the patcher
                    var lastNonModOutfit = Settings.Cache.ResolveAllContexts<IOutfit, IOutfitGetter>(winningOtft.FormKey)
                        .Where(c => !Settings.UserSettings.ArmorModsForOutfits.ContainsKey(c.ModKey.FileName));

                    if (lastNonModOutfit.Count() > 1)
                    {
                        // Getting outfit records form armor mods added in the patcher and patchign those
                        List<LeveledItem> oLLs = new();
                        Outfit o = PatchedMod.Outfits.GetOrAddAsOverride(lastNonModOutfit.First().Record);
                        var overridenOtfts = Settings.Cache.ResolveAllContexts<IOutfit, IOutfitGetter>(winningOtft.FormKey)
                            .Where(c => Settings.UserSettings.ArmorModsForOutfits.ContainsKey(c.ModKey.FileName));
                        overridenOtfts.ForEach(r =>
                        {
                            var items = r.Record.Items.Select(x => Settings.Cache.Resolve<IItemGetter>(x.FormKey));
                            var ll = OutfitUtils.CreateLeveledList(PatchedMod, items, "ll_" + r.Record.EditorID + 0, 1, LeveledItem.Flag.UseAll);
                            oLLs.Add(ll);
                        });

                        // Creating patched outfit
                        LeveledItem sLL = OutfitUtils.CreateLeveledList(PatchedMod, oLLs.Distinct().Reverse(), "sll_" + outfit.EditorID, 1, Settings.LeveledListFlag);
                        Outfit newOutfit = PatchedMod.Outfits.AddNew(Settings.PatcherSettings.LeveledListPrefix + outfit.EditorID);
                        newOutfit.Items = new();
                        newOutfit.Items.Add(sLL);
                        AddOutfitToGroup(newOutfit);
                    }                
                }
            }
        }
        
        private void ProcessNpcsForOutfits()
        {
            ISkyrimMod patch = FileUtils.GetOrAddPatch(Settings.PatcherSettings.PatcherPrefix + "NPC Outfits.esp");
            //int processed = 0;
            //RandomSource rand = new();
            //ConcurrentDictionary<string, TNPC> npcs = new();
            //HashSet<string> missingId = new();

            //// NPC Patched Keyword and outfit Keywords
            //Keyword kywrd = patch.Keywords.AddNew(Patcher.OutfitPatchedKeywordEID);
            //Patcher.OutfitPatchedKeyword = kywrd.FormKey;
            //Dictionary<string, Keyword> otftKywrds = new();

            //List<FormKey> outfit2Skip = new();
            //Materials.Values.Select(x => x.Outfits).ForEach(x => outfit2Skip.AddRange(x.Keys));
            //Logger.InfoFormat("Outfit records processed...\n\n");

            //// Adding Armor sets to Mannequins
            //if (User.AddArmorsToMannequin)
            //{
            //    List<TArmorSet> armorSets = new();
            //    mergedOutfits.Values.ForEach(x => armorSets.AddRange(x.Armors));
            //    ArmorUtils.AddArmorsToMannequin(armorSets);
            //}

            //// Assiging outfits using SPID
            //List<string> lines = new();
            //string filters = User.FilterGurads ? "-0x"
            //    +Skyrim.Faction.GuardDialogueFaction.FormKey.IDString(): "NONE";

            //foreach (var k in otftKywrds.Keys) {
            //    var id = k.Replace("_OTFT","");
            //    mergedOutfits[id].GenderOutfit.ForEach(x=> {
            //        if (x.Value != FormKey.Null && x.Key!="U") {
            //            string line = "Outfit = 0x00" +
            //                  x.Value.ToString().Replace(":", " - ") +
            //                  " | " + k +
            //                  " | " + filters + 
            //                  " | NONE" +
            //                  " | " + (x.Key=="C"? "NONE ":x.Key) +
            //                  " | 1" +
            //                  " | 75";
            //            lines.Add(line);
            //        }
            //    });                
            //}

            //File.WriteAllLines(Path.Combine(State.DataFolderPath, "ZZZ_Patcher_NPC_OTFT_DISTR.ini"), lines);
            //all["Missing"] = missingId;
            //all["NPC"] = npcs;
            //Logger.InfoFormat("Total NPC records processed: {0}...\n\n", processed);
        }

        private void AddOutfitToGroup(IOutfitGetter outfit)
        {
            string eid = outfit.EditorID;
            var type = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.ArmorTypeRegex, eid);
            if (!type.Any()) type = new string[] { "" };

            var groups = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, eid);
            groups.ForEach(f =>
            {
                type.ForEach(eidType => {
                    var k = DividableFactions.Contains(f.ToLower()) ? f + eidType : f;
                    GrouppedArmorSets.GetOrAdd(k, new TArmorMaterial(k)).AddOutfit(outfit);
                });
            });
            if (groups.Any()) Logger.DebugFormat("Outfit Processed: {0}[{1}]", eid, outfit.FormKey);
            else Logger.DebugFormat("Outfit Missed: {0}[{1}]", eid, outfit.FormKey);
        }

        private void AddArmorSetToGroup(string group, TArmorSet armorSet)
        {
            if (Regex.IsMatch(group, "Jewelry|Shield", RegexOptions.IgnoreCase)) return;
            GrouppedArmorSets.GetOrAdd(group, new TArmorMaterial(group)).Armors.Add(armorSet);
        }

        private void FilterGaurdAndMaterialBasedOutfits() {
            Dictionary<FormKey, string> list = new();
            GrouppedArmorSets.Where(x => x.Key.EndsWith("Armor") || x.Key.EndsWith("Guards"))
                .Select(x => x.Value.Outfits)
                .ForEach(x => x.ForEach(o => {
                    if (Settings.PatcherSettings.Masters.Contains(o.Key.ModKey.FileName))
                        list.TryAdd(o.Key, o.Value);
                }));

            list.ForEach(o => GrouppedArmorSets
                .Where(x => !x.Key.EndsWith("Armor") && !x.Key.EndsWith("Guards"))
                .ToDictionary()
                .Values.ForEach(x => {
                    var common = x.Outfits.Where(x => list.ContainsKey(x.Key))
                             .ToDictionary(x => x.Key, x => x.Value);
                    common.ForEach(c => x.Outfits.TryRemove(c));
                })); ;
        }

        private void GenerateArmorSlotData()
        {
            Logger.InfoFormat("Generating armor meshes data...");
            var masters = Settings.UserSettings.ArmorModsForOutfits.Keys.ToList();
            masters.AddRange(State.LoadOrder.Where(x=> Settings.PatcherSettings.Masters.Contains(x.Key.FileName)).Select(x=>x.Key));

            var block = new ActionBlock<IArmorGetter>(
                armor =>
                {
                    string armorType = armor.BodyTemplate.ArmorType.ToString();
                    if (!ArmorsWithSlot.ContainsKey(armorType))
                        ArmorsWithSlot[armorType] = new();

                    if (IsEligibleForMeshMapping(armor, armorType)
                        && armor.Armature.FirstOrDefault().TryResolve<IArmorAddonGetter>(Settings.Cache, out var addon))
                    {
                        // Adding Armor sets
                        var slots = ArmorUtils.GetBodySlots(addon);
                        if (masters.Contains(armor.FormKey.ModKey.FileName))
                        {
                            slots.Select(x => x.ToString()).ForEach(slot => {
                                if (!ArmorsWithSlot[armorType].ContainsKey(slot)) 
                                    ArmorsWithSlot[armorType][slot] = new();
                                ArmorsWithSlot[armorType][slot].TryAdd(armor.FormKey, armor.Value);
                                
                            });
                        }
                    }
                }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 10 });

            foreach (IArmorGetter armor in State.LoadOrder.PriorityOrder
                .Where(x =>
                    !Settings.UserSettings.ModsToSkip.Contains(x.ModKey.FileName)
                    && !Settings.UserSettings.SleepingOutfitMods.Contains(x.ModKey.FileName)
                    && !Settings.UserSettings.JewelryMods.Contains(x.ModKey.FileName))
                .WinningOverrides<IArmorGetter>()
                .Where(x =>
                    ArmorUtils.IsValidArmor(x)
                    && x.Keywords != null && !x.HasKeyword(Skyrim.Keyword.ArmorJewelry)
                    && !x.HasKeyword(Skyrim.Keyword.ArmorShield)).EmptyIfNull())
            {
                block.Post(armor);
            }
            block.Complete();
            block.Completion.Wait();
            Logger.InfoFormat("Armor meshes data generated...\n\n");
        }

        private bool IsEligibleForMeshMapping(IArmorGetter armor, string material)
        {
            var groups = HelperUtils.GetRegexBasedGroup(Regex4outfits, material, armor.EditorID);
            var key = groups.Any() ? groups.First() : "Unknown";
            return GrouppedArmorSets.ContainsKey(key)
                    && armor.Armature != null
                    && armor.Armature.Count > 0
                    && !armor.HasKeyword(Skyrim.Keyword.ArmorHelmet)
                    && !armor.HasKeyword(Skyrim.Keyword.ArmorJewelry)
                    && !armor.HasKeyword(Skyrim.Keyword.ArmorShield)
                    && !armor.HasKeyword(Skyrim.Keyword.ClothingHead)
                    && !armor.HasKeyword(Skyrim.Keyword.ClothingCirclet)
                    && !armor.HasKeyword(Skyrim.Keyword.ClothingNecklace);
        }
    }
}
