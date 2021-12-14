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

        private ISkyrimMod? PatchedMod;
        readonly IPatcherState<ISkyrimMod, ISkyrimModGetter> State;

        private List<string> SPID = new();
        private static object locker = new object();
        private List<string> DividableFactions = new();
        private Dictionary<string, TArmorCategory> GrouppedArmorSets = new();
        private Dictionary<string, HashSet<string>> OutfitArmorTypes = new();
        private readonly Dictionary<string, string> Regex4outfits = Settings.PatcherSettings.OutfitRegex;
        private Dictionary<string, Dictionary<string, Dictionary<FormKey, float>>> ArmorsWithSlot = new();

        public OutfitManager(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            State = state;
            DividableFactions = Settings.PatcherSettings.DividableFactions.ToLower().Split("|").ToList();
        }

        public void Process()
        {
            //var outfits = GetPatchableOutfits();
            //GroupOutfits(outfits);
            GroupOutfits();

            GenerateArmorSlotData();
            GetOutfitArmorTypes();
            CreateArmorsSets();

            FilterGaurdAndMaterialBasedOutfits();
            CreateNewOutfits();
            ResolveOutfitOverrides();

            //CreateNPCKeywords();
            AddGroppedArmorSetToSPIDFile();

            SPID = SPID.Where(x => x.Trim().Any()).Select(x => x.Replace("~Skyrim.esm", "")).ToList();
            File.WriteAllLines(Path.Combine(State.DataFolderPath, Settings.IniName), SPID);
        }

        /**
         * Returns armor list with are associated with Outfits for Skyrim masters
         */
        private void GetOutfitArmorTypes()
        {
            Logger.InfoFormat("");
            foreach (var outfit in State.LoadOrder.PriorityOrder.WinningOverrides<IOutfitGetter>()
                .Where(x => Settings.PatcherSettings.Masters.Contains(x.FormKey.ModKey.FileName)))
            {
                // Creating Outfit Category based on ArmorType
                var t = OutfitUtils.GetOutfitArmorType(outfit);
                OutfitArmorTypes.GetOrAdd(t).Add(outfit.EditorID);
            }
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
            Logger.InfoFormat("Getting outfits....");
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
                }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1 });

            foreach (IOutfitGetter outfit in State.LoadOrder.PriorityOrder
                .Where(x => !Settings.UserSettings.ModsToSkip.Contains(x.ModKey.FileName))
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
         * Filter outfits based on the its uses count
         */
        private void GroupOutfits()
        {
            Logger.InfoFormat("Getting outfits to be patched....");
            foreach (IOutfitGetter outfit in State.LoadOrder.PriorityOrder
                .Where(x => !Settings.UserSettings.ModsToSkip.Contains(x.ModKey.FileName))
                .WinningOverrides<IOutfitGetter>()
                .Where(x => ArmorUtils.IsValidOutfit(x)))
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
            Logger.InfoFormat("Grouping Armors....");
            // For each armor mod creating armor sets, skipping mods which have the outfit records.
            // If these outfit records are overriden it will be resolved later in the patch
            var modlists = State.LoadOrder.PriorityOrder
                .Where(x => Settings.UserSettings.ArmorModsForOutfits.ContainsKey(x.ModKey.FileName)
                    && x.Mod.Armors.Count > 0);

            var patchName = Settings.PatcherSettings.PatcherPrefix + "Armors Part 1.esp";
            ISkyrimMod patch = FileUtils.GetOrAddPatch(patchName);
            List<ModKey> masters = new();

            var watch = System.Diagnostics.Stopwatch.StartNew();
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
                        if (ArmorUtils.IsBodyArmor(armor)) bodies.Add(armor);
                        else if (ArmorUtils.IsJewelry(armor)) jewelries.Add(new(armor));
                        else others.Add(new(armor));

                        if (!masters.Contains(armor.FormKey.ModKey))
                        {
                            if (masters.Count() > 250)
                            {
                                lock (locker)
                                {
                                    patch = GetIncrementedMod(patch);
                                    masters.Clear();
                                }
                            }
                            masters.Add(armor.FormKey.ModKey);
                        }
                    });

                int bodyCount = bodies.Count;
                var commongStrCount = 0;
                if (bodyCount > 5 && Settings.UserSettings.ArmorModsForOutfits[mod.ModKey.FileName].Contains("Generic"))
                    commongStrCount = HelperUtils.GetCommonItems(others.Select(x => HelperUtils.SplitString(x.Name)).ToList())
                               .Where(x => !x.IsNullOrEmpty()).Count();

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
                    armorSet.CreateMatchingSetFrom(armors, bodyCount == 1, commongStrCount);

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

                    lock (locker)
                    {
                        patch = GetIncrementedMod(patch);
                        armorSet.CreateLeveledList();
                    }

                    // Add using materials
                    var group = armorSet.Material;
                    AddArmorSetToGroup(group, armorSet);

                    // Add using provided category
                    List<string> modsFactions = Settings.UserSettings.ArmorModsForOutfits[mod.ModKey.FileName];
                    modsFactions.Remove("Generic");
                    foreach (string fgroup in modsFactions)
                    {
                        AddArmorSetToGroup(fgroup, armorSet);
                    }

                    if (i > 0 && (i + 1) % 100 == 0)
                        Logger.InfoFormat("Created {0}/{1} armor-set for: {2}", i + 1, bodyCount, mod.ModKey.FileName);
                }
                Logger.InfoFormat("Created {0}/{0} armor-set for: {1}", bodyCount, mod.ModKey.FileName);
            }

            watch.Stop();
            Logger.InfoFormat("Time Spend: " + watch.ElapsedMilliseconds / 1000);
            Logger.InfoFormat("Created armor sets for armor mods based on materials....\n\n");
        }

        private void CreateNewOutfits()
        {
            var patchName = Settings.PatcherSettings.PatcherPrefix + "Outfits Part 1.esp";
            PatchedMod = FileUtils.GetOrAddPatch(patchName);
            Logger.InfoFormat("Creating Outfit records...");

            if (GrouppedArmorSets.Remove("Unknown", out var temp)) {
                GrouppedArmorSets.GetOrAdd("Bandit", () => new TArmorCategory("Bandit")).AddArmorSets(temp.Armors);
                GrouppedArmorSets.GetOrAdd("Mercenary", () => new TArmorCategory("Mercenary")).AddArmorSets(temp.Armors);
            }

            GrouppedArmorSets = new(GrouppedArmorSets.Where(x => !x.Value.Armors.IsEmpty));
            GrouppedArmorSets.ForEach(rec =>
            {
                PatchedMod = GetIncrementedMod(PatchedMod);
                rec.Value.CreateOutfits(PatchedMod);
                Logger.InfoFormat("Created new outfit record for: " + rec.Key);
            });
            Logger.InfoFormat("Added new outfits records...\n\n");
        }

        private void ResolveOutfitOverrides()
        {
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
                            PatchedMod = GetIncrementedMod(PatchedMod);
                            var ll = OutfitUtils.CreateLeveledList(PatchedMod, items, "ll_" + r.Record.EditorID + 0, 1, LeveledItem.Flag.UseAll);
                            oLLs.Add(ll);
                        }
                    });

                    // Reverting Overriden outfit by armor mods added in the patcher
                    var lastNonModOutfit = Settings.Cache.ResolveAllContexts<IOutfit, IOutfitGetter>(winningOtft.FormKey)
                        .Where(c => !Settings.UserSettings.ArmorModsForOutfits.ContainsKey(c.ModKey.FileName));

                    if (lastNonModOutfit.Count() > 1)
                    {
                        // Getting outfit records form armor mods added in the patcher and patching those
                        Outfit nOutfit = PatchedMod.Outfits.GetOrAddAsOverride(lastNonModOutfit.First().Record);
                        var items = nOutfit.Items.Select(x => Settings.Cache.Resolve<IItemGetter>(x.FormKey));
                        if (items.Count() == 1)
                        {
                            oLLs.Add(items.First());
                        }
                        else
                        {
                            PatchedMod = GetIncrementedMod(PatchedMod);
                            var ll = OutfitUtils.CreateLeveledList(PatchedMod, items, "ll_" + nOutfit.EditorID + 0, 1, LeveledItem.Flag.UseAll);
                            oLLs.Add(ll);
                        }

                        // Creating patched outfit
                        PatchedMod = GetIncrementedMod(PatchedMod);
                        LeveledItem sLL = OutfitUtils.CreateLeveledList(PatchedMod, oLLs.Distinct(), "sll_" + outfit.EditorID, 1, Settings.LeveledListFlag);
                        nOutfit.Items = new();
                        nOutfit.Items.Add(sLL);
                    }
                }
            }
        }

        private void AddGroppedArmorSetToSPIDFile()
        {
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
            var type = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.ArmorTypeRegex, eid);
            if (!type.Any()) type = new string[] { "" };

            var groups = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, eid);
            groups.ForEach(f =>
            {
                type.ForEach(eidType =>
                {
                    var k = DividableFactions.Contains(f.ToLower()) ? f + eidType : f;
                    GrouppedArmorSets.GetOrAdd(k, () => new TArmorCategory(k)).AddOutfit(outfit);
                });
            });
            if (groups.Any()) Logger.DebugFormat("Outfit Processed: {0}[{1}]", eid, outfit.FormKey);
            else Logger.DebugFormat("Outfit Missed: {0}[{1}]", eid, outfit.FormKey);
        }

        private void AddArmorSetToGroup(string group, TArmorSet armorSet)
        {
            GrouppedArmorSets.GetOrAdd(group, ()=>new TArmorCategory(group)).Armors.Add(armorSet);
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
                    common.ForEach(c => x.Outfits.TryRemove(c));
                })); ;
        }

        private void GenerateArmorSlotData()
        {
            Logger.InfoFormat("Generating armor cache...");
            var masters = Settings.UserSettings.ArmorModsForOutfits.Keys.ToList();
            masters.AddRange(State.LoadOrder.Where(x => Settings.PatcherSettings.Masters.Contains(x.Key.FileName)).Select(x => x.Key.FileName.ToString()));

            foreach (IArmorGetter armor in State.LoadOrder.PriorityOrder
                .WinningOverrides<IArmorGetter>()
                .Where(x=>ArmorUtils.IsValidArmor(x)))
            {
                string armorType = ArmorUtils.GetArmorType(armor);
                if (armor.Armature.FirstOrDefault().TryResolve<IArmorAddonGetter>(Settings.Cache, out var addon))
                {
                    // Adding Armor sets
                    var slots = ArmorUtils.GetBodySlots(addon);
                    if (masters.Contains(armor.FormKey.ModKey.FileName))
                    {
                        slots.Select(x => x.ToString()).ForEach(slot =>
                        {
                            ArmorsWithSlot.GetOrAdd(armorType).GetOrAdd(slot).TryAdd(armor.FormKey, armor.Value);
                        });
                    }
                }
            }
            Logger.InfoFormat("Armor cache generated...\n\n");
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

        private ISkyrimMod GetIncrementedMod(ISkyrimMod mod)
        {
            if (mod.EnumerateMajorRecords().Where(x => x.FormKey.ModKey.Equals(mod.ModKey)).Count() < 2040)
                return mod;

            var name = "";
            try
            {
                var indx = Int32.Parse(mod.ModKey.Name.Last().ToString());
                name = mod.ModKey.Name.Replace(indx.ToString(), (indx + 1).ToString());
            }
            catch
            {
                name = mod.ModKey.Name + " 1";
            }
            return FileUtils.GetOrAddPatch(name);
        }
    }
}
