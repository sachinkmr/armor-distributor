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

namespace ArmorDistributor.Managers
{
    public class OutfitManager
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(OutfitManager));

        private ISkyrimMod? PatchedMod;
        readonly IPatcherState<ISkyrimMod, ISkyrimModGetter> State;

        private List<string> SPID = new();
        private List<string> DividableFactions = new();
        private ConcurrentBag<FormKey> ArmorsWithOutfit = new();
        private ConcurrentDictionary<string, TArmorGroupable> GrouppedArmorSets = new();
        private readonly Dictionary<string, string> Regex4outfits = Settings.PatcherSettings.OutfitRegex;
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
            ArmorsWithOutfit = IsPatchableArmor();
            CreateArmorsSets();

            ResolveOutfitOverrides();
            FilterGaurdAndMaterialBasedOutfits();
            CreateNewOutfits();

            CreateNPCKeywords();
            AddGroppedArmorSetToSPIDFile();
            File.WriteAllLines(Path.Combine(State.DataFolderPath, Settings.IniName), SPID);
        }

        /**
         * Returns armor list with are associated with Outfits
         */
        private ConcurrentBag<FormKey> IsPatchableArmor()
        {
            Logger.InfoFormat("");
            ConcurrentBag<FormKey> ArmorsFormKey = new();
            var block = new ActionBlock<IOutfitGetter>(
                outfit =>
                {
                    List<IArmorGetter> armors = new();
                    outfit.ContainedFormLinks.ForEach(x=> {
                        var item = State.LinkCache.Resolve<IItemGetter>(x.FormKey);
                        OutfitUtils.GetArmorList(item, armors, ArmorsFormKey);
                    });
                }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 10 });

            foreach (var outfit in State.LoadOrder.PriorityOrder.WinningOverrides<IOutfitGetter>()
                .Where(x => Settings.PatcherSettings.Masters.Contains(x.FormKey.ModKey.FileName))) {
                block.Post(outfit);
            }
            block.Complete();
            block.Completion.Wait();
            return ArmorsFormKey;
        }

        /**
         * Returns a dictonary with outfit and number of times those are used.
         */
        
        private Dictionary<string, int> GetPatchableOutfits()
        {
            Logger.InfoFormat("Fetching outfit records...");
            Dictionary<string, int> outfits = new();
            State.LoadOrder.PriorityOrder
                .Where(x => !Settings.UserSettings.ModsToSkip.Contains(x.ModKey.FileName))
                .WinningOverrides<INpcGetter>().EmptyIfNull()
                .ForEach(npc =>
                {
                    //mapping outfits
                    if (npc.DefaultOutfit.TryResolve<IOutfitGetter>(Settings.Cache, out IOutfitGetter otft))
                    {
                        string otfteid = otft.EditorID;
                        int val = outfits.ContainsKey(otfteid)
                            ? outfits.GetValueOrDefault(otfteid) : 0;
                        outfits[otfteid] = val + 1;
                    }
                });
            Logger.InfoFormat("Outfit records grouped...\n\n");
            return outfits;
        }


        /*
         * Filter outfits based on the its uses count
         */ 
        private void GroupOutfits(Dictionary<string, int> AllowedOutfits)
        {
            Logger.InfoFormat("Getting outfits to be patched based on armor materials....");
            // Filtering outfits with more than 3 references
            var dict = AllowedOutfits
                .Where(x => AllowedOutfits.GetValueOrDefault(x.Key) > 0
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


        /* 
         * Creates aremor sets baed on material and provided keywords
         */
        private void CreateArmorsSets()
        {
            Logger.InfoFormat("Grouping Armors....");
            // For each armor mod creating armor sets, skipping mods which have the outfit records.
            // If these outfit records are overriden it will be resolved later in the patch
            var modlists = State.LoadOrder.PriorityOrder
                .Where(x => Settings.UserSettings.ArmorModsForOutfits.ContainsKey(x.ModKey.FileName)
                    && x.Mod.Armors.Count > 0);
            int r = 1;
            var patchName = Settings.PatcherSettings.PatcherPrefix + "Armors Part " + r++ + ".esp";
            ISkyrimMod patch = FileUtils.GetOrAddPatch(patchName);
            ConcurrentBag<ModKey> masters = new();            

            for (int m= 0; m < modlists.Count(); m++)
            {
                ISkyrimModGetter mod = modlists.ElementAt(m).Mod;
                List<IArmorGetter> bodies = new();
                List<IArmorGetter> others = new();

                mod.Armors
                    .Where(x => ArmorUtils.IsValidArmor(x)
                        && x.Keywords != null)
                    .ForEach(armor =>
                    {
                        if (ArmorUtils.IsBodyArmor(armor))
                            if (!ArmorsWithOutfit.Contains(armor.FormKey))
                                bodies.Add(armor);
                            else
                                others.Add(armor);
                        
                        if (!masters.Contains(armor.FormKey.ModKey))
                        {
                            if (masters.Count() > 250)
                            {
                                patchName = Settings.PatcherSettings.PatcherPrefix + "Armors Part " + r++ + ".esp";
                                patch = FileUtils.GetOrAddPatch(patchName);
                                masters.Clear();
                            }
                            masters.Add(armor.FormKey.ModKey);
                        }
                    });

                int bodyCount = bodies.Count;
                for (int i = 0; i < bodyCount; i++)
                {
                    // Creating armor sets and LLs
                    var body = bodies.ElementAt(i);
                    TArmorSet armorSet = new(body, patch);
                    armorSet.CreateMatchingSetFrom(others, bodyCount == 1);

                    // Distributing weapons as well
                    if (mod.Weapons != null && mod.Weapons.Count > 0)
                        armorSet.CreateMatchingSetFrom(mod.Weapons, bodyCount);

                    // Checking for Boots
                    if (!armorSet.Armors.Where(x => x.BodySlots.Contains(TBodySlot.Feet)).Any())
                    {
                        string type = body.BodyTemplate.ArmorType.ToString();
                        var feetsAll = ArmorsWithSlot[type][TBodySlot.Feet.ToString()]
                            .OrderBy(x => x.Value);
                        var feets = feetsAll.Where(x => x.Value <= body.Value);
                        var feet = feets.Any() ? feets.First().Key : feetsAll.First().Key;
                        armorSet.AddArmor(new TArmor(Settings.Cache.Resolve<IArmorGetter>(feet), armorSet.Material));
                    }

                    armorSet.CreateLeveledList();                    
                    if (patch.EnumerateMajorRecords().Where(x => x.FormKey.ModKey.Equals(patch.ModKey)).Count() > 2045)
                    {
                        patchName = Settings.PatcherSettings.PatcherPrefix + "Armors Part " + r++ + ".esp";
                        patch = FileUtils.GetOrAddPatch(patchName);
                    }                    

                    // Add using materials
                    var group = armorSet.Material;
                    AddArmorSetToGroup(group, armorSet);

                    // Add using provided category
                    List<string> modsFactions = Settings.UserSettings.ArmorModsForOutfits[mod.ModKey.FileName];
                    modsFactions.Remove("Generic");
                    foreach (string fgroup in modsFactions)
                    {
                        //var fg = DividableFactions.Contains(fgroup.ToLower())
                        //    ? fgroup + armorSet.Type : fgroup;
                        //AddArmorSetToGroup(fg, armorSet);
                        AddArmorSetToGroup(fgroup, armorSet);
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
                if (set.Type == TArmorType.Heavy)
                {
                    if (GrouppedArmorSets.ContainsKey("BanditHeavy")) GrouppedArmorSets["BanditHeavy"].Armors.Add(set);
                    if (GrouppedArmorSets.ContainsKey("MercenaryHeavy")) GrouppedArmorSets["MercenaryHeavy"].Armors.Add(set);
                }
                else if (set.Type == TArmorType.Light)                   
                {
                    if (GrouppedArmorSets.ContainsKey("BanditLight")) GrouppedArmorSets["BanditLight"].Armors.Add(set);
                    if (GrouppedArmorSets.ContainsKey("MercenaryLight")) GrouppedArmorSets["MercenaryLight"].Armors.Add(set);
                }
                else if (set.Type == TArmorType.Wizard) 
                {
                    if (GrouppedArmorSets.ContainsKey("BanditWizard")) GrouppedArmorSets["BanditWizard"].Armors.Add(set); 
                    if (GrouppedArmorSets.ContainsKey("MercenaryWizard")) GrouppedArmorSets["MercenaryWizard"].Armors.Add(set);
                }
                else if (set.Type == TArmorType.Cloth)
                {
                    if (GrouppedArmorSets.ContainsKey("Citizen")) GrouppedArmorSets["Citizen"].Armors.Add(set);
                }
            });

            GrouppedArmorSets.TryRemove("Unknown", out var temp);
            GrouppedArmorSets = new(GrouppedArmorSets.Where(x => !x.Value.Armors.IsEmpty));
            GrouppedArmorSets.ForEach(rec =>
            {
                rec.Value.CreateOutfits(PatchedMod);
                Logger.InfoFormat("Created new outfit record for: " + rec.Key);
            });
            Logger.InfoFormat("Added new outfits records...\n\n");
        }

        private void ResolveOutfitOverrides() {
            var outfitContext = State.LoadOrder.PriorityOrder.Outfit()
               .WinningContextOverrides();

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

                    List<LeveledItem> oLLs = new();
                    if (lastNonModOutfit.Count() > 1)
                    {
                        // Getting outfit records form armor mods added in the patcher and patching those
                        PatchedMod.Outfits.GetOrAddAsOverride(lastNonModOutfit.First().Record);                        
                    }

                    // Merging lvls from the armor mods together
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

        /**
         * Assign outfit and combat style related keywords to NPC
         */
        private void CreateNPCKeywords()
        {
            Dictionary<FormKey, HashSet<FormKey>> outfitAndNPCs = new();
            Dictionary<string, HashSet<FormKey>> combatStyles = new();
            var regex = Settings.PatcherSettings.ArmorTypeRegex.ToDictionary();
            Settings.PatcherSettings.CombatStyleRegex.ForEach(pair => regex[pair.Key]=pair.Value);

            State.LoadOrder.PriorityOrder.WinningOverrides<INpcGetter>()
                .ForEach(npc =>
                {

                    // Outfit assignment
                    //if(npc.DefaultOutfit!=null && !npc.DefaultOutfit.FormKey.IsNull){
                    //    outfitAndNPCs.GetOrAdd(npc.DefaultOutfit.FormKey).Add(npc.FormKey);
                    //}

                    // Combat style assignment
                    if (npc.CombatStyle != null && !npc.CombatStyle.FormKey.IsNull)
                    {
                        var cs = State.LinkCache.Resolve<ICombatStyleGetter>(npc.CombatStyle.FormKey);
                        HelperUtils.GetRegexBasedGroup(regex, cs.EditorID)
                        .ForEach(c => combatStyles.GetOrAdd(c).Add(npc.FormKey));
                    }
                });

            //SPID.Add(";Keywords for outfits");
            //GrouppedArmorSets.ForEach(pair => {
            //    string line = "Keyword = OutfitType" + pair.Key + "|";

            //    List<string> npcs = new();
            //    if (pair.Key.Contains("Race")) npcs.Add(pair.Key.Replace("Race", ""));

            //    pair.Value.Outfits.ForEach(o => {
            //        if (outfitAndNPCs.ContainsKey(o.Key))
            //            npcs.AddRange(outfitAndNPCs[o.Key].Select(n => "0x" + n.IDString() + "~" + n.ModKey.FileName));
            //    });

            //    line += string.Join(",", npcs);
            //    if(npcs.Any()) SPID.Add(line);
            //});

            SPID.Add("\n\n;Keywords for Combat Style");
            State.LoadOrder.PriorityOrder.WinningOverrides<ICombatStyleGetter>().ForEach(r =>
            {
                HelperUtils.GetRegexBasedGroup(regex, r.EditorID)
                        .ForEach(c => combatStyles.GetOrAdd(c).Add(r.FormKey));
            });


            combatStyles.ForEach(p => {
                string line = "Keyword = CombatStyle" + p.Key + "|";
                List<string> styles = new();
                p.Value.ForEach(n => styles.Add("0x" + n.IDString() + "~" + n.ModKey.FileName));
                line += string.Join(",", styles);
                if (styles.Any()) SPID.Add(line);
            });

            SPID.Add("\n\n\n");
        }

        //TODO: Figure out the order
        private void AddGroppedArmorSetToSPIDFile()
        {
            // Distributing Outfits
            var percentage = Int32.Parse(Settings.UserSettings.DefaultOutfitPercentage.ToString().Substring(1));
            var sets = GrouppedArmorSets.Where(x => x.Value.Outfits.Any()).OrderBy(x=>x.Key);
            foreach (var pair in sets) {

                SPID.Add(string.Format("; Outfits for {0}", pair.Key));
                GrouppedArmorSets[pair.Key].GenderOutfit.Where(g=>g.Value!=null && g.Value.Any())
                    .ForEach(x=> {
                        var gender = x.Key == "M" ? "M" : x.Key == "C" ? "NONE" : "F";
                        gender = Settings.UserSettings.FilterUniqueNPC ? gender : gender + "/U";
                        
                        var outfits = GrouppedArmorSets[pair.Key].Outfits.Keys
                        .Select(x => "0x" + x.IDString() + "~" + x.ModKey.FileName).ToList();
                        foreach (var armorType in x.Value.Keys) {
                            var outfit = x.Value[armorType];
                            //var keywords = "OutfitType" + pair.Key + (armorType != TArmorType.Cloth ? "+CombatStyle" + armorType : "");
                            string keywords = "NONE";
                            if (armorType == TArmorType.Wizard) keywords = "CombatStyle" + armorType;
                            if (armorType == TArmorType.Heavy) keywords = "CombatStyle" + armorType;
                            if (armorType == TArmorType.Light) keywords = "CombatStyle" + armorType;

                            string line = String.Format("Outfit = 0x{0}~{1}|{2}|{3}|NONE|{4}|NONE|{5}",
                            outfit.IDString(), outfit.ModKey.FileName, keywords, string.Join(",", outfits), gender, 100 - percentage);
                            SPID.Add(line);
                        }
                });
                SPID.Add("\n");
            }
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
            masters.AddRange(State.LoadOrder.Where(x=> Settings.PatcherSettings.Masters.Contains(x.Key.FileName)).Select(x=>x.Key.FileName.ToString()));

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
