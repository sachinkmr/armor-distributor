using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Noggog;
using System.IO;
using System.Text.RegularExpressions;
using System;
using System.Threading.Tasks.Dataflow;
using System.Collections.Concurrent;
using ArmorDistributor.Utils;
using System.Collections.Generic;
using System.Linq;
using ArmorDistributor.Armor;
using log4net;
using ArmorDistributor.Config;
using Mutagen.Bethesda.Plugins;
using System.Threading;

namespace ArmorDistributor.Managers
{
    public class OutfitManager
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(OutfitManager));

        private ISkyrimMod? OutfitMod;
        readonly IPatcherState<ISkyrimMod, ISkyrimModGetter> State;

        private List<string> SPID = new();
        private HashSet<string> OutfitsWithNPC = new();
        //private List<string> DividableFactions = new();
        private Dictionary<string, TArmorCategory> GrouppedArmorSets = new();
        private Dictionary<string, HashSet<string>> OutfitArmorTypes = new();
        private Dictionary<string, HashSet<string>> OutfitArmorTypesRev = new();
        private Dictionary<string, Dictionary<string, Dictionary<FormKey, float>>> ArmorsWithSlot = new();

        public OutfitManager(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            State = state;
            //DividableFactions = Settings.PatcherSettings.DividableFactions.ToLower().Split("|").ToList();
            OutfitMod = FileUtils.GetOrAddPatch(Settings.PatcherSettings.PatcherPrefix + "Outfits Part 1.esp");
        }

        public void Process()
        {
            Logger.InfoFormat("\n************ Starting Patcher ************");

            // Processing Armors and Armor Mods
            CategorizeArmors();
            CreateArmorsSets();

            // Processing Outfits
            GetPatchableOutfits();
            CategorizeOutfits();
            FilterGaurdAndMaterialBasedOutfits();
            
            ResolveOutfitOverrides();
            CreateNewOutfits();
            MergeOutfits();

            //CreateNPCKeywords();
            AddGroppedArmorSetToSPIDFile();

            // Writing data to SPID File
            SPID = SPID.Where(x => x.Trim().Any()).ToList();
            File.WriteAllLines(Path.Combine(State.DataFolderPath, Settings.IniName), SPID);
        }

        
        /**
         * Returns armor list with are associated with Outfits for Skyrim masters
         */
        private void CategorizeOutfits()
        {
            Logger.InfoFormat("Processing Outfits to Patch...");
            foreach (var outfit in State.LoadOrder.PriorityOrder
                .Where(x => !Settings.UserSettings.ModsToSkip.Contains(x.ModKey.FileName))
                .WinningOverrides<IOutfitGetter>()
                .Where(x => OutfitUtils.IsValidOutfit(x)))
            {
                // Creating Outfit Category based on ArmorType
                var types = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.ArmorTypeRegex, outfit.EditorID);
                if (!types.Any()) {
                    types = OutfitUtils.GetOutfitArmorType(outfit);
                }
                if (!types.Any()) {
                    types = new List<string>() { TArmorType.Unknown };
                }
                types.ForEach(x => {
                    OutfitArmorTypes.GetOrAdd(x).Add(outfit.EditorID);
                    OutfitArmorTypesRev.GetOrAdd(outfit.EditorID).Add(x);
                });
                
                if(!OutfitsWithNPC.Contains(outfit.EditorID)) AddOutfitToGroup(outfit);

            }
            Logger.InfoFormat("Processed Outfits to Patch...");
        }

        /**
         * Returns a dictonary with outfit and number of times those are used.
         */

        private void GetPatchableOutfits()
        {
            Logger.InfoFormat("Fetching outfit records...");
            Dictionary<string, HashSet<FormKey>> outfits = new();
            State.LoadOrder.PriorityOrder
                .Where(x => !Settings.UserSettings.ModsToSkip.Contains(x.ModKey.FileName))
                .WinningOverrides<INpcGetter>().EmptyIfNull()
                .ForEach(npc =>
                {
                    var npcs = Settings.Cache.ResolveAllContexts<INpc, INpcGetter>(npc.FormKey);
                    npcs.ForEach(n => { 
                        var record = n.Record;
                        if (record.DefaultOutfit.TryResolve<IOutfitGetter>(Settings.Cache, out IOutfitGetter otft))
                        {
                            string otfteid = otft.EditorID;
                            outfits.GetOrAdd(otfteid).Add(npc.FormKey);
                        }
                    });
                    //mapping outfits
                });

            foreach (IOutfitGetter outfit in State.LoadOrder.PriorityOrder
                .Where(x => !Settings.UserSettings.ModsToSkip.Contains(x.ModKey.FileName))
                .WinningOverrides<IOutfitGetter>()
                .Where(x => OutfitUtils.IsValidOutfit(x)
                    && outfits.ContainsKey(x.EditorID) && outfits[x.EditorID].Count()>1))
            {
                AddOutfitToGroup(outfit);
            }

            OutfitsWithNPC = new(outfits.Keys);
            Logger.InfoFormat("Outfit records grouped...\n\n");
        }

        /*
         * Filter outfits based on the its uses count
         */
        private void GroupOutfits()
        {
            Logger.InfoFormat("Getting outfits to be patched....");
            foreach (IOutfitGetter outfit in State.LoadOrder.PriorityOrder
                .Where(x => !Settings.UserSettings.ModsToSkip.Contains(x.ModKey.FileName))
                .WinningOverrides<IOutfitGetter>()
                .Where(x => OutfitUtils.IsValidOutfit(x)))
            {
                AddOutfitToGroup(outfit);
            }
            Logger.InfoFormat("Outfits are categorized for patching....\n\n");
        }


        /* 
         * Creates aremor sets baed on material and provided keywords
         */
        private void CreateArmorsSets()
        {
            Logger.InfoFormat("Creating matching armor sets for armor mods...");
            // For each armor mod creating armor sets, skipping mods which have the outfit records.
            // If these outfit records are overriden it will be resolved later in the patch
            var modlists = State.LoadOrder.PriorityOrder
                .Where(x => Settings.UserSettings.ArmorModsForOutfits.ContainsKey(x.ModKey.FileName)
                    && x.Mod.Armors.Count > 0);

            var patchName = Settings.PatcherSettings.PatcherPrefix + "Armors Part 1.esp";
            ISkyrimMod patch = FileUtils.GetOrAddPatch(patchName);
            Keyword kywrd = patch.Keywords.AddNew("ArmorTypeADP");
            List<ModKey> masters = new();
            var totalSets = 0;

            foreach (var mod in modlists.Select(x => x.Mod))
            {
                List<IArmorGetter> bodies = new();
                List<TArmor> others = new();
                List<TArmor> jewelries = new();

                mod.Armors
                    .Where(x => ArmorUtils.IsValidArmor(x)
                        && x.Keywords != null)
                    .ForEach(armor =>
                    {
                        //Mutagen.Bethesda.Skyrim.Armor armor = patch.Armors.GetOrAddAsOverride(a);
                        //armor.Keywords.Add(kywrd);

                        if (ArmorUtils.IsBodyArmor(armor)) bodies.Add(armor);
                        else if (ArmorUtils.IsJewelry(armor)) jewelries.Add(new(armor));
                        else others.Add(new(armor));

                        if (!masters.Contains(armor.FormKey.ModKey))
                        {
                            if (masters.Count() > 250)
                            {
                                patch = FileUtils.GetIncrementedMod(patch);
                                masters.Clear();
                            }
                            masters.Add(armor.FormKey.ModKey);
                        }
                    });

                int bodyCount = bodies.Count;
                totalSets += bodyCount;

                var commanName = 0;
                var commanEID = 0;
                if (bodyCount > 5 && Settings.UserSettings.ArmorModsForOutfits[mod.ModKey.FileName].Contains("Generic")) {
                    commanName = HelperUtils.GetCommonItems(others.Select(x => HelperUtils.SplitString(x.Name)).ToList())
                               .Where(x => !x.IsNullOrEmpty()).Count();
                    commanEID = HelperUtils.GetCommonItems(others.Select(x => HelperUtils.SplitString(x.EditorID)).ToList())
                               .Where(x => !x.IsNullOrEmpty()).Count();
                }

                var armorGroups = others.GroupBy(x => x.Type)
                    .ToDictionary(x => x.Key, x => x.Select(a => a).GroupBy(a => a.Material).ToDictionary(a => a.Key, a => a.Select(b => b)));
                for (int i = 0; i < bodyCount; i++)
                {
                    // Creating armor sets and LLs
                    var body = bodies.ElementAt(i);
                    TArmorSet armorSet = new(body, patch);
                    List<TArmor> armors = armorGroups.GetOrAdd(armorSet.Type)
                            .GetOrDefault(armorSet.Material).EmptyIfNull()
                            .ToList();

                    armors.AddRange(jewelries);
                    armorSet.CreateMatchingSetFrom(armors, bodyCount == 1, commanName, commanEID);

                    // Distributing weapons as well
                    if (mod.Weapons != null && mod.Weapons.Count > 0)
                        armorSet.CreateMatchingSetFrom(mod.Weapons, bodyCount);

                    // Checking for Boots
                    if (!armorSet.Armors.Where(x => x.BodySlots.Contains(TBodySlot.Feet)).Any())
                    {
                        string type = armorSet.Type;
                        var feetsAll = ArmorsWithSlot[type][TBodySlot.Feet.ToString()]
                            .OrderBy(x => x.Value);
                        var feets = feetsAll.Where(x => x.Value <= body.Value);
                        var feet = feets.Any() ? feets.First().Key : feetsAll.First().Key;
                        armorSet.AddArmor(new TArmor(Settings.Cache.Resolve<IArmorGetter>(feet), armorSet.Material));
                    }

                    patch = FileUtils.GetIncrementedMod(patch);
                    armorSet.CreateLeveledList();

                    // Add using materials
                    var group = armorSet.Material;
                    AddArmorSetToGroup(group, armorSet);

                    // Add using provided category
                    List<string> modsCategories = Settings.UserSettings.ArmorModsForOutfits[mod.ModKey.FileName];
                    modsCategories.Remove("Generic");
                    foreach (string fgroup in modsCategories.Distinct())
                    {
                        AddArmorSetToGroup(fgroup, armorSet);
                    }

                    if (i > 0 && (i + 1) % 100 == 0)
                        Logger.InfoFormat("Created {0}/{1} armor-set for: {2}", i + 1, bodyCount, mod.ModKey.FileName);
                }
                Logger.InfoFormat("Created {0}/{0} armor-set for: {1}", bodyCount, mod.ModKey.FileName);
            }
            Logger.InfoFormat("Created {0} matching armor sets from armor mods...\n", totalSets);
        }

        private void CreateNewOutfits()
        {
            Logger.InfoFormat("Creating New Outfits...");
            if (GrouppedArmorSets.Remove("Unknown", out var temp)) {
                var types = temp.Armors.GroupBy(a => a.Type).ToDictionary(x=>x.Key, x=>x.Select(b=>b));
                foreach (var t in types) {
                    if (t.Key == TArmorType.Cloth)
                    {
                        GrouppedArmorSets.GetOrAdd("Traveller", () => new TArmorCategory("Traveller")).AddArmorSets(temp.Armors);
                        GrouppedArmorSets.GetOrAdd("Merchant", () => new TArmorCategory("Merchant")).AddArmorSets(temp.Armors);
                    }
                    else {
                        GrouppedArmorSets.GetOrAdd("Bandit", () => new TArmorCategory("Bandit")).AddArmorSets(t.Value);
                        GrouppedArmorSets.GetOrAdd("Warrior", () => new TArmorCategory("Warrior")).AddArmorSets(t.Value);
                        GrouppedArmorSets.GetOrAdd("Mercenary", () => new TArmorCategory("Mercenary")).AddArmorSets(t.Value);
                    }
                }                
            }

            GrouppedArmorSets = new(GrouppedArmorSets.Where(x => !x.Value.Armors.IsEmpty));
            GrouppedArmorSets.ForEach(rec =>
            {
                OutfitMod = FileUtils.GetIncrementedMod(OutfitMod);
                rec.Value.CreateOutfits(OutfitMod);
                Logger.DebugFormat("Created new outfit record for: " + rec.Key);
            });
        }

        private void ResolveOutfitOverrides()
        {
            Logger.InfoFormat("\nResolving outfit conflicts...");
            var outfitContext = State.LoadOrder.PriorityOrder.Outfit()
               .WinningContextOverrides();

            foreach (var outfit in State.LoadOrder.PriorityOrder
                .Where(x => Settings.UserSettings.ArmorModsForOutfits.ContainsKey(x.ModKey.FileName))
                .WinningOverrides<IOutfitGetter>())
            {

                var winningOtfts = outfitContext.Where(c => c.Record.FormKey == outfit.FormKey).EmptyIfNull();
                if (winningOtfts.Any())
                {
                    List<IItemGetter> oLLs = new();
                    var winningOtft = winningOtfts.First().Record;

                    // Merging outfit's lvls from the armor mods together
                    var overridenOtfts = Settings.Cache.ResolveAllContexts<IOutfit, IOutfitGetter>(winningOtft.FormKey)
                            .Where(c => Settings.UserSettings.ArmorModsForOutfits.ContainsKey(c.ModKey.FileName));

                    overridenOtfts.ForEach(r =>
                    {
                        var items = r.Record.Items.Select(x => Settings.Cache.Resolve<IItemGetter>(x.FormKey));
                        if (items.Count() == 1)
                        {
                            oLLs.Add(items.First());
                        }
                        else
                        {
                            OutfitMod = FileUtils.GetIncrementedMod(OutfitMod);
                            var ll = OutfitUtils.CreateLeveledList(OutfitMod, items, "ll_" + r.Record.EditorID + 0, 1, LeveledItem.Flag.UseAll);
                            oLLs.Add(ll);
                        }
                    });

                    // Reverting Overriden outfit by armor mods added in the patcher
                    var lastNonModOutfit = Settings.Cache.ResolveAllContexts<IOutfit, IOutfitGetter>(winningOtft.FormKey)
                        .Where(c => !Settings.UserSettings.ArmorModsForOutfits.ContainsKey(c.ModKey.FileName));

                    if (lastNonModOutfit.Count() > 1)
                    {
                        // Getting outfit records form armor mods added in the patcher and patching those
                        Outfit nOutfit = OutfitMod.Outfits.GetOrAddAsOverride(lastNonModOutfit.First().Record);
                        var items = nOutfit.Items.Select(x => Settings.Cache.Resolve<IItemGetter>(x.FormKey));
                        if (items.Count() == 1)
                        {
                            oLLs.Add(items.First());
                        }
                        else
                        {
                            OutfitMod = FileUtils.GetIncrementedMod(OutfitMod);
                            var ll = OutfitUtils.CreateLeveledList(OutfitMod, items, "ll_" + nOutfit.EditorID + 0, 1, LeveledItem.Flag.UseAll);
                            oLLs.Add(ll);
                        }

                        // Creating patched outfit
                        OutfitMod = FileUtils.GetIncrementedMod(OutfitMod);
                        LeveledItem sLL = OutfitUtils.CreateLeveledList(OutfitMod, oLLs.Distinct(), "sll_" + outfit.EditorID, 1, Settings.LeveledListFlag);
                        nOutfit.Items = new();
                        nOutfit.Items.Add(sLL);
                    }
                }
            }
        }

        private void MergeOutfits()
        {
            // Merging Default outfits with their respective categories
            foreach (var group in GrouppedArmorSets) {
                var category = group.Value.GenderOutfit.GetOrAdd("C");
                Dictionary<string, List<IItemGetter>> dict = new();
                group.Value.Outfits.Keys.ForEach(o=> {
                    List<IItemGetter> list = new();
                    var otft = State.LinkCache.Resolve<IOutfitGetter>(o);
                    var types = OutfitArmorTypesRev.ContainsKey(otft.EditorID) ? OutfitArmorTypesRev[otft.EditorID]
                        : HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.ArmorTypeRegex, otft.EditorID);
                    var itms = otft.Items.Select(i=> State.LinkCache.Resolve<IItemGetter>(i.FormKey));
                    if (itms.Count() == 1) list.AddRange(itms);
                    else {
                        OutfitMod = FileUtils.GetIncrementedMod(OutfitMod);
                        var ll = OutfitUtils.CreateLeveledList(OutfitMod, itms, "LL_" + otft.EditorID, 1, LeveledItem.Flag.UseAll);
                        list.Add(ll);
                    }
                    if (OutfitArmorTypesRev.ContainsKey(otft.EditorID))
                        OutfitArmorTypesRev[otft.EditorID].ForEach(t => dict.GetOrAdd(t).AddRange(list));
                    else {
                        Logger.InfoFormat("outfit category not found: {0}:{1}",otft.EditorID, otft.FormKey.ToString());
                    }
                });

                var cache = State.LoadOrder.ToMutableLinkCache();
                dict.ForEach(t => {
                    OutfitMod = FileUtils.GetIncrementedMod(OutfitMod);
                    if (category.ContainsKey(t.Key))
                    {
                        var llFormKey = cache.Resolve<IOutfitGetter>(category[t.Key]).Items.First().FormKey;
                        var ll = cache.Resolve<LeveledItem>(llFormKey);
                        OutfitUtils.AddItemsToLeveledList(OutfitMod, ll, t.Value, 1);
                    }
                    else {                       
                        var id = group.Key + "_C_" + t.Key;
                        Outfit newOutfit = OutfitUtils.CreateOutfit(OutfitMod, id, t.Value);
                        category.Add(t.Key, newOutfit.FormKey);
                    }                       
                });
            }

            // Merging outfits which are not assigned to any NPC
            var mCache = State.LoadOrder.ToMutableLinkCache();
            var outfits = State.LoadOrder.PriorityOrder
                .Where(l => !l.ModKey.FileName.String.StartsWith(Settings.PatcherSettings.PatcherPrefix) && !Settings.UserSettings.ModsToSkip.Contains(l.ModKey.FileName))
                .WinningOverrides<IOutfitGetter>()
                .Where(o => OutfitUtils.IsValidOutfit(o) && !OutfitsWithNPC.Contains(o.EditorID))
                .OrderBy(o=>o.EditorID);
                //.Where(o => o.EditorID.EndsWith(Settings.PatcherSettings.OutfitSuffix) && o.EditorID.StartsWith(Settings.PatcherSettings.OutfitPrefix));
                //.ToDictionary(o=>Regex.Replace(o.EditorID, Settings.PatcherSettings.OutfitSuffix + "|" + Settings.PatcherSettings.OutfitPrefix, ""), o=>o.FormKey);

            foreach (var otft in outfits) {
                if (otft.EditorID.Contains(Settings.PatcherSettings.OutfitSuffix))
                {
                    var tokens = Regex.Replace(otft.EditorID, Settings.PatcherSettings.OutfitSuffix + "|" + Settings.PatcherSettings.OutfitPrefix, "").Split('_');
                    var category = tokens[0];
                    var gender = tokens[1];
                    var type = tokens[2];
                    var genderotft = GrouppedArmorSets.GetOrAdd(category, () => new TArmorCategory(category)).GenderOutfit.GetOrAdd(gender);
                    if (genderotft.ContainsKey(type))
                    {
                        // Getting already added LL
                        LeveledItem ll = null;
                        IOutfitGetter o = mCache.Resolve<IOutfitGetter>(genderotft[type]);
                        var itms = o.Items.Select(i => mCache.Resolve<IItemGetter>(i.FormKey));
                        if (itms.Count() == 1 && itms.All(i => i is ILeveledItemGetter))
                            ll = OutfitMod.LeveledItems.GetOrAddAsOverride((ILeveledItemGetter)itms.First());
                        else
                            ll = OutfitUtils.CreateLeveledList(OutfitMod, itms, "LL_" + String.Join("_", tokens) + "1", 1, Settings.LeveledListFlag);

                        // Adding items to the above leveled list
                        itms = otft.Items.Select(i => mCache.Resolve<IItemGetter>(i.FormKey));
                        OutfitUtils.AddItemsToLeveledList(OutfitMod, ll, itms, 1);
                    }
                    else
                    {
                        genderotft.Add(type, otft.FormKey);
                    }
                }
                else {

                    //TODO: Pull Armor from the non NPC Outfits
                    //var categories = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, otft.EditorID);
                    //foreach (var category in categories) { 

                    //}
                    Logger.DebugFormat("Non NPC Outfit: {0}::{1}",otft.EditorID, otft.FormKey.ToString());
                }
            }
        }


        private void AddGroppedArmorSetToSPIDFile()
        {
            Logger.InfoFormat("Creating SPID ini file...");

            // Distributing Outfits
            var percentage = Int32.Parse(Settings.UserSettings.DefaultOutfitPercentage.ToString().Substring(1));
            var sets = GrouppedArmorSets.Where(x => x.Value.Outfits.Any()).OrderBy(x => x.Key);
            foreach (var pair in sets)
            {
                GrouppedArmorSets[pair.Key].GenderOutfit.Where(g => g.Value != null && g.Value.Any())
                    .ForEach(x =>
                    {
                        var gender = x.Key == "M" ? "M" : x.Key == "C" ? "NONE" : "F";
                        gender = Settings.UserSettings.FilterUniqueNPC ? gender : gender + "/U";
                        foreach (var armorType in x.Value.Keys)
                        {
                            var outfits = GrouppedArmorSets[pair.Key].Outfits
                                .Where(o => OutfitArmorTypes[armorType].Contains(o.Value))
                                .Select(o => "0x" + o.Key.ID.ToString("X") + "~" + o.Key.ModKey.FileName).ToList();

                            if (!outfits.Any()) continue;
                            var outfit = x.Value[armorType];

                            SPID.Add(string.Format("\n;Outfits for {0}{1}/{2}", pair.Key, armorType, gender.Replace("NONE", "M+F").Replace("/U", "")));
                            string line = String.Format("Outfit = 0x{0}~{1}|{2}|{3}|NONE|{4}|NONE|{5}",
                            outfit.ID.ToString("X"), outfit.ModKey.FileName, "NONE", string.Join(",", outfits), gender, 100 - percentage);
                            SPID.Add(line);
                            SPID.Add(line.Replace("/U|NONE", "|NONE"));
                        }
                        SPID.Add(Environment.NewLine);
                    });
            }
        }

        private void AddOutfitToGroup(IOutfitGetter outfit)
        {
            string eid = outfit.EditorID;
            var groups = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, eid);
            groups.ForEach(group => GrouppedArmorSets.GetOrAdd(group, () => new TArmorCategory(group)).AddOutfit(outfit));

            //var type = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.ArmorTypeRegex, eid);
            //if (!type.Any()) type = new string[] { "" };

            //groups.ForEach(f =>
            //{
            //    type.ForEach(eidType =>
            //    {
            //        var k = DividableFactions.Contains(f.ToLower()) ? f + eidType : f;
            //        GrouppedArmorSets.GetOrAdd(k, () => new TArmorCategory(k)).AddOutfit(outfit);
            //    });
            //});
            if (groups.Any()) Logger.DebugFormat("Outfit Processed: {0}[{1}]", eid, outfit.FormKey);
            else Logger.DebugFormat("Outfit Missed: {0}[{1}]", eid, outfit.FormKey);
        }

        private void AddArmorSetToGroup(string group, TArmorSet armorSet)
        {
            GrouppedArmorSets.GetOrAdd(group, ()=>new TArmorCategory(group)).Armors.Add(armorSet);
        }

        private void FilterGaurdAndMaterialBasedOutfits()
        {
            Logger.InfoFormat("Filtering guard and armor outfits from other outfits...");
            Dictionary<FormKey, string> list = new();
            GrouppedArmorSets.Where(x => x.Key.EndsWith("Armor") || x.Key.EndsWith("Guards"))
                .Select(x => x.Value.Outfits)
                .ForEach(x => x.ForEach(o =>
                {
                    if (Settings.PatcherSettings.Masters.Contains(o.Key.ModKey.FileName))
                        list.TryAdd(o.Key, o.Value);
                }));

            list.ForEach(o => GrouppedArmorSets
                .Where(x => !x.Key.EndsWith("Armor") && !x.Key.EndsWith("Guards"))
                .ToDictionary()
                .Values.ForEach(x =>
                {
                    var common = x.Outfits.Where(x => list.ContainsKey(x.Key))
                             .ToDictionary(x => x.Key, x => x.Value);
                    common.ForEach(c => x.Outfits.TryRemove(c));
                })); ;
        }

        private void CategorizeArmors()
        {
            Logger.InfoFormat("Categorizing armors based on body slots...");
            foreach (IArmorGetter armor in State.LoadOrder.PriorityOrder
                .WinningOverrides<IArmorGetter>()
                .Where(x=>ArmorUtils.IsValidArmor(x)))
            {
                string armorType = ArmorUtils.GetArmorType(armor);
                if (armor.Armature.FirstOrDefault().TryResolve<IArmorAddonGetter>(Settings.Cache, out var addon))
                {
                    // Adding Armor sets
                    var slots = ArmorUtils.GetBodySlots(addon);
                    slots.Select(x => x.ToString()).ForEach(slot =>
                    {
                        ArmorsWithSlot.GetOrAdd(armorType).GetOrAdd(slot).TryAdd(armor.FormKey, armor.Value);
                    });
                }
            }
        }

        private bool IsEligibleForMeshMapping(IArmorGetter armor, string material)
        {
            var groups = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, material, armor.EditorID);
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
