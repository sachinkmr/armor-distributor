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

        private Random Random = new();
        private List<string> SPID = new();        
        Dictionary<string, HashSet<string>> KeywordedNPCs = new();
        private Dictionary<string, NpcKeyword> NpcKeywords = new();
        private Dictionary<string, HashSet<FormKey>> OutfitsWithNPC = new();
        private Dictionary<string, TArmorGroup> GrouppedArmorSets = new();
        private Dictionary<TArmorType, HashSet<string>> OutfitArmorTypes = new();
        private Dictionary<string, HashSet<TArmorType>> OutfitArmorTypesRev = new();
        private readonly Dictionary<string, Dictionary<string, string>> MaleArmorMeshes = new();
        private readonly Dictionary<string, Dictionary<string, string>> FemaleArmorMeshes = new();
        private readonly Dictionary<string, Dictionary<string, HashSet<string>>> RandomArmorMeshes = new();
        private Dictionary<TArmorType, Dictionary<TBodySlot, Dictionary<FormKey, float>>> ArmorsWithSlot = new();

        public OutfitManager(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            State = state;
        }

        public ISkyrimMod Process(ISkyrimMod patch)
        {
            var outputfile = "logs/"+ DateTime.Now.ToString("F").Replace(":", "-") + ".json";
            Console.WriteLine("Logs Directory: " + Directory.GetParent(outputfile).FullName);
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
            MergeDefaultOutfits();
            MergeUnAssignedOutfits();
            ForceAssignMissingArmors();
            // Clearing data
            //CategorizeNPCs();

            MaleArmorMeshes.Clear();
            FemaleArmorMeshes.Clear();
            RandomArmorMeshes.Clear();

            // Generating & Writing data to SPID File
            if (Program.Settings.UserSettings.ProcessNPCs)
            {
                CreateNPCKeywords();
                AddGroppedArmorSetToSPIDFile();
                SPID = SPID.Where(x => x.Trim().Any()).ToList();
                File.WriteAllLines(Path.Combine(State.DataFolderPath, Program.Settings.IniName), SPID);
            }

            // Writing Debug logs
            if (Program.Settings.UserSettings.DumpDebugData)
                DumpDebugLogs(outputfile);
            return Patch;
        }

        private void ForceAssignMissingArmors()
        {
            List<FormKey> l = new();
            var cache = State.LoadOrder.ToMutableLinkCache();

            GrouppedArmorSets.Values.ForEach(v =>
                v.GenderOutfit.Where(x => !x.Key.Equals(TGender.Common))
                .ToDictionary().Values
                .ForEach(a => l.AddRange(a.Values))
            );

            l.Select(o => cache.Resolve<IOutfitGetter>(o))
                .ForEach(o => OutfitUtils.GetArmorList(cache, o)
                .ForEach(a => AddMissingGenderMeshes(a, true)));

            cache = State.LoadOrder.ToMutableLinkCache();
            GrouppedArmorSets.Values.ForEach(category =>
            {
                Dictionary<TArmorType, List<FormKey>> outfits = new();
                var common = category.GenderOutfit.GetOrAdd(TGender.Common);
                common.ForEach(a => outfits.GetOrAdd(a.Key).Add(a.Value));

                category.GenderOutfit.Where(x => !x.Key.Equals(TGender.Common))
                .ToDictionary()
                .ForEach(gt =>
                    gt.Value.ForEach(to =>
                    {
                        var type = to.Key;
                        var otft = cache.Resolve<IOutfitGetter>(to.Value);
                        outfits.GetOrAdd(type).Add(otft.FormKey);
                    })
                );


                outfits.Where(x => x.Value.Count() > 1).ForEach(to =>
                {
                    List<IFormLink<IItemGetter>> itms = new();
                    to.Value.Select(o => cache.Resolve<IOutfitGetter>(o))
                        .ForEach(o => itms.AddRange(o.Items.Select(ii=> ii.FormKey.AsLink<IItemGetter>())));

                    Outfit nOtft = null;
                    var eid = category.Name + "_" + TGender.Common + "_" + to.Key;
                    Patch = FileUtils.GetIncrementedMod(Patch);
                    var LL = OutfitUtils.CreateLeveledList(Patch, itms, "mLL_" + eid + "_ALL", 1, Program.Settings.LeveledListFlag);
                    if (cache.TryResolve<IOutfitGetter>(common.GetOrAdd(to.Key), out var otft))
                    {
                        nOtft = Patch.Outfits.GetOrAddAsOverride(otft);
                        nOtft.Items = new(LL.AsLink().AsEnumerable());
                    }
                    else
                        nOtft = OutfitUtils.CreateOutfit(Patch, eid, itms);
                    common[to.Key]= nOtft.FormKey;
                });
            });
        }

        /**
         * Returns a dictonary with outfit and number of times those are used.
         */
        private void GetPatchableOutfits()
        {
            Logger.InfoFormat("Fetching outfit records...");
            State.LoadOrder.PriorityOrder
                .WinningOverrides<INpcGetter>()
                .Where(x => !Program.Settings.UserSettings.ModsToSkip.Contains(x.FormKey.ModKey) 
                        && !Program.Settings.UserSettings.NPCToSkip.Contains(x.FormKey))
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

            // Adding valid outfits to Armor Category group
            var outfitNpcCount = Program.Settings.UserSettings.MinimumNpcForOutfit;
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
                    else if (!OutfitsWithNPC[outfit.EditorID].Any()||OutfitsWithNPC[outfit.EditorID].Count() >= outfitNpcCount)
                        AddOutfitToGroup(outfit);
                }
