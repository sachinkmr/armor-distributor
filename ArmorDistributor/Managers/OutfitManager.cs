using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Noggog;
using System.IO;
using System.Text.RegularExpressions;
using System;
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

        private ISkyrimMod? Patch;
        readonly IPatcherState<ISkyrimMod, ISkyrimModGetter> State;

        private List<string> SPID = new();
        private Dictionary<string, HashSet<FormKey>> OutfitsWithNPC = new();
        private Dictionary<string, TArmorCategory> GrouppedArmorSets = new();
        private Dictionary<string, HashSet<string>> OutfitArmorTypes = new();
        private Dictionary<string, HashSet<string>> OutfitArmorTypesRev = new();
        private readonly Dictionary<string, Dictionary<string, string>> MaleArmorMeshes = new();
        private readonly Dictionary<string, Dictionary<string, string>> FemaleArmorMeshes = new();
        private Dictionary<string, Dictionary<string, Dictionary<FormKey, float>>> ArmorsWithSlot = new();

        public OutfitManager(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            State = state;
        }

        public ISkyrimMod Process(ISkyrimMod patch)
        {
            Console.WriteLine("\n************ Starting Patcher ************");
            Patch = FileUtils.GetIncrementedMod(patch);

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

            // Clearing data
            MaleArmorMeshes.Clear();
            FemaleArmorMeshes.Clear();
            ArmorsWithSlot.Clear();

            // Creating SPID Lines
            CategorizeNPCs();
            AddGroppedArmorSetToSPIDFile();

            // Writing data to SPID File
            SPID = SPID.Where(x => x.Trim().Any()).ToList();
            File.WriteAllLines(Path.Combine(State.DataFolderPath, Program.Settings.IniName), SPID);



            return Patch;
        }

        /**
         * Returns a dictonary with outfit and number of times those are used.
         */
        private void GetPatchableOutfits()
        {
            Console.WriteLine("Fetching outfit records...");
            State.LoadOrder.PriorityOrder
                .WinningOverrides<INpcGetter>()
                .Where(x => !Program.Settings.UserSettings.ModsToSkip.Contains(x.FormKey.ModKey))
                .Where(x => NPCUtils.IsValidActorType(x))
                .ForEach(npc =>
                {
                    var npcs = Program.Settings.Cache.ResolveAllContexts<INpc, INpcGetter>(npc.FormKey);
                    npcs.ForEach(n =>
                    {
                        var record = n.Record;
                        if (record.DefaultOutfit.TryResolve<IOutfitGetter>(Program.Settings.Cache, out IOutfitGetter otft))
                        {
                            string otfteid = otft.EditorID;
                            OutfitsWithNPC.GetOrAdd(otfteid).Add(npc.FormKey);
                        }
                    });
                });


            // Missing missing Valid outfits having no NPC record assiciated with
            var otfts = State.LoadOrder.PriorityOrder
                .Where(x => !Program.Settings.UserSettings.ModsToSkip.Contains(x.ModKey.FileName))
                .WinningOverrides<IOutfitGetter>()
                .Where(x => !Program.Settings.UserSettings.ModsToSkip.Contains(x.FormKey.ModKey));
            foreach (IOutfitGetter outfit in otfts)
            {
                if (OutfitUtils.IsValidOutfit(outfit))
                {
                    if (!OutfitsWithNPC.ContainsKey(outfit.EditorID))
                    {
                        OutfitsWithNPC.GetOrAdd(outfit.EditorID);
                        AddOutfitToGroup(outfit);
                    }
                    else if (OutfitsWithNPC[outfit.EditorID].Count() > 0)
                    {
                        AddOutfitToGroup(outfit);
                    }
                    else
                    {
                        Logger.DebugFormat("Skipping Valid OTFT: " + outfit.EditorID);
                    }
                }
                else Logger.DebugFormat("Skipping Invalid OTFT: " + outfit.EditorID);

            }
        }

        /**
         * Returns armor list with are associated with Outfits for Skyrim masters
         */
        private void CategorizeOutfits()
        {
            Console.WriteLine("Processing Outfits to Patch...");
            foreach (var outfit in State.LoadOrder.PriorityOrder
                .Where(x => !Program.Settings.UserSettings.ModsToSkip.Contains(x.ModKey.FileName))
                .WinningOverrides<IOutfitGetter>()
                .Where(x => !Program.Settings.UserSettings.ModsToSkip.Contains(x.FormKey.ModKey))
                .Where(x => OutfitUtils.IsValidOutfit(x)))
            {
                // Creating Outfit Category based on ArmorType
                var types = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.ArmorTypeRegex, outfit.EditorID);
                if (!types.Any())
                {
                    types = OutfitUtils.GetOutfitArmorType(outfit);
                }
                if (!types.Any())
                {
                    types = new List<string>() { TArmorType.Unknown };
                }
                types.ForEach(x =>
                {
                    OutfitArmorTypes.GetOrAdd(x).Add(outfit.EditorID);
                    OutfitArmorTypesRev.GetOrAdd(outfit.EditorID).Add(x);
                });

                // This will outfit which assigned to Anyy NPC to the group
                //if (!OutfitsWithNPC.ContainsKey(outfit.EditorID)) AddOutfitToGroup(outfit);

            }
            Console.WriteLine("Processed Outfits to Patch...");
        }

        /*
         * Filter outfits based on the its uses count
         */
        private void GroupOutfits()
        {
            Console.WriteLine("Getting outfits to be patched....");
            foreach (IOutfitGetter outfit in State.LoadOrder.PriorityOrder
                .WinningOverrides<IOutfitGetter>()
                .Where(x => !Program.Settings.UserSettings.ModsToSkip.Contains(x.FormKey.ModKey))
                .Where(x => OutfitUtils.IsValidOutfit(x)))
            {
                AddOutfitToGroup(outfit);
            }
            Console.WriteLine("Outfits are categorized for patching....\n\n");
        }

        /* 
         * Creates aremor sets baed on material and provided keywords
         */
        private void CreateArmorsSets()
        {
            Console.WriteLine("Creating matching armor sets for armor mods...");
            // For each armor mod creating armor sets, skipping mods which have the outfit records.
            // If these outfit records are overriden it will be resolved later in the patch
            var modlists = State.LoadOrder.PriorityOrder
                .Where(x => Program.Settings.UserSettings.ArmorMods.ContainsKey(x.ModKey.FileName)
                    && x.Mod.Armors.Count > 0);

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
                        Patch = FileUtils.GetIncrementedMod(Patch);
                        if (IsEligibleForMeshMapping(armor)) AddMissingGenderMeshes(armor);
                        if (ArmorUtils.IsBodyArmor(armor)) bodies.Add(armor);
                        else if (ArmorUtils.IsJewelry(armor)) jewelries.Add(new(armor));
                        else others.Add(new(armor));
                    });

                int bodyCount = bodies.Count;
                totalSets += bodyCount;

                var commanName = 0;
                var commanEID = 0;
                if (bodyCount > 5 && Program.Settings.UserSettings.ArmorMods[mod.ModKey.FileName].Contains("Generic"))
                {
                    commanName = HelperUtils.GetCommonItems(others.Select(x => HelperUtils.SplitString(x.Name)).ToList())
                               .Where(x => !x.IsNullOrEmpty()).Count();
                    commanEID = HelperUtils.GetCommonItems(others.Select(x => HelperUtils.SplitString(x.EditorID)).ToList())
                               .Where(x => !x.IsNullOrEmpty()).Count();
                }

                var armorGroups = others.GroupBy(x => x.Type)
                    .ToDictionary(x => x.Key, x => x.Select(a => a)
                            .GroupBy(a => a.Material)
                            .ToDictionary(a => a.Key, a => a.Select(b => b)));
                for (int i = 0; i < bodyCount; i++)
                {
                    // Creating armor sets and LLs
                    var body = bodies.ElementAt(i);
                    TArmorSet armorSet = new(body);
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
                        armorSet.AddArmor(new TArmor(Program.Settings.Cache.Resolve<IArmorGetter>(feet), armorSet.Material));
                    }

                    //Patch = FileUtils.GetIncrementedMod(Patch);
                    //armorSet.CreateLeveledList();

                    // Add using materials
                    var group = armorSet.Material;
                    AddArmorSetToGroup(group, armorSet);

                    // Add using provided category
                    List<string> modsCategories = Program.Settings.UserSettings.ArmorMods[mod.ModKey.FileName];
                    modsCategories.Remove("Generic");
                    foreach (string fgroup in modsCategories.Distinct())
                    {
                        AddArmorSetToGroup(fgroup, armorSet);
                    }

                    if (i > 0 && (i + 1) % 100 == 0)
                        Console.WriteLine("Created {0}/{1} armor-set for: {2}", i + 1, bodyCount, mod.ModKey.FileName);
                }
                Console.WriteLine("Created {0}/{0} armor-set for: {1}", bodyCount, mod.ModKey.FileName);
            }
            Console.WriteLine("Created {0} matching armor sets from armor mods...\n", totalSets);
            ArmorsWithSlot.Clear();
        }

        private void CreateNewOutfits()
        {
            Console.WriteLine("Creating New Outfits...");
            if (GrouppedArmorSets.Remove("Unknown", out var temp))
            {
                var types = temp.Armors.GroupBy(a => a.Type).ToDictionary(x => x.Key, x => x.Select(b => b));
                foreach (var t in types)
                {
                    if (t.Key == TArmorType.Cloth)
                    {
                        GrouppedArmorSets.GetOrAdd("Traveller", () => new TArmorCategory("Traveller")).AddArmorSets(temp.Armors);
                        GrouppedArmorSets.GetOrAdd("Merchant", () => new TArmorCategory("Merchant")).AddArmorSets(temp.Armors);
                    }
                    else
                    {
                        GrouppedArmorSets.GetOrAdd("Bandit", () => new TArmorCategory("Bandit")).AddArmorSets(t.Value);
                        GrouppedArmorSets.GetOrAdd("Warrior", () => new TArmorCategory("Warrior")).AddArmorSets(t.Value);
                        GrouppedArmorSets.GetOrAdd("Mercenary", () => new TArmorCategory("Mercenary")).AddArmorSets(t.Value);
                    }
                }
            }

            GrouppedArmorSets.ForEach(rec =>
            {
                Patch = FileUtils.GetIncrementedMod(Patch);
                rec.Value.CreateOutfits(Patch);
                Logger.DebugFormat("Created new outfit record for: " + rec.Key);
            });
        }

        private void ResolveOutfitOverrides()
        {
            Console.WriteLine("\nResolving outfit conflicts...");
            var outfitContext = State.LoadOrder.PriorityOrder.Outfit()
               .WinningContextOverrides();

            foreach (var outfit in State.LoadOrder.PriorityOrder
                .Where(x => Program.Settings.UserSettings.ArmorMods.ContainsKey(x.ModKey.FileName))
                .WinningOverrides<IOutfitGetter>()
                .Where(x => !Program.Settings.UserSettings.ModsToSkip.Contains(x.FormKey.ModKey)))
            {

                var winningOtfts = outfitContext.Where(c => c.Record.FormKey == outfit.FormKey).EmptyIfNull();
                if (winningOtfts.Any())
                {
                    List<IItemGetter> oLLs = new();
                    var winningOtft = winningOtfts.First().Record;

                    // Merging outfit's lvls from the armor mods together
                    var overridenOtfts = Program.Settings.Cache.ResolveAllContexts<IOutfit, IOutfitGetter>(winningOtft.FormKey)
                            .Where(c => Program.Settings.UserSettings.ArmorMods.ContainsKey(c.ModKey.FileName));

                    overridenOtfts.ForEach(r =>
                    {
                        var items = r.Record.Items.Select(x => Program.Settings.Cache.Resolve<IItemGetter>(x.FormKey));
                        if (items.Count() == 1)
                        {
                            oLLs.Add(items.First());
                        }
                        else
                        {
                            Patch = FileUtils.GetIncrementedMod(Patch);
                            var ll = OutfitUtils.CreateLeveledList(Patch, items, "ll_" + r.Record.EditorID + 0, 1, LeveledItem.Flag.UseAll);
                            oLLs.Add(ll);
                        }
                    });

                    // Reverting Overriden outfit by armor mods added in the patcher
                    var lastNonModOutfit = Program.Settings.Cache.ResolveAllContexts<IOutfit, IOutfitGetter>(winningOtft.FormKey)
                        .Where(c => !Program.Settings.UserSettings.ArmorMods.ContainsKey(c.ModKey.FileName));

                    if (lastNonModOutfit.Count() > 1)
                    {
                        // Getting outfit records form armor mods added in the patcher and patching those
                        Outfit nOutfit = Patch.Outfits.GetOrAddAsOverride(lastNonModOutfit.First().Record);
                        var items = nOutfit.Items.Select(x => Program.Settings.Cache.Resolve<IItemGetter>(x.FormKey));
                        if (items.Count() == 1)
                        {
                            oLLs.Add(items.First());
                        }
                        else
                        {
                            Patch = FileUtils.GetIncrementedMod(Patch);
                            var ll = OutfitUtils.CreateLeveledList(Patch, items, "ll_" + nOutfit.EditorID + 0, 1, LeveledItem.Flag.UseAll);
                            oLLs.Add(ll);
                        }

                        // Creating patched outfit
                        Patch = FileUtils.GetIncrementedMod(Patch);
                        LeveledItem sLL = OutfitUtils.CreateLeveledList(Patch, oLLs.Distinct(), "sll_" + outfit.EditorID, 1, Program.Settings.LeveledListFlag);
                        nOutfit.Items = new();
                        nOutfit.Items.Add(sLL);
                    }
                }
            }
        }

        private void MergeOutfits()
        {
            // Merging Default outfits with their respective categories
            Console.WriteLine("Merging default outfits with the new outfits...");
            var cache = State.LoadOrder.ToMutableLinkCache();
            foreach (var group in GrouppedArmorSets)
            {
                var category = group.Value.GenderOutfit.GetOrAdd("C");
                Dictionary<string, List<IItemGetter>> dict = new();
                group.Value.Outfits.Keys.ForEach(o =>
                {
                    List<IItemGetter> list = new();
                    var otft = cache.Resolve<IOutfitGetter>(o);
                    var itms = otft.Items.Where(i => State.LinkCache.TryResolve<IItemGetter>(i.FormKey, out var a))
                                .Select(i => State.LinkCache.Resolve<IItemGetter>(i.FormKey));
                    if (itms.Count() == 1) list.AddRange(itms);
                    else
                    {
                        Patch = FileUtils.GetIncrementedMod(Patch);
                        var ll = OutfitUtils.CreateLeveledList(Patch, itms, "LL_" + otft.EditorID, 1, LeveledItem.Flag.UseAll);
                        list.Add(ll);
                    }
                    if (OutfitArmorTypesRev.ContainsKey(otft.EditorID))
                        OutfitArmorTypesRev[otft.EditorID].ForEach(t => dict.GetOrAdd(t).AddRange(list));
                    else
                    {
                        Logger.DebugFormat("outfit category not found: {0}:{1}", otft.EditorID, otft.FormKey.ToString());
                    }
                });

                dict.ForEach(t =>
                {
                    Patch = FileUtils.GetIncrementedMod(Patch);
                    if (category.ContainsKey(t.Key))
                    {
                        FormKey llFormKey = cache.Resolve<IOutfitGetter>(category[t.Key]).Items.First().FormKey;
                        var ll = Patch.LeveledItems.GetOrAddAsOverride(cache.Resolve<LeveledItem>(llFormKey));
                        OutfitUtils.AddItemsToLeveledList(Patch, ll, t.Value, 1);
                    }
                    else
                    {
                        var id = group.Key + "_C_" + t.Key;
                        Outfit newOutfit = OutfitUtils.CreateOutfit(Patch, id, t.Value);
                        category.Add(t.Key, newOutfit.FormKey);
                    }
                });
            }

            // Merging outfits which are not assigned to any NPC
            Console.WriteLine("Merging non assigned outfits with the new outfits...");
            var outfits = State.LoadOrder.PriorityOrder
                .Where(l => !l.ModKey.FileName.String.StartsWith(Settings.PatcherSettings.PatcherPrefix) && !Program.Settings.UserSettings.ModsToSkip.Contains(l.ModKey.FileName))
                .WinningOverrides<IOutfitGetter>()
                .Where(x => !Program.Settings.UserSettings.ModsToSkip.Contains(x.FormKey.ModKey))
                .Where(o => OutfitUtils.IsValidOutfit(o) && !OutfitsWithNPC.ContainsKey(o.EditorID))
                .OrderBy(o => o.EditorID);

            foreach (var otft in outfits)
            {
                // Add missing gender meshes
                OutfitUtils.GetArmorList(otft).ForEach(i => AddMissingGenderMeshes(i));

                List<string> categories = new();
                var gender = "C";
                List<string> types = new();
                if (otft.EditorID.Contains(Settings.PatcherSettings.OutfitSuffix))
                {
                    var tokens = Regex.Replace(otft.EditorID, Settings.PatcherSettings.OutfitSuffix + "|" + Settings.PatcherSettings.OutfitPrefix, "").Split('_');
                    categories.Add(tokens[0]);
                    gender = tokens[1];
                    types.Add(tokens[2]);
                }
                else
                {
                    categories = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, otft.EditorID).ToList();
                    types = OutfitUtils.GetOutfitArmorType(otft.EditorID);
                }
                foreach (var category in categories)
                    foreach (var type in types.Where(t => t != TArmorType.Unknown))
                    {
                        var genderotft = GrouppedArmorSets.GetOrAdd(category, () => new TArmorCategory(category)).GenderOutfit.GetOrAdd(gender);
                        if (genderotft.ContainsKey(type))
                        {
                            // Getting already added LL
                            LeveledItem ll = null;
                            IOutfitGetter o = cache.Resolve<IOutfitGetter>(genderotft[type]);
                            var itms = o.Items.Select(i => cache.Resolve<IItemGetter>(i.FormKey));
                            if (itms.Count() == 1 && itms.All(i => i is ILeveledItemGetter))
                                ll = Patch.LeveledItems.GetOrAddAsOverride((ILeveledItemGetter)itms.First());
                            else
                                ll = OutfitUtils.CreateLeveledList(Patch, itms, "LL_" + category + "_C_" + type + "_1", 1, Program.Settings.LeveledListFlag);

                            // Adding items to the above leveled list
                            itms = otft.Items.Select(i => cache.Resolve<IItemGetter>(i.FormKey));
                            OutfitUtils.AddItemsToLeveledList(Patch, ll, itms, 1);
                        }
                        else genderotft.Add(type, otft.FormKey);
                    }
            }
        }

        private void CategorizeNPCs()
        {
            Dictionary<string, Dictionary<string, HashSet<FormKey>>> map = new();
            // Class Based Distribution
            if (Program.Settings.UserSettings.ClassBasedDistribution)
            {
                Console.WriteLine("\nParsing NPC Classes...");
                State.LoadOrder.PriorityOrder.WinningOverrides<IClassGetter>()
                    .Where(x => !Program.Settings.UserSettings.ModsToSkip.Contains(x.FormKey.ModKey))
                    .ForEach(c =>
                    {
                        var id = Regex.Replace(c.EditorID, "class|combat", "", RegexOptions.IgnoreCase);
                        var groups = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, id).ToList();
                        groups.ForEach(group =>
                        {
                            var types = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.ArmorTypeRegex, c.EditorID);
                            types.ForEach(t => map.GetOrAdd(group).GetOrAdd(t).Add(c.FormKey));
                        });
                    });
            }

            // Faction Based Distribution
            if (Program.Settings.UserSettings.FactionBasedDistribution)
            {
                Console.WriteLine("Parsing NPC Factions...");
                State.LoadOrder.PriorityOrder.WinningOverrides<IFactionGetter>()
                    .Where(x => !Program.Settings.UserSettings.ModsToSkip.Contains(x.FormKey.ModKey))
                .Where(r => NPCUtils.IsValidFaction(r))
                .ForEach(c =>
                {
                    var groups = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, c.EditorID).ToList();
                    groups.ForEach(group =>
                    {
                        var types = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.ArmorTypeRegex, c.EditorID);
                        types.ForEach(t => map.GetOrAdd(group).GetOrAdd(t).Add(c.FormKey));
                    });
                });
            }

            // Putting Race and Classes data in armor sets
            map.ForEach(p => GrouppedArmorSets.GetOrAdd(p.Key, () => new TArmorCategory(p.Key)).Identifiers = p.Value);
        }

        private void AddGroppedArmorSetToSPIDFile()
        {
            Console.WriteLine("Creating SPID ini file...");

            // Distributing Outfits
            var percentage = Program.Settings.UserSettings.DefaultOutfitPercentage;
            var categories = Program.Settings.UserSettings.CategoriesToSkip.Select(x => x.ToString());

            var NotGuard = "-0x" + Skyrim.Faction.GuardDialogueFaction.FormKey.ID.ToString("X");
            var sets = GrouppedArmorSets.Where(x => x.Value.Outfits.Any() || x.Value.Identifiers.Any())
                .OrderBy(x => x.Key);

            foreach (var pair in sets)
            {
                //var identifiers = pair.Value.Identifiers.Select(o => "0x" + o.ID.ToString("X") + "~" + o.ModKey.FileName);
                GrouppedArmorSets[pair.Key].GenderOutfit.Where(g => g.Value != null && g.Value.Any())
                    .ForEach(x =>
                    {
                        var gender = x.Key == "M" ? "M" : x.Key == "C" ? "NONE" : "F";
                        foreach (var armorType in x.Value.Keys)
                        {
                            var outfits = GrouppedArmorSets[pair.Key].Outfits
                                .Where(o => OutfitArmorTypes[armorType].Contains(o.Value) && OutfitsWithNPC.GetOrAdd(o.Value).Any())
                                .Select(o => "0x" + o.Key.ID.ToString("X") + "~" + o.Key.ModKey.FileName).ToList();

                            // Adding NPC Identifiers
                            var identifiers = GrouppedArmorSets[pair.Key].Identifiers.GetOrAdd(armorType)
                                .Select(o => "0x" + o.ID.ToString("X") + "~" + o.ModKey.FileName);
                            outfits.AddRange(identifiers);

                            if (!outfits.Any()) continue;
                            if (!pair.Key.EndsWith("Guards")) outfits.Add(NotGuard);

                            var outfit = x.Value[armorType];
                            var filters = string.Join(",", outfits);

                            SPID.Add(string.Format("\n;Outfits for {0}{1}/{2}", pair.Key, armorType, gender.Replace("NONE", "M+F").Replace("/U", "")));
                            string line = String.Format("Outfit = 0x{0}~{1}|{2}|{3}|NONE|{4}|NONE|{5}", outfit.ID.ToString("X"),
                                outfit.ModKey.FileName, "NONE", filters, gender, 100 - percentage);

                            // Skipping mentioned category
                            if (categories.Contains(pair.Key)) line = ";" + line;
                            if (Program.Settings.UserSettings.SkipGuardDistribution && pair.Key.EndsWith("Guards")) line = ";" + line;
                            if (!Program.Settings.UserSettings.RaceBasedDistribution && pair.Key.EndsWith("Race")) line = ";" + line;
                            if (Program.Settings.UserSettings.FilterUniqueNPC) SPID.Add(line.Replace("/U|NONE", "|NONE"));
                            SPID.Add(line);
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
            if (groups.Any()) Logger.DebugFormat("Outfit Processed: {0}[{1}]", eid, outfit.FormKey);
            else Logger.DebugFormat("Outfit Missed: {0}[{1}]", eid, outfit.FormKey);
        }

        private void AddArmorSetToGroup(string group, TArmorSet armorSet)
        {
            GrouppedArmorSets.GetOrAdd(group, () => new TArmorCategory(group)).Armors.Add(armorSet);
        }

        private void FilterGaurdAndMaterialBasedOutfits()
        {
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
                    common.ForEach(c => x.Outfits.Remove(c.Key));
                })); ;
        }

        private void CategorizeArmors()
        {
            Console.WriteLine("Categorizing armors based on body slots...");
            foreach (IArmorGetter armor in State.LoadOrder.PriorityOrder
                .WinningOverrides<IArmorGetter>()
                .Where(x => ArmorUtils.IsValidArmor(x)))
            {
                string armorType = ArmorUtils.GetArmorType(armor);
                if (armor.Armature.FirstOrDefault().TryResolve<IArmorAddonGetter>(Program.Settings.Cache, out var addon))
                {
                    // Adding Armor sets
                    var slots = ArmorUtils.GetBodySlots(addon);
                    slots.Select(x => x.ToString()).ForEach(slot =>
                    {
                        ArmorsWithSlot.GetOrAdd(armorType).GetOrAdd(slot).TryAdd(armor.FormKey, armor.Value);
                    });


                    // Adding Armor meshes male
                    if (!IsEligibleForMeshMapping(armor)) continue;
                    string material = ArmorUtils.GetMaterial(armor);
                    if (addon.WorldModel != null && addon.WorldModel.Male != null)
                    {
                        if (File.Exists(Path.Combine(State.DataFolderPath, "Meshes", addon.WorldModel.Male.File)))
                        {
                            // Getting body armor related like data material, editorID, armor type etc
                            var armorMat = MaleArmorMeshes.GetOrAdd(material);
                            slots.ForEach(flag =>
                            {
                                var slot = flag.ToString();
                                if (!armorMat.ContainsKey(slot))
                                    armorMat.TryAdd(slot, addon.WorldModel.Male.File);
                            });
                        }
                    }

                    // Adding Armor meshes female
                    if (addon.WorldModel != null && addon.WorldModel.Female != null)
                    {
                        if (File.Exists(Path.Combine(State.DataFolderPath, "Meshes", addon.WorldModel.Female.File)))
                        {
                            // Getting body armor related like data material, editorID, armor type etc
                            var armorMat = FemaleArmorMeshes.GetOrAdd(material);
                            slots.ForEach(flag =>
                            {
                                var slot = flag.ToString();
                                if (!armorMat.ContainsKey(slot))
                                    armorMat.TryAdd(slot, addon.WorldModel.Female.File);
                            });
                        }
                    }
                }
            }
        }

        private void AddMissingGenderMeshes(IArmorGetter armor)
        {
            string material = ArmorUtils.GetMaterial(armor);
            if (!armor.Armature.EmptyIfNull().Any()) return;
            IArmorAddonGetter addon = armor.Armature.FirstOrDefault().Resolve(Program.Settings.Cache);
            ArmorUtils.GetBodySlots(addon).ForEach(flag =>
            {
                var slot = flag.ToString();
                // Mapping Male Models with Female only Armor
                if (addon.WorldModel != null && addon.WorldModel.Male == null
                    && MaleArmorMeshes.GetOrAdd(material).TryGetValue(slot, out var maleMesh))
                {
                    Patch = FileUtils.GetIncrementedMod(Patch);
                    IArmorAddon localAddon = Patch.ArmorAddons.GetOrAddAsOverride(addon);
                    localAddon.WorldModel.Male = new();
                    localAddon.WorldModel.Male.File = maleMesh;
                    Logger.DebugFormat("Missing male mesh added for armor: " + armor.EditorID);
                    return;
                }

                // Mapping Female Models with Female only Armor
                if (addon.WorldModel != null && addon.WorldModel.Female == null
                    && FemaleArmorMeshes.GetOrAdd(material).TryGetValue(slot, out var femaleMesh))
                {
                    Patch = FileUtils.GetIncrementedMod(Patch);
                    IArmorAddon localAddon = Patch.ArmorAddons.GetOrAddAsOverride(addon);
                    localAddon.WorldModel.Female = new();
                    localAddon.WorldModel.Female.File = femaleMesh;
                    Logger.DebugFormat("Missing female mesh added for armor: " + armor.EditorID);
                }
                if (addon.WorldModel == null || addon.WorldModel.Female == null || addon.WorldModel.Male == null)
                    Logger.DebugFormat("M=No Matching meshes fond for armor {0}::{1} ", armor.EditorID, armor.FormKey);
            });
        }

        private bool IsEligibleForMeshMapping(IArmorGetter armor)
        {
            return ArmorUtils.IsValidArmor(armor)
                    & armor.Armature != null
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
