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
using static OutFitPatcher.Config.Configuration;
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

        private readonly Dictionary<string, int> AllowedOutfits = new();
        private ConcurrentDictionary<string, TArmorGroupable> Materials = new();
        private ConcurrentDictionary<string, TArmorGroupable> Factions = new();
        private readonly Dictionary<string, string> Regex4outfits = Patcher.OutfitRegex;
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> MaleArmorMeshes = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> FemaleArmorMeshes = new();
        private static readonly ILog Logger = LogManager.GetLogger(typeof(OutfitManager));
        List<string> DividableFactions = new();

        //TODO: Remove this
        readonly ConcurrentDictionary<string, object> all = new();
        public OutfitManager(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            State = state;
            DividableFactions = Patcher.DividableFactions.ToLower().Split("|").ToList();
        }

        public void Process()
        {
            GroupOutfitRecords();
            
            GetOutfitsForMaterial();
            //GetOutfitsForFactions();

            GenerateArmorMeshesData();
            ProcessArmorsForOutfits();

            CreateMaterialFactionBasedOutfits();
            ProcessNpcsForOutfits();

            FileUtils.WriteJson(Path.Combine(State.ExtraSettingsDataPath, "output.json"), all);
        }

        private void GroupOutfitRecords()
        {
            Logger.InfoFormat("Grouping outfit records...");
            State.LoadOrder.PriorityOrder
                .Where(x => !Configuration.User.ModsToSkip.Contains(x.ModKey.FileName))
                .WinningOverrides<INpcGetter>().EmptyIfNull()
                .ForEach(npc =>
                {
                    //mapping outfits
                    if (npc.DefaultOutfit.TryResolve<IOutfitGetter>(Cache, out IOutfitGetter otft))
                    {
                        string otfteid = otft.EditorID;
                        int val = AllowedOutfits.ContainsKey(otfteid)
                            ? AllowedOutfits.GetValueOrDefault(otfteid) : 0;
                        AllowedOutfits[otfteid] = val + 1;
                    }
                });
            Logger.InfoFormat("Outfit records grouped...\n\n");
        }

        private void GetOutfitsForMaterial()
        {
            Logger.InfoFormat("Getting outfits to be patched based on armor materials....");
            // Filtering outfits with more than 3 references
            var dict = AllowedOutfits
                .Where(x => AllowedOutfits.GetValueOrDefault(x.Key) > 2
                    && ArmorUtils.IsValidOutfit(x.Key));
            var allowedOutfits = new Dictionary<string, int>(dict);
            Logger.InfoFormat("Patchable outfit records: {0}", allowedOutfits.Count);

            Regex4outfits.Keys
                .ForEach(ot =>
                {
                    Logger.InfoFormat("Getting '{0}' based outfits", ot);
                    List<string> otpt = new();
                    var material = Materials.GetOrAdd(ot, new TArmorMaterial(ot));
                    string[] regex = Regex4outfits.GetValueOrDefault(ot).Split("::");

                    foreach (IOutfitGetter record in allowedOutfits.Keys
                        .Select(x => Cache.Resolve<IOutfitGetter>(x))
                        .Where(x => ArmorUtils.IsValidOutfit(x))
                        .Where(f => regex.Length == 1 ? Regex.Match(f.EditorID ?? "", regex[0], RegexOptions.IgnoreCase).Success
                            : Regex.Match(f.EditorID ?? "", regex[0], RegexOptions.IgnoreCase).Success
                            && !Regex.Match(f.EditorID ?? "", regex[1], RegexOptions.IgnoreCase).Success))
                    {
                        otpt.Add(string.Format("{0}::{1}", record.FormKey.ToString(), record.EditorID));
                        material.AddOutfit(record);
                    }
                    Logger.DebugFormat(string.Join("\n\t\t", otpt));
                });

            var block = new ActionBlock<IOutfitGetter>(
                outfit =>
                {
                    string eid = outfit.EditorID;
                    string eidType = ArmorUtils.GetOutfitArmorType(eid);
                    Logger.DebugFormat("Processing outfit: {0}[{1}]", eid, outfit.FormKey);
                    var factions = HelperUtils.GetRegexBasedGroup(Patcher.OutfitRegex, eid)
                    .Where(x=>!Materials.ContainsKey(x)).ToList();
                    factions.ForEach(f =>
                    {
                        var k = DividableFactions.Contains(f.ToLower()) ? f + eidType : f;
                        Materials.GetOrAdd(k, new TArmorMaterial(k)).AddOutfit(outfit);
                    });

                }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 10 });

            foreach (IOutfitGetter outfit in State.LoadOrder.PriorityOrder
                .Where(x => !Configuration.User.ModsToSkip.Contains(x.ModKey.FileName)
                    && !Configuration.User.SleepingOutfitMods.Contains(x.ModKey.FileName))
                .WinningOverrides<IOutfitGetter>()
                .Where(x => ArmorUtils.IsValidOutfit(x)
                    && allowedOutfits.ContainsKey(x.EditorID)))
            {
                block.Post(outfit);
            }
            block.Complete();
            block.Completion.Wait();

            AllowedOutfits.Clear();
            allowedOutfits.Clear();
            Materials = new(Materials.Where(x => !x.Value.Outfits.IsEmpty));
            Logger.InfoFormat("Material based outfits are filtered for patching....\n\n");
        }

        private void GenerateArmorMeshesData()
        {
            Logger.InfoFormat("Generating armor meshes data...");
            var block = new ActionBlock<IArmorGetter>(
                armor =>
                {
                    string material = ArmorUtils.GetMaterial(armor);                    
                    if (!ArmorsWithSlot.ContainsKey(material))
                        ArmorsWithSlot[material] = new();

                    if (IsEligibleForMeshMapping(armor, material) 
                        && armor.Armature.FirstOrDefault().TryResolve<IArmorAddonGetter>(Cache,out var addon))
                    {
                        // Adding Armor sets
                        var slots = ArmorUtils.GetBodySlots(addon);
                        if (Patcher.Masters.Contains(armor.FormKey.ModKey.FileName)) {
                            slots.Select(x => x.ToString()).ForEach(slot => {
                                if (!ArmorsWithSlot[material].ContainsKey(slot))
                                    ArmorsWithSlot[material][slot] = armor.FormKey;
                            });
                        }                        

                        if (addon.WorldModel != null && addon.WorldModel.Male != null)
                        {
                            if (File.Exists(Path.Combine(State.DataFolderPath, "Meshes", addon.WorldModel.Male.File)))
                            {
                                // Getting body armor related like data material, editorID, armor type etc
                                if (!MaleArmorMeshes.ContainsKey(material))
                                    MaleArmorMeshes.TryAdd(material, new());
                                slots.ForEach(flag =>
                                {
                                    var slot = flag.ToString();
                                    if (!MaleArmorMeshes.GetValueOrDefault(material).ContainsKey(slot))
                                        MaleArmorMeshes.GetValueOrDefault(material).TryAdd(slot, addon.WorldModel.Male.File);
                                });
                            }
                        }

                        if (addon.WorldModel != null && addon.WorldModel.Female != null)
                        {
                            if (File.Exists(Path.Combine(State.DataFolderPath, "Meshes", addon.WorldModel.Female.File)))
                            {
                                // Getting body armor related like data material, editorID, armor type etc
                                if (!FemaleArmorMeshes.ContainsKey(material))
                                    FemaleArmorMeshes.TryAdd(material, new());
                                slots.ForEach(flag =>
                                {
                                    var slot = flag.ToString();
                                    if (!FemaleArmorMeshes.GetValueOrDefault(material).ContainsKey(slot))
                                        FemaleArmorMeshes.GetValueOrDefault(material).TryAdd(slot, addon.WorldModel.Female.File);
                                });
                            }
                        }
                    }
                }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 10 });

            foreach (IArmorGetter armor in State.LoadOrder.PriorityOrder
                .Where(x => !User.ModsToSkip.Contains(x.ModKey.FileName)
                    && !User.SleepingOutfitMods.Contains(x.ModKey.FileName)
                    && !User.JewelryMods.Contains(x.ModKey.FileName))
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

        private void ProcessArmorsForOutfits()
        {
            Logger.InfoFormat("Grouping Armors....");
            // For each armor mod creating armor sets, skipping mods which have the outfit records.
            // If these outfit records are overriden it will be resolved later in the patch
            var modlists = State.LoadOrder.PriorityOrder
                .Where(x => User.ArmorModsForOutfits.ContainsKey(x.ModKey.FileName)
                    && x.Mod.Armors.Count > 0 && x.Mod.Outfits.Count == 0);


            for(int m= 0; m < modlists.Count(); m++)
            {
                ISkyrimModGetter mod = modlists.ElementAt(m).Mod;
                List<IArmorGetter> bodies = new();
                List<IArmorGetter> others = new();

                var patchName = "ZZZ Armors " + (m / 150);
                ISkyrimMod patch = FileUtils.GetOrAddPatch(patchName, true);                

                mod.Armors
                    .Where(x => ArmorUtils.IsValidArmor(x) && x.Keywords != null)
                    .ForEach(armor =>
                    {
                        if (ArmorUtils.IsBodyArmor(armor)) bodies.Add(armor);
                        else others.Add(armor);
                        AddMissingGenderMeshes(patch, armor);
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
                        if (ArmorsWithSlot.TryGetValue(armorSet.Material, out var slots))
                            if (slots.TryGetValue(TBodySlot.Feet.ToString(), out var feetArmor))
                                armorSet.AddArmor(new TArmor(Cache.Resolve<IArmorGetter>(feetArmor), armorSet.Material));
                    }

                    armorSet.CreateLeveledList();
                    List<string> modsFactions = User.ArmorModsForOutfits[mod.ModKey.FileName];
                    modsFactions.Remove("Generic");

                    // to be distributed using materials
                    var group = armorSet.Material;
                    addArmorSet(group, armorSet);

                    foreach (string fgroup in modsFactions)
                    {
                        var fg = DividableFactions.Contains(fgroup.ToLower())
                            ? fgroup + armorSet.Type : fgroup;
                        addArmorSet(fg, armorSet);
                    }

                    if (i > 0 && (i + 1) % 100 == 0)
                        Logger.InfoFormat("Created {0}/{1} armor-set for: {2}", i + 1, bodyCount, mod.ModKey.FileName);
                }
                Logger.InfoFormat("Created {0}/{0} armor-set for: {1}", bodyCount, mod.ModKey.FileName);
            }
            MaleArmorMeshes.Clear();
            FemaleArmorMeshes.Clear();
            Logger.InfoFormat("Created armor sets for armor mods based on materials....\n\n");
        }

        private void addArmorSet(string group, TArmorSet armorSet)
        {
            if (Regex.IsMatch(group, "Jewelry|Shield", RegexOptions.IgnoreCase)) return;
            if (Materials.ContainsKey(group))
                Materials.GetValueOrDefault(group).Armors.Add(armorSet);
            else
            {
                if (!Factions.ContainsKey(group))
                    Factions.TryAdd(group, new TArmorFaction(group));
                Factions.GetValueOrDefault(group).Armors.Add(armorSet);
            }
        }

        private void CreateMaterialFactionBasedOutfits()
        {
            PatchedMod = FileUtils.GetOrAddPatch("ZZZ Outfits");
            Logger.InfoFormat("Creating material based outfit records...");
            Materials.Where(x => x.Value.Outfits.IsEmpty && !x.Value.Armors.IsEmpty)
                .ForEach(rec =>
                {
                    string eid = Patcher.LeveledListPrefix + "mLL_" + rec.Key;
                    var oLLs = rec.Value.Armors.Select(a => a.CreateLeveledList().AsLink<IItemGetter>());
                    LeveledItem mLL = OutfitUtils.CreateLeveledList(PatchedMod, oLLs, eid, 1, LeveledListFlag);
                    Outfit newOutfit = PatchedMod.Outfits.AddNew(eid);
                    newOutfit.Items = new(mLL.AsLink().AsEnumerable());

                    rec.Value.Outfits.TryAdd(newOutfit.FormKey, eid);
                    Logger.InfoFormat("Created new outfit record for: " + rec.Key);
                });

            Logger.InfoFormat("\nCreating faction based outfit records...");
            Factions.Where(x => x.Value.Outfits.IsEmpty && !x.Value.Armors.IsEmpty)
                .ForEach(rec =>
                {
                    string eid = Patcher.LeveledListPrefix + "mLL_" + rec.Key;
                    var oLLs = rec.Value.Armors.Select(a => a.CreateLeveledList().AsLink<IItemGetter>());
                    LeveledItem mLL = OutfitUtils.CreateLeveledList(PatchedMod, oLLs, eid, 1, LeveledListFlag);
                    Outfit newOutfit = PatchedMod.Outfits.AddNew(eid);
                    newOutfit.Items = new(mLL.AsLink().AsEnumerable());

                    rec.Value.Outfits.TryAdd(newOutfit.FormKey, eid);
                    Logger.InfoFormat("Created new outfit record for: " + rec.Key);
                });

            Logger.InfoFormat("Added new outfits based on Materials & Factions...\n\n");
        }

        private void ProcessNpcsForOutfits()
        {
            ISkyrimMod patch = FileUtils.GetOrAddPatch("ZZZ NPC Outfits");
            int processed = 0;
            RandomSource rand = new();
            ConcurrentDictionary<string, TNPC> npcs = new();
            HashSet<string> missingId = new();
            var mergedOutfits = MergeAndPatchOutfits();

            // NPC Patched Keyword
            Keyword kywrd = patch.Keywords.AddNew(Patcher.OutfitPatchedKeywordEID);
            Patcher.OutfitPatchedKeyword = kywrd.FormKey;

            List<FormKey> outfit2Skip = new();
            mergedOutfits.Values.Select(x => x.Outfits).ForEach(x => outfit2Skip.AddRange(x.Keys));
            Logger.InfoFormat("Outfit records processed...\n\n");

            // Adding Armor sets to Mannequins
            if (User.AddArmorsToMannequin)
            {
                List<TArmorSet> armorSets = new();
                mergedOutfits.Values.ForEach(x => armorSets.AddRange(x.Armors));
                AddArmorsToMannequin(armorSets);
            }

            Logger.InfoFormat("Processing NPC records to assign outfit...");
            foreach (var context in State.LoadOrder.PriorityOrder.Npc()
                .WinningContextOverrides()
                .Where(c => !c.Record.DefaultOutfit.IsNull
                    && !outfit2Skip.Contains(c.Record.DefaultOutfit.FormKey)
                    && NPCUtils.IsValidNPC(c.Record) && !NPCUtils.IsEssential(c.Record)
                    && Cache.Resolve<IRaceGetter>(c.Record.Race.FormKey).HasKeyword(Skyrim.Keyword.ActorTypeNPC))
                .ToList())
            {
                var overriddenMods = Cache.ResolveAllContexts<INpc, INpcGetter>(context.Record.FormKey)
                    .Select(c => c.ModKey.FileName.String).ToList();
                if (!overriddenMods.Intersect(User.ModsToSkip).Any())
                {
                    //block.Post(context.Record);
                    INpcGetter npc = context.Record;
                    Logger.DebugFormat("Processing NPC: " + npc.EditorID);
                    var tnpc = new TNPC(State, npc);
                    npcs.TryAdd(npc.EditorID, tnpc);
                    var id = tnpc.Identifier;
                    if (mergedOutfits.ContainsKey(id))
                    {
                        Npc newNPC = patch.Npcs.GetOrAddAsOverride(npc);
                        
                        //Setting outfit
                        var outfitFormKey = mergedOutfits[id].Outfits.Keys.Randomize(rand).First();
                        newNPC.DefaultOutfit.SetTo(outfitFormKey);

                        // Setting keyword to track
                        if (!newNPC.Keywords.Any())
                            newNPC.Keywords = new();
                        newNPC.Keywords.Add(kywrd);
                        Logger.DebugFormat("Adding outfit to NPC {0}[{1}]=>{2}", npc.EditorID, npc.FormKey, outfitFormKey);
                    }
                    else
                    {
                        Logger.DebugFormat("Skipping NPC: {0}[{1}]=> {2}", npc.EditorID, npc.FormKey, id);
                        missingId.Add(id);
                    }
                    if (++processed % 500 == 0)
                        Logger.InfoFormat("Total NPC processed: " + processed);
                }
            }
            all["Missing"] = missingId;
            all["NPC"] = npcs;
            Logger.InfoFormat("Total NPC records processed: {0}...\n\n", processed);
        }

        private void AddArmorsToMannequin(List<TArmorSet> armorSets)
        {
            Logger.InfoFormat("Distributing Armor sets to Mannequins...");

            ISkyrimMod patch = FileUtils.GetOrAddPatch("ZZZ Mannequins Outfits");
            var form = patch.FormLists.AddNew("MannequinsArmorForm");           
            armorSets = armorSets.Distinct().ToList();
            var lls = armorSets.Select(set => set.CreateLeveledList().AsLink<IItemGetter>());
            form.Items.AddRange(lls);
            Logger.InfoFormat("Distributed Armor sets to Mannequins...\n\n");
        }

        private void AddMissingGenderMeshes(ISkyrimMod patch, IArmorGetter armor)
        {
            string material = ArmorUtils.GetMaterial(armor);
            if (!IsEligibleForMeshMapping(armor, material))
            {
                Logger.DebugFormat("Skipping adding of missing mesh for armor: " + armor.EditorID);
                return;
            }

            IArmorAddonGetter addon = armor.Armature.FirstOrDefault().Resolve(Configuration.Cache);
            ArmorUtils.GetBodySlots(addon).ForEach(flag =>
            {
                var slot = flag.ToString();
                // Mapping Male Models with Female only Armor
                if (addon.WorldModel != null && addon.WorldModel.Male == null
                    && MaleArmorMeshes.ContainsKey(material)
                    && MaleArmorMeshes.GetValueOrDefault(material).TryGetValue(slot, out var maleMesh))
                {
                    IArmorAddon localAddon = patch.ArmorAddons.GetOrAddAsOverride(addon);
                    localAddon.WorldModel.Male = new();
                    localAddon.WorldModel.Male.File = maleMesh;
                    Logger.DebugFormat("Missing male mesh added for armor: " + armor.EditorID);
                    return;
                }

                // Mapping Female Models with Female only Armor
                if (addon.WorldModel != null && addon.WorldModel.Female == null
                    && FemaleArmorMeshes.ContainsKey(material)
                    && MaleArmorMeshes.GetValueOrDefault(material).TryGetValue(slot, out var femaleMesh))
                {
                    IArmorAddon localAddon = patch.ArmorAddons.GetOrAddAsOverride(addon);
                    localAddon.WorldModel.Male = new();
                    localAddon.WorldModel.Male.File = femaleMesh;
                    Logger.DebugFormat("Missing female mesh added for armor: " + armor.EditorID);
                }
            });
        }

        private bool IsEligibleForMeshMapping(IArmorGetter armor, string material)
        {
            var groups = HelperUtils.GetRegexBasedGroup(Regex4outfits, material, armor.EditorID);
            var key = groups.Any() ? groups.First() : "Unknown";
            return Materials.ContainsKey(key)
                    && armor.Armature != null
                    && armor.Armature.Count > 0
                    && !armor.HasKeyword(Skyrim.Keyword.ArmorHelmet)
                    && !armor.HasKeyword(Skyrim.Keyword.ArmorJewelry)
                    && !armor.HasKeyword(Skyrim.Keyword.ArmorShield)
                    && !armor.HasKeyword(Skyrim.Keyword.ClothingHead)
                    && !armor.HasKeyword(Skyrim.Keyword.ClothingCirclet)
                    && !armor.HasKeyword(Skyrim.Keyword.ClothingNecklace);
        }

        private Dictionary<string, TArmorGroupable> MergeAndPatchOutfits()
        {
            Dictionary<string, TArmorGroupable> MergedOutfits = new(Materials.ToList());
            Factions.Keys.ForEach(f =>
            {
                var node = Factions[f];
                if (Materials.ContainsKey(f))
                {
                    MergedOutfits[f].AddArmorSets(node.Armors);
                    MergedOutfits[f].AddOutfits(Cache, node.Outfits);
                }
                else
                {
                    MergedOutfits.TryAdd(f, node);
                }
            });

            MergedOutfits["Unknown"].Armors.ForEach(set =>
            {
                if (set.Type == TArmorType.Light)
                {
                    MergedOutfits["BanditLight"].Armors.Add(set);
                    MergedOutfits["MercenaryLight"].Armors.Add(set);
                }else if (set.Type == TArmorType.Robes)
                {
                    MergedOutfits["BanditWizard"].Armors.Add(set);
                    MergedOutfits["MercenaryWizard"].Armors.Add(set);
                }else if (set.Type == TArmorType.Heavy)
                {
                    MergedOutfits["BanditHeavy"].Armors.Add(set);
                    MergedOutfits["MercenaryHeavy"].Armors.Add(set);
                }else if (set.Type == TArmorType.Clothes) 
                    MergedOutfits["CitizenRich"].Armors.Add(set);
            });

            // Merge Outfits
            // merging CitizenRich to merchant''
            MergedOutfits["Merchant"].AddArmorSets(MergedOutfits["CitizenRich"].Armors);

            // Resolving or creating new outfit records
            var outfitContext = State.LoadOrder.PriorityOrder.Outfit()
                .WinningContextOverrides().Where(x => ArmorUtils.IsValidOutfit(x.Record));

            MergedOutfits.Values.ForEach(group =>
            {
                if (group.Name != "Unknown" && !group.Armors.IsEmpty)
                {
                    Logger.InfoFormat("Processing outfit records for: " + group.Name);
                    List<FormLink<IItemGetter>> mLLs = new();

                    var modLL = group.GetLeveledListsUsingArmorSets();
                    group.Outfits.Keys.ForEach(key =>
                    {
                        var eid = group.Outfits[key];
                        List<FormLink<IItemGetter>> oLLs = new();
                        oLLs.AddRange(modLL);

                        // Getting All outfit but taking the winning override of the outfits except armor mods
                        // Merging Armor mods outfit's items as well to the overriden record
                        var winningOtfts = outfitContext.Where(c => c.Record.FormKey == key).EmptyIfNull();
                        if (winningOtfts.Any())
                        {
                            var winningOtft = winningOtfts.First().Record;
                            var otft = Cache.ResolveAllContexts<IOutfit, IOutfitGetter>(winningOtft.FormKey)
                            .Where(c => !User.ArmorModsForOutfits.ContainsKey(c.ModKey.FileName)).First().Record;

                            var items = otft.Items.Select(x => Cache.Resolve<IItemGetter>(x.FormKey));
                            var ll = OutfitUtils.CreateLeveledList(PatchedMod, items, eid + 0, 1, LeveledItem.Flag.UseAll);
                            oLLs.Add(ll.FormKey);
                        }

                        // Adding outfits from Armor mods
                        IEnumerable<IOutfitGetter> modedOutfits = Cache.ResolveAll<IOutfitGetter>(key);
                        for (int i = 0; i < modedOutfits.Count(); i++)
                        {
                            IOutfitGetter outfit = modedOutfits.ElementAt(i);
                            var items1 = outfit.Items.Select(x => Cache.Resolve<IItemGetter>(x.FormKey));
                            var ll_eid1 = outfit.EditorID + (i + 1);
                            var allArmor = items1.All(x => x is IArmorGetter);
                            var flg = allArmor ? LeveledItem.Flag.UseAll : LeveledListFlag;
                            var ll1 = OutfitUtils.CreateLeveledList(PatchedMod, items1, ll_eid1, 1, flg);
                            oLLs.Add(ll1.FormKey);
                        }

                        // Creating final patched outfit
                        LeveledItem sLL = OutfitUtils.CreateLeveledList(PatchedMod, oLLs.Distinct().Reverse(), "sLL_" + eid, 1, LeveledListFlag);
                        Outfit newOutfit = PatchedMod.Outfits.GetOrAddAsOverride(Cache.Resolve<IOutfitGetter>(key));
                        newOutfit.Items.Clear();
                        newOutfit.Items.Add(sLL);
                        mLLs.Add(sLL);
                        Logger.DebugFormat("Patched outfit record: {0}", eid);
                    });

                    LeveledItem mLL = OutfitUtils.CreateLeveledList(PatchedMod, mLLs.Distinct(), "mLL_" + group, 1, LeveledListFlag);
                    Outfit gOTFT = PatchedMod.Outfits.AddNew(group+"_Outfit");
                    gOTFT.Items = new();
                    gOTFT.Items.Add(mLL);
                    group.LLKey = mLL.FormKey;
                    group.OutfitKey = gOTFT.FormKey;
                }
            });

            return MergedOutfits;
        }

        private void GetOutfitsForFactions()
        {
            Logger.InfoFormat("Filtering and Processing faction based outfits....");

            // Filtering outfits with more than 2 references
            var dict = AllowedOutfits
                .Where(x => AllowedOutfits.GetValueOrDefault(x.Key) > 2
                    && ArmorUtils.IsValidOutfit(x.Key));
            var allowedOutfits = new Dictionary<string, int>(dict);

            var block = new ActionBlock<IOutfitGetter>(
                outfit =>
                {
                    string eid = outfit.EditorID;
                    string eidType = ArmorUtils.GetOutfitArmorType(eid);
                    Logger.DebugFormat("Processing outfit: {0}[{1}]", eid, outfit.FormKey);
                    var factions = HelperUtils.GetRegexBasedGroup(Patcher.OutfitRegex, eid);
                    factions.ForEach(f =>
                    {
                        var k = DividableFactions.Contains(f.ToLower()) ? f + eidType : f;
                        Factions.GetOrAdd(k, new TArmorFaction(k)).AddOutfit(outfit);
                    });

                }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 10 });

            foreach (IOutfitGetter outfit in State.LoadOrder.PriorityOrder
                .Where(x => !Configuration.User.ModsToSkip.Contains(x.ModKey.FileName)
                    && !Configuration.User.SleepingOutfitMods.Contains(x.ModKey.FileName))
                .WinningOverrides<IOutfitGetter>()
                .Where(x => ArmorUtils.IsValidOutfit(x)
                    && allowedOutfits.ContainsKey(x.EditorID)))
            {
                block.Post(outfit);
            }
            block.Complete();
            block.Completion.Wait();

            Factions = new(Factions.Where(x => !Materials.ContainsKey(x.Key)));
            AllowedOutfits.Clear();
            Logger.InfoFormat("Faction based outfits are filtered for patching....\n\n");
        }


    }
}