//                else Logger.DebugFormat("Skipping Invalid OTFT: " + outfit.EditorID);
            }
        }

        /**
         * Returns armor list with are associated with Outfits for Skyrim masters
         */
        private void CategorizeOutfits()
        {
            Logger.InfoFormat("Processing Outfits to Patch...");
            foreach (var outfit in State.LoadOrder.PriorityOrder
                .Where(x => !Program.Settings.UserSettings.ModsToSkip.Contains(x.ModKey.FileName))
                .WinningOverrides<IOutfitGetter>()
                .Where(x => !Program.Settings.UserSettings.ModsToSkip.Contains(x.FormKey.ModKey))
                .Where(x => OutfitUtils.IsValidOutfit(x)))
            {
                // Creating Outfit Category based on ArmorType
                var types = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.ArmorTypeRegex, outfit.EditorID)
                    .Select(x=> x.ToEnum<TArmorType>());
                if (!types.Any()) types = OutfitUtils.GetOutfitArmorType(State.LinkCache, outfit);
                if (!types.Any()) types = new List<TArmorType>() { TArmorType.Unknown };

                types.ForEach(x =>
                {
                    OutfitArmorTypes.GetOrAdd(x).Add(outfit.EditorID);
                    OutfitArmorTypesRev.GetOrAdd(outfit.EditorID).Add(x);
                });
            }
            Logger.InfoFormat("Processed Outfits to Patch...");
        }

        /*
         * Filter outfits based on the its uses count
         */
        private void GroupOutfits()
        {
            Logger.InfoFormat("Getting outfits to be patched....");
            foreach (IOutfitGetter outfit in State.LoadOrder.PriorityOrder
                .WinningOverrides<IOutfitGetter>()
                .Where(x => !Program.Settings.UserSettings.ModsToSkip.Contains(x.FormKey.ModKey))
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
                        && x.Keywords != null && x.Armature!=null && x.Armature.Any())
                    .ForEach(armor =>
                    {
                        Patch = FileUtils.GetIncrementedMod(Patch);
                        if (ArmorUtils.IsEligibleForMeshMapping(armor)) AddMissingGenderMeshes(armor, true);
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

                var weapons = mod.Weapons.EmptyIfNull().Select(w => new TWeapon(w)).ToHashSet();
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
                            .Union(jewelries).ToList();

                    armorSet.CreateMatchingSetFrom(armors, bodyCount == 1, commanName, commanEID);

                    //Distributing weapons as well
                    if (Program.Settings.UserSettings.DistributeWeapons)
                        armorSet.CreateMatchingSetFrom(weapons, bodyCount);

                    // Checking for Boots
                    if (!armorSet.Armors.Where(x => x.BodySlots.Contains(TBodySlot.Feet)).Any())
                    {
                        var type = armorSet.Type;
                        var feetsAll = ArmorsWithSlot[type][TBodySlot.Feet]
                            .OrderBy(x => x.Value);
                        var feets = feetsAll.Where(x => x.Value <= body.Value);
                        var feet = feets.Any() ? feets.First().Key : feetsAll.First().Key;
                        armorSet.AddArmor(new TArmor(Program.Settings.Cache.Resolve<IArmorGetter>(feet), armorSet.Material));
                    }

                    // Creating Leveled List
                    Patch = armorSet.CreateLeveledList(Patch);

                    // Add using materials
                    AddArmorSetToGroup(armorSet.Material, armorSet);

                    // Add using provided category
                    List<string> modsCategories = Program.Settings.UserSettings.ArmorMods[mod.ModKey.FileName];
                    modsCategories.Remove("Generic");
                    foreach (string fgroup in modsCategories.Distinct())
                        AddArmorSetToGroup(fgroup, armorSet);                   

                    if (i > 0 && (i + 1) % 100 == 0)
                        Logger.InfoFormat("Created {0}/{1} armor-set for: {2}", i + 1, bodyCount, mod.ModKey.FileName);
                }
                Logger.InfoFormat("Created {0}/{0} armor-set for: {1}", bodyCount, mod.ModKey.FileName);
            }
            Logger.InfoFormat("Created {0} matching armor sets from armor mods...\n", totalSets);
            ArmorsWithSlot.Clear();
        }

        private void CreateNewOutfits()
        {
            Logger.InfoFormat("Creating New Outfits...");
            if (GrouppedArmorSets.Remove("Unknown", out var temp))
            {
                var types = temp.Armorsets.GroupBy(a => a.Type).ToDictionary(x => x.Key, x => x.Select(b => b));
                foreach (var t in types)
                {
                    if (t.Key == TArmorType.Cloth)
                    {
                        GrouppedArmorSets.GetOrAdd("Merchant", () => new TArmorGroup("Merchant")).AddArmorSets(temp.Armorsets);
                    }
                    else
                    {
                        GrouppedArmorSets.GetOrAdd("Bandit", () => new TArmorGroup("Bandit")).AddArmorSets(t.Value);
                        GrouppedArmorSets.GetOrAdd("Warrior", () => new TArmorGroup("Warrior")).AddArmorSets(t.Value);
                        GrouppedArmorSets.GetOrAdd("Mercenary", () => new TArmorGroup("Mercenary")).AddArmorSets(t.Value);
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
            Logger.InfoFormat("\nResolving outfit conflicts...");
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

        private void MergeDefaultOutfits()
        {
            // Merging Default outfits with their respective categories
            Logger.InfoFormat("Merging default outfits with the new outfits...");
            var cache = State.LoadOrder.ToMutableLinkCache();
            foreach (var group in GrouppedArmorSets)
            {
                var category = group.Value.GenderOutfit.GetOrAdd(TGender.Common);
                Dictionary<TArmorType, List<IItemGetter>> dict = new();
                group.Value.Outfits.Keys.ForEach(o =>
                {
                    List<IItemGetter> list = new();
                    var otft = cache.Resolve<IOutfitGetter>(o);
                    var itms = otft.Items.Where(i => State.LinkCache.TryResolve<IItemGetter>(i.FormKey, out var a))
                                .Select(i => State.LinkCache.Resolve<IItemGetter>(i.FormKey));
                    
                    Patch = FileUtils.GetIncrementedMod(Patch);
                    var ll = OutfitUtils.CreateLeveledList(Patch, itms, "LL_" + otft.EditorID, 1, LeveledItem.Flag.UseAll);
                    list.Add(ll);
                    
                    if (OutfitArmorTypesRev.ContainsKey(otft.EditorID))
                        OutfitArmorTypesRev[otft.EditorID].ForEach(armorType => dict.GetOrAdd(armorType).AddRange(list));
                    else                    
                        Logger.DebugFormat("outfit category not found: {0}:{1}", otft.EditorID, otft.FormKey.ToString());
                });

                dict.ForEach(t =>
                {
                    Patch = FileUtils.GetIncrementedMod(Patch);
                    if (category.ContainsKey(t.Key))
                    {
                        var otft = cache.Resolve<IOutfitGetter>(category[t.Key]);
                        otft.Items.Select(i => cache.Resolve<LeveledItem>(i.FormKey))
                        .ForEach(l => {
                            var ll = Patch.LeveledItems.GetOrAddAsOverride(l);
                            OutfitUtils.AddItemsToLeveledList(Patch, ll, t.Value, 1);
                        });

                        Patch = FileUtils.GetIncrementedMod(Patch);
                        OutfitUtils.GetArmorList(State.LinkCache, otft).ForEach(a => AddMissingGenderMeshes(a, true));
                    }
                    else
                    {
                        var id = group.Key + "_"+ TGender.Common.ToString() + "_" + t.Key;
                        Outfit newOutfit = OutfitUtils.CreateOutfit(Patch, id, t.Value);
                        category.Add(t.Key, newOutfit.FormKey);

                        Patch = FileUtils.GetIncrementedMod(Patch);
                        OutfitUtils.GetArmorList(State.LinkCache, newOutfit).ForEach(a => AddMissingGenderMeshes(a, true));
                    }
                });
            }


            // Merging new outfits with their respective Default outfits
            //Logger.InfoFormat("Merging new outfits with the default outfits...");
            //cache = State.LoadOrder.ToMutableLinkCache();
            //foreach (var group in GrouppedArmorSets) {
            //    List<IItemGetter> list = new();
            //    var category = group.Value.GenderOutfit.GetOrAdd(TGender.Common);                
            //    category.Values.Select(x=>cache.Resolve<IOutfitGetter>(x))
            //        .ForEach(o=>list.AddRange(o.Items.Select(i=>cache.Resolve<ILeveledItem>(i.FormKey))));
                
            //    var outfits = group.Value.Outfits.Select(o => cache.Resolve<IOutfitGetter>(o.Key));
            //    outfits.Where(o => Program.Settings.UserSettings.PatchNonVanillaOutfits
            //            || Settings.PatcherSettings.Masters.Contains(o.FormKey.ModKey.FileName))
            //        .ForEach(otft =>
            //    {
            //        LeveledItem LL = null;
            //        Patch = FileUtils.GetIncrementedMod(Patch);
            //        var itms = otft.Items.Where(i => cache.TryResolve<IItemGetter>(i.FormKey, out var a))
            //                    .Select(i => cache.Resolve<IItemGetter>(i.FormKey));
                    
            //        LL = OutfitUtils.CreateLeveledList(Patch, itms.Union(list), "mLL1_" + otft.EditorID, 1, LeveledItem.Flag.UseAll);
            //        var ot = Patch.Outfits.GetOrAddAsOverride(otft);
            //        ot.Items = new(LL.AsLink().AsEnumerable());
                    
            //        Patch = FileUtils.GetIncrementedMod(Patch);
            //    });
            //}
        }

        private void MergeUnAssignedOutfits()
        {
            // Merging outfits which are not assigned to any NPC
            Logger.InfoFormat("Merging unassigned outfits with the new outfits...");
            var cache = State.LoadOrder.ToMutableLinkCache(Patch);
            var outfits = State.LoadOrder.PriorityOrder
                .Where(l => !l.ModKey.FileName.String.StartsWith(Settings.PatcherSettings.PatcherPrefix) && !Program.Settings.UserSettings.ModsToSkip.Contains(l.ModKey.FileName))
                .WinningOverrides<IOutfitGetter>()
                .Where(x => !Program.Settings.UserSettings.ModsToSkip.Contains(x.FormKey.ModKey))
                .Where(o => OutfitUtils.IsValidOutfit(o) 
                        && OutfitsWithNPC.ContainsKey(o.EditorID) 
                        && !OutfitsWithNPC[o.EditorID].Any());

            foreach (var otft in outfits)
            {
                // Add missing gender meshes
                List<string> categories = new();
                var gender = TGender.Common;
                List<TArmorType> types = new();
                if (otft.EditorID.EndsWith(Settings.PatcherSettings.OutfitSuffix)
                    && otft.EditorID.StartsWith(Settings.PatcherSettings.OutfitPrefix))
                {
                    var tokens = Regex.Replace(otft.EditorID, Settings.PatcherSettings.OutfitSuffix + "|" + Settings.PatcherSettings.OutfitPrefix, "").Split('_');
                    categories.Add(tokens[0]);
                    gender = tokens[1].ToEnum<TGender>();
                    types.Add(tokens[2].ToEnum<TArmorType>());
                }
                else
                {
                    categories = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, otft.EditorID).ToList();
                    types = OutfitUtils.GetOutfitArmorType(otft.EditorID);
                    Logger.DebugFormat("Unused outfit: [{0}][{1}]=>[{2}]", otft.FormKey, otft.EditorID, string.Join(", ",categories));
                }

                foreach (var category in categories) {
                    Patch = FileUtils.GetIncrementedMod(Patch);
                    OutfitUtils.GetArmorList(State.LinkCache, otft).ForEach(a => AddMissingGenderMeshes(a, true));
                    
                    foreach (var type in types.Where(t => t != TArmorType.Unknown))
                    {
                        var genderotft = GrouppedArmorSets.GetOrAdd(category, () => new TArmorGroup(category)).GenderOutfit.GetOrAdd(gender);
                        if (genderotft.ContainsKey(type))
                        {
                            Patch = FileUtils.GetIncrementedMod(Patch);
                            IOutfitGetter o = cache.Resolve<IOutfitGetter>(genderotft[type]);
                            var itms = o.Items.Select(i => cache.Resolve<IItemGetter>(i.FormKey))
                                .Union(otft.Items.Select(i => cache.Resolve<IItemGetter>(i.FormKey)));

                            var LLs = OutfitUtils.CreateLeveledList(Patch, itms, o.EditorID + "1", 1, Program.Settings.LeveledListFlag);
                            Outfit ot = Patch.Outfits.GetOrAddAsOverride(o);
                            ot.Items = new();
                            ot.Items.Add(LLs);
                        }
                        else genderotft.Add(type, otft.FormKey);
                    }
                }
            }
        }

        private void CategorizeNPCs()
        {
            Logger.InfoFormat("\nCategorizing NPCs...");
            Dictionary<string, Dictionary<string, Dictionary<FormKey, string>>> map = new();

            // Race Based
            if (Program.Settings.UserSettings.RaceBasedDistribution)
            {
                Logger.InfoFormat("Parsing NPC Races...");
                State.LoadOrder.PriorityOrder.WinningOverrides<IRaceGetter>()
                    .Where(x => !Program.Settings.UserSettings.ModsToSkip.Contains(x.FormKey.ModKey))
                    .ForEach(c =>
                    {
                        var groups = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, c.EditorID);
                        groups.ForEach(group =>
                        {
                            var types = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.ArmorTypeRegex, c.EditorID);
                            types.ForEach(t => map.GetOrAdd(group).GetOrAdd(t).Add(c.FormKey, c.EditorID));
                        });
                    });
            }

            // Class Based
            if (Program.Settings.UserSettings.ClassBasedDistribution)
            {
                Logger.InfoFormat("Parsing NPC Classes...");
                State.LoadOrder.PriorityOrder.WinningOverrides<IClassGetter>()
                    .Where(x => !Program.Settings.UserSettings.ModsToSkip.Contains(x.FormKey.ModKey))
                    .ForEach(c =>
                    {
                        var id = Regex.Replace(c.EditorID, "class|combat", "", RegexOptions.IgnoreCase);
                        var groups = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, id);
                        groups.ForEach(group =>
                        {
                            var types = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.ArmorTypeRegex, c.EditorID);
                            types.ForEach(t => map.GetOrAdd(group).GetOrAdd(t).Add(c.FormKey, c.EditorID));
                        });
                    });
            }

            // Faction Based Distribution
            if (Program.Settings.UserSettings.FactionBasedDistribution)
            {                
                Logger.InfoFormat("Parsing NPC Factions...");
                State.LoadOrder.PriorityOrder.WinningOverrides<IFactionGetter>()
                    .Where(x => !Program.Settings.UserSettings.ModsToSkip.Contains(x.FormKey.ModKey))
                .Where(r => NPCUtils.IsValidFaction(r))
                .ForEach(c =>
                {
                    var groups = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, c.EditorID);
                    groups.ForEach(group =>
                    {
                        var types = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.ArmorTypeRegex, c.EditorID);
                        types.ForEach(t => map.GetOrAdd(group).GetOrAdd(t).Add(c.FormKey, c.EditorID));
                    });
                });
            }

            // Putting Race and Classes data in armor sets
            map.ForEach(p => GrouppedArmorSets.GetOrAdd(p.Key, () => new TArmorGroup(p.Key)).Identifiers = p.Value);
        }

        private void CreateNPCKeywords()
        {
            Logger.InfoFormat("\nCreating Keywords...");
            // Creating Keywords for NPCs
            Patch = FileUtils.GetIncrementedMod(Patch);
            GrouppedArmorSets.Keys
                .Union(Settings.PatcherSettings.ArmorTypeRegex.Keys)
                .ForEach(key => {
                    NpcKeywords.GetOrAdd(key, () => new(Patch, key));
                });

            // Assigning Keyword to NPC
            Logger.InfoFormat("Assigning Keywords to NPCs...");
            int count = 0;
            var cache = State.LoadOrder.ToMutableLinkCache();
            var npcs = State.LoadOrder.PriorityOrder
                .WinningOverrides<INpcGetter>()
                .Where(x => !Program.Settings.UserSettings.ModsToSkip.Contains(x.FormKey.ModKey)
                    || !Program.Settings.UserSettings.NPCToSkip.Contains(x.FormKey))
                .Where(n => n.DefaultOutfit != null && NPCUtils.IsValidActorType(n));
            foreach (var npc in npcs)
            {
                Patch = FileUtils.GetIncrementedMod(Patch);

                var identifier = string.Empty;
                var npcType = string.Empty;

                // Faction Based
                if (Program.Settings.UserSettings.FactionBasedDistribution)
                    npc.Factions.EmptyIfNull().ForEach(f => {
                        if (State.LinkCache.TryResolve<IFactionGetter>(f.Faction.FormKey, out var fac)
                            && NPCUtils.IsValidFaction(fac))
                            identifier += " " + fac.EditorID;
                    });


                //Class based
                if (Program.Settings.UserSettings.ClassBasedDistribution
                    && State.LinkCache.TryResolve<IClassGetter>(npc.Class.FormKey, out var cls)
                    && NPCUtils.IsValidClass(cls))
                    identifier += " " + Regex.Replace(cls.EditorID, "class|combat", "", RegexOptions.IgnoreCase);

                // Outfit of NPC
                if (State.LinkCache.TryResolve<IOutfitGetter>(npc.DefaultOutfit.FormKey, out var otft)
                    && OutfitUtils.IsValidOutfit(otft))
                {
                    identifier += " " + otft.EditorID;
                    OutfitArmorTypesRev.GetOrAdd(otft.EditorID).UnionWith(OutfitUtils.GetOutfitArmorType(cache, otft));
                }

                //Race based
                if (Program.Settings.UserSettings.RaceBasedDistribution
                    && State.LinkCache.TryResolve<IRaceGetter>(npc.Race.FormKey, out var race)
                    && NPCUtils.IsValidRace(race))
                    identifier += " " + race.EditorID;

                // Name/editor id based
                if (Program.Settings.UserSettings.NameBasedDistribution && NPCUtils.IsValidNPCName(npc))
                    identifier += " " + NPCUtils.GetName(npc);

                // Getting cloth or armor type
                HashSet<string> list = (otft != null && OutfitArmorTypesRev.ContainsKey(otft.EditorID)
                    ? OutfitArmorTypesRev[otft.EditorID] : new()).Select(x=>x.ToString()).ToHashSet();

                if (!list.Any())
                    list = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.ArmorTypeRegex, identifier)                        
                        .ToHashSet();

                var npcTypes = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, identifier);                    
                if (!npcTypes.Any() || !list.Any())
                {
                    var t = !npcTypes.Any() ? "Type" : !list.Any() ? "Outfit" : "Outfit & Type";
                    Logger.DebugFormat("NPC {2} Not Found: [{0}]=>[{1}]", npc.FormKey, identifier, t);
                    continue;
                }

                var skippable = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.SkippableRegex, identifier);                    

                //Adding Keywords for NPCs
                list.Union(npcTypes).Union(skippable)
                    .Select(x => NpcKeywords.GetOrAdd(x, () => new(Patch, x.ToString())))
                .ForEach(k => {
                    if (!npc.HasKeyword(k.FormKey)) {
                        var n = Patch.Npcs.GetOrAddAsOverride(npc);
                        if (n.Keywords == null) n.Keywords = new();
                        n.Keywords.Add(k.FormKey.AsLink<IKeywordGetter>());
                        KeywordedNPCs.GetOrAdd(k.Keyword).Add(identifier);
                    }                    
                });

                if (++count > 0 && count % 500 == 0)
                    Logger.InfoFormat("Processed {0} NPCs...", count);
            }
            Logger.InfoFormat("Processed {0} NPCs...", count);
        }

        private void AddGroppedArmorSetToSPIDFile()
        {
            Logger.InfoFormat("Creating SPID ini file...");

            // Distributing Outfits
            var percentage = 100 - Program.Settings.UserSettings.DefaultOutfitPercentage;
            var categories = Program.Settings.UserSettings.CategoriesToSkip.Select(x => x.ToString());
            var cache = State.LoadOrder.ToMutableLinkCache();

            // Skippable Types            
            var skippableList = NpcKeywords.Where(x=>x.Key.ToString().EndsWith("Skip"))
                .ToDictionary(x=> x.Key, x => "-" + x.Value.Keyword);

            var skippable = string.Join(",", skippableList.Values);
            foreach (var pair in GrouppedArmorSets.OrderBy(x => x.Key))
            {
                GrouppedArmorSets[pair.Key].GenderOutfit.Where(g => g.Value != null && g.Value.Any())
                    .ForEach(x =>
                    {
                        var gender = x.Key.Equals(TGender.Male) ? "M" : x.Key.Equals(TGender.Common) ? "NONE" : "F";
                        foreach (var armorType in x.Value.Keys)
                        {
                            List<string> filters = new();
                            var outfit = x.Value[armorType];
                            var armorTypekeyword = NpcKeywords[armorType.ToString()].Keyword;
                            var categoryKeyword = NpcKeywords[pair.Key].Keyword;
                            var keywords = "ActorTypeNPC+"
                                            + armorTypekeyword
                                            + "+"
                                            + categoryKeyword
                                            + ","
                                            + skippable;

                            if (pair.Key.EndsWith("Child"))
                                keywords = keywords.Replace("," + skippableList.GetOrDefault(TSkippableCategory.ChildSkip.ToString()), "");
                            if (pair.Key.EndsWith("Guards"))
                                keywords = keywords.Replace("," + skippableList.GetOrDefault(TSkippableCategory.GuardsSkip.ToString()), "");

                            if (!filters.Any()) filters.Add("NONE");
                            var filter = string.Join(",", filters);

                            var npcs = KeywordedNPCs.GetOrAdd(categoryKeyword).Intersect(KeywordedNPCs.GetOrAdd(armorTypekeyword)).Count();
                            var armorSets = OutfitUtils.GetArmorList(cache, cache.Resolve<IOutfitGetter>(outfit))
                                .Where(x => ArmorUtils.IsBodyArmor(x))
                                .Count();


                            string line1 = string.Format("\n;Outfits for {0}{1}/{2} [{3} armorsets to {4} NPCs]", pair.Key,
                                armorType, gender.Replace("NONE", "M+F").Replace("/U", ""), armorSets, npcs);
                            string line2 = String.Format("Outfit = 0x{0}~{1}|{2}|{3}|NONE|{4}|NONE|{5}",
                                outfit.ID.ToString("X"), outfit.ModKey.FileName, keywords, filter, gender, percentage);

                            // Skipping distributions
                            if(!x.Key.Equals(TGender.Common)) line2 = ";" + line2;
                            if (categories.Contains(pair.Key) || armorSets == 0 || npcs==0) line2 = ";" + line2;
                            if (Program.Settings.UserSettings.SkipGuardDistribution && pair.Key.EndsWith("Guards")) line2 = ";" + line2;
                            if (!Program.Settings.UserSettings.RaceBasedDistribution && pair.Key.EndsWith("Race")) line2 = ";" + line2;
                            if (Program.Settings.UserSettings.FilterUniqueNPC) SPID.Add(line2.Replace("/U|NONE", "|NONE"));

                            if (!line2.StartsWith(";")) {
                                SPID.Add(line1);
                                SPID.Add(line2);
                            }                            
                        }
                        SPID.Add(Environment.NewLine);
                    });
            }
        }

        private void DumpDebugLogs(string outputfile)
        {
            // Writing Debug logs
            Logger.InfoFormat("Writing Debug logs...");
            Dictionary<string, HashSet<string>> AllNPCs = new();
            var cache = State.LoadOrder.ToMutableLinkCache();
            State.LoadOrder.PriorityOrder
                .WinningOverrides<INpcGetter>()
                .Where(n => NPCUtils.IsValidActorType(n))
                .ForEach(n =>
                {
                    var name = NPCUtils.GetName(n);
                    var npc = AllNPCs.GetOrAdd(n.FormKey.ToString() + "::" + name);
                    n.Keywords.EmptyIfNull().ForEach(k => {
                        var cat = cache.Resolve<IKeywordGetter>(k.FormKey).EditorID;
                        npc.Add(cat);
                        if (GrouppedArmorSets.ContainsKey(cat)) GrouppedArmorSets[cat].NPCs.Add(n.FormKey, name);
                    });
                });

            Dictionary<string, object> all = new();
            all.Add("Categories", GrouppedArmorSets);
            all.Add("AllNPCs", AllNPCs);
            FileUtils.WriteJson(outputfile, all);
            Logger.InfoFormat("Data Written...");
        }

        private void AddOutfitToGroup(IOutfitGetter outfit)
        {
            // Handling already created outfits by patcher
            string eid = outfit.EditorID.StartsWith(Settings.PatcherSettings.OutfitPrefix) 
                && outfit.EditorID.EndsWith(Settings.PatcherSettings.OutfitSuffix) 
                ? outfit.EditorID.Split("_")[2] : outfit.EditorID;
            var groups = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, eid);
            groups.ForEach(group => GrouppedArmorSets.GetOrAdd(group, () => new TArmorGroup(group)).AddOutfit(outfit));
            if (!groups.Any()) 
                Logger.DebugFormat("Outfit Missed: {0}[{1}]=> {2}", eid, OutfitsWithNPC.GetOrAdd(eid).Count(), outfit.FormKey);
        }

        private void AddArmorSetToGroup(string group, TArmorSet armorSet)
        {
            GrouppedArmorSets.GetOrAdd(group, () => new TArmorGroup(group)).AddArmorSet(armorSet);
        }

        private void FilterGaurdAndMaterialBasedOutfits()
        {
            Dictionary<FormKey, string> list = new();
            //GrouppedArmorSets.Where(x => x.Key.EndsWith("Armor") || x.Key.EndsWith("Guards") || x.Key.EndsWith("Race"))
            GrouppedArmorSets.Where(x => x.Key.EndsWith("Guards"))
                .Select(x => x.Value.Outfits)
                .ForEach(x => x.ForEach(o =>
                {
                    if (Settings.PatcherSettings.Masters.Contains(o.Key.ModKey.FileName))
                        list.TryAdd(o.Key, o.Value);
                }));

            list.ForEach(o => GrouppedArmorSets
               // .Where(x => !x.Key.EndsWith("Armor") && !x.Key.EndsWith("Guards") && !x.Key.EndsWith("Race"))
                .Where(x => !x.Key.EndsWith("Guards") )
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
            Logger.InfoFormat("\nCategorizing armors based on body slots...");
            foreach (IArmorGetter armor in State.LoadOrder.PriorityOrder
                .WinningOverrides<IArmorGetter>()
                .Where(x => ArmorUtils.IsEligibleForMeshMapping(x)))
            {
                var armorType = ArmorUtils.GetArmorType(armor);
                string material = ArmorUtils.GetMaterial(armor);

                armor.Armature.ForEach(ar =>{
                    if (ar.TryResolve<IArmorAddonGetter>(State.LinkCache, out var addon))
                    {
                        // Adding Armor sets
                        var slots = ArmorUtils.GetBodySlots(addon);
                        slots.Select(x => x).ForEach(slot =>
                        {
                            ArmorsWithSlot.GetOrAdd(armorType).GetOrAdd(slot).TryAdd(armor.FormKey, armor.Value);
                        });

                        // Adding Armor meshes male                        
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
                });                
            }

            // Adding Random Mesh Map data
            Dictionary<string, HashSet<string>> map = new();
            FemaleArmorMeshes.ForEach(s => s.Value.ForEach(v => {
                if (!map.ContainsKey(v.Key)) map.GetOrAdd(v.Key).Add(v.Value);
            }));
            RandomArmorMeshes["Female"] = map;
            map.Clear();

            MaleArmorMeshes.ForEach(s => s.Value.ForEach(v => {
                if (!map.ContainsKey(v.Key)) map.GetOrAdd(v.Key).Add(v.Value);
            }));
            RandomArmorMeshes["Male"] = map;
        }

        private void AddMissingGenderMeshes(IArmorGetter armor, bool force=false)
        {
            if (!armor.Armature.EmptyIfNull().Any()) return;
            IArmorAddonGetter addon = armor.Armature.FirstOrDefault().Resolve(Program.Settings.Cache);

            if (addon.WorldModel == null)
            {
                Logger.DebugFormat("No Model attached for armor {0}::{1} ", armor.EditorID, armor.FormKey);
                return;
            }
            else if (addon.WorldModel.Female != null && addon.WorldModel.Male != null) {
                return;
            }

            bool missed = true;
            string material = ArmorUtils.GetMaterial(armor);
            ArmorUtils.GetBodySlots(addon).ForEach(flag =>
            {
                var slot = flag.ToString();
                IArmorAddon localAddon = null;
                Patch = FileUtils.GetIncrementedMod(Patch);
                // Mapping Male Models with Female only Armor
                if (addon.WorldModel.Male == null
                    && MaleArmorMeshes.GetOrAdd(material).TryGetValue(slot, out var maleMesh))
                {
                    localAddon = Patch.ArmorAddons.GetOrAddAsOverride(addon);
                    localAddon.WorldModel.Male = new();
                    localAddon.WorldModel.Male.File = maleMesh;
                    missed = false;
                }

                // Mapping Female Models with Female only Armor
                if (addon.WorldModel.Female == null
                    && FemaleArmorMeshes.GetOrAdd(material).TryGetValue(slot, out var femaleMesh))
                {
                    localAddon = Patch.ArmorAddons.GetOrAddAsOverride(addon);
                    localAddon.WorldModel.Female = new();
                    localAddon.WorldModel.Female.File = femaleMesh;
                    missed = false;
                }

                if (missed && force) {                    
                    if (addon.WorldModel.Female == null) {
                        var list = RandomArmorMeshes["Female"][slot];
                        localAddon = Patch.ArmorAddons.GetOrAddAsOverride(addon);
                        localAddon.WorldModel.Female = new();
                        localAddon.WorldModel.Female.File = list.ElementAt(Random.Next(list.Count));
                        missed = false;
                    }
                    if (addon.WorldModel.Male == null)
                    {
                        var list = RandomArmorMeshes["Male"][slot];
                        localAddon = Patch.ArmorAddons.GetOrAddAsOverride(addon);
                        localAddon.WorldModel.Male = new();
                        localAddon.WorldModel.Male.File = list.ElementAt(Random.Next(list.Count));
                        missed = false;
                    }
                }
            });
            if (missed)
                Logger.DebugFormat("No Matching meshes found for armor {0}::{1} ", armor.EditorID, armor.FormKey);
        }
    }
}
