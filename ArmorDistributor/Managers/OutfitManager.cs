using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
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
        Dictionary<string, HashSet<string>> AllNPCs = new();

        private string LoadOrderFile;
        private Random Random = new();
        private List<string> SPID = new();        
        Dictionary<string, HashSet<string>> KeywordedNPCs = new();
        private Dictionary<string, NpcKeyword> NpcKeywords = new();
        private Dictionary<string, HashSet<FormKey>> OutfitsWithNPC = new();
        private Dictionary<string, HashSet<string>> OutfitTypeRegex = new();
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
            LoadOrderFile = Path.Combine(Settings.LogsDirectory, "ArmorModsLoadOrder.txt");            
        }

        public ISkyrimMod Process(ISkyrimMod patch)
        {  
            Patch = FileUtils.GetIncrementedMod(patch);
            CategorizeNPCs(); DumpDebugLogs(); return Patch;

            // Processing Armors and Armor Mods
            //CategorizeArmorsForMissingMeshes();
            CreateArmorsSets();
            CreateNewOutfits();
            ResolveOutfitOverrides();

            if (!Program.Settings.UserSettings.CreateOutfitsOnly)
            {
                // Processing Outfits
                GetPatchableOutfits();
                CategorizeOutfits();
                MergeExistingOutfits();
                FilterGaurdOutfits();

                //ForceAssignMissingArmors();

                // Clearing data
                //MaleArmorMeshes.Clear();
                //FemaleArmorMeshes.Clear();
                //RandomArmorMeshes.Clear();
            }

            if (Program.Settings.UserSettings.AssignOutfits) {
                // Generating & Writing data to SPID File
                CategorizeNPCs();
                CreateSPID();                

                //CreateNPCKeywords();
                //AddGroppedArmorSetToSPIDFile();

                SPID = SPID.Where(x => x.Trim().Any()).ToList();
                File.WriteAllLines(Path.Combine(State.DataFolderPath, Program.Settings.IniName), SPID);
            }

            // Writing Debug logs
            if (Program.Settings.UserSettings.DumpDebugData)
                DumpDebugLogs();
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
                    npcs.Where(pc=> Program.Settings.UserSettings.ModsToPatch.Contains(pc.ModKey)).ForEach(n =>
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
                    else if (!OutfitsWithNPC[outfit.EditorID].Any() || OutfitsWithNPC[outfit.EditorID].Count() >= outfitNpcCount)
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
                .Where(x => Program.Settings.UserSettings.ModsToPatch.Contains(x.FormKey.ModKey))
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
            // For each armor mod creating armor sets
            var modlists = State.LoadOrder.PriorityOrder
                .Where(x => (Program.Settings.UserSettings.ArmorMods.ContainsKey(x.ModKey.FileName)
                    && x.Mod.Armors.Count > 0)
                    && !(x.Mod.Outfits != null
                    && x.Mod.Outfits.Any(o => o.EditorID.EndsWith(Settings.PatcherSettings.OutfitSuffix)
                    && o.EditorID.StartsWith(Settings.PatcherSettings.OutfitPrefix))));

            var totalSets = 0;
            var loadOrderList = new List<string>();

            for (int l = 0; l < modlists.Count(); l++)
            {
                var modLoadOrder =  "LO-"+l;
                var mod = modlists.ElementAt(l).Mod;
                List<IArmorGetter> bodies = new();
                List<TArmor> others = new();
                List<TArmor> jewelries = new();
                loadOrderList.Add(mod.ModKey.FileName + " = " + modLoadOrder);

                var modsCategories = Program.Settings.UserSettings.ArmorMods[mod.ModKey.FileName].Distinct().ToList();
                modsCategories.Remove("Generic");

                mod.Armors
                    .Where(x => ArmorUtils.IsValidArmor(x)
                        && x.Keywords != null && x.Armature != null && x.Armature.Any())
                    .ForEach(armor =>
                    {
                        Patch = FileUtils.GetIncrementedMod(Patch);
                        //if (ArmorUtils.IsEligibleForMeshMapping(armor)) AddMissingGenderMeshes(armor, true);
                        if (ArmorUtils.IsBodyArmor(armor)) { bodies.Add(armor); }
                        else
                        {
                            var mats = ArmorUtils.GetMaterial(armor);
                            if (mats.Count() > 1 && mats.Contains("Unknown"))
                                mats.Remove("Unknown");
                            
                            mats.Concat(modsCategories).Distinct()
                            .ForEach(m=>{
                                TArmor ar = new(armor, m);
                                others.Add(ar);
                                if (ArmorUtils.IsJewelry(armor))
                                    jewelries.Add(ar);
                            });
                            
                        }
                    });

                int bodyCount = bodies.Count;
                var tt = 0;

                var commanName = 0;
                if (bodyCount > 5 && Program.Settings.UserSettings.ArmorMods[mod.ModKey.FileName].Contains("Generic"))                
                    commanName = HelperUtils.GetCommonItems(others.Select(x => HelperUtils.SplitString(x.Name)).ToList())
                               .Where(x => !x.IsNullOrEmpty()).Count();
                

                var weapons = mod.Weapons.EmptyIfNull().Select(w => new TWeapon(w)).ToHashSet();
                Dictionary<TArmorType, Dictionary<string, List<TArmor>>> armorGroups = new();
                others.GroupBy(x => x.Type).ToDictionary(x => x.Key, x => x.Select(a => a)).ForEach(x =>
                {
                    Dictionary<string, List<TArmor>> d1 = new();
                    x.Value.ForEach(a => d1.GetOrAdd(a.Material).Add(a));
                    armorGroups.Add(x.Key, d1);
                });

                for (int i = 0; i < bodyCount; i++)
                {
                    // Creating armor sets and LLs
                    var body = bodies.ElementAt(i);
                    var fullname = ArmorUtils.GetFullName(body);
                    var bMats = ArmorUtils.GetMaterial(body).Concat(modsCategories).Distinct().ToList();
                    if (bMats.Contains("Unknown") && bMats.Count() > 1)
                        bMats.Remove("Unknown");
                    if (!bMats.Any() || bMats.Count() > 1)
                        Console.Write("");

                    bMats.ForEach(bMat => {
                        var bArmor = new TArmor(body, bMat);
                        TArmorSet armorSet = new(bArmor, bMat);
                        var jwels = jewelries.Where(z => HelperUtils.GetMatchingWordCount(bArmor.Name, z.Name, false) - commanName > 0);
                        List<TArmor> armors = armorGroups.GetOrAdd(armorSet.Type)
                            .GetOrDefault(bMat).EmptyIfNull()
                            .Union(jwels).ToList();

                        armorSet.LoadOrder = modLoadOrder;
                        armorSet.CreateMatchingSetFrom(armors, bodyCount == 1, commanName);

                        //Distributing weapons as well
                        if (Program.Settings.UserSettings.DistributeWeapons && weapons.Any())
                            armorSet.CreateMatchingSetFrom(weapons, bodyCount);

                        //if (armorSet.Armors.Count()==1)
                        //    Console.Write(bMats);

                        // Checking for Boots
                        if (!armorSet.Armors.Where(x => x.BodySlots.Contains(TBodySlot.Feet)).Any())
                        {
                            var type = armorSet.Type;
                            var feets = others.Where(x => x.BodySlots.Contains(TBodySlot.Feet));
                            if (feets.Any())
                                armorSet.AddArmor(feets.OrderBy(i => Random.Next()).First());
                        }

                        // Creating Leveled List
                        Patch = armorSet.CreateLeveledList(Patch);

                        // Add using materials
                        AddArmorSetToGroup(bMat, armorSet);
                        if (tt++ > 0 && tt % 100 == 0)
                            Logger.InfoFormat("Created {0} armor-set for: {1}", tt, mod.ModKey.FileName);
                    });                                        
                }
                Logger.InfoFormat("Created {0} armor-set for: {1}", tt, mod.ModKey.FileName);
                totalSets += tt;
            }
            Logger.InfoFormat("Created {0} matching armor sets from armor mods...\n", totalSets);

            File.WriteAllLines(LoadOrderFile, loadOrderList);
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
                        GrouppedArmorSets.GetOrAdd("Citizen", () => new TArmorGroup("Citizen")).AddArmorSets(temp.Armorsets);
                    }
                    else
                    {
                        GrouppedArmorSets.GetOrAdd("Bandit", () => new TArmorGroup("Bandit")).AddArmorSets(t.Value);
                        GrouppedArmorSets.GetOrAdd("Warrior", () => new TArmorGroup("Warrior")).AddArmorSets(t.Value);
                    }
                }
            }
            var bandit = GrouppedArmorSets.GetOrAdd("Bandit", () => new TArmorGroup("Bandit"));
            bandit.AddArmorSets(GrouppedArmorSets.GetOrAdd("Warrior", () => new TArmorGroup("Warrior")).Armorsets);
            bandit.AddArmorSets(GrouppedArmorSets.GetOrAdd("Knight", () => new TArmorGroup("Knight")).Armorsets);

            GrouppedArmorSets.ForEach(rec =>
            {
                Patch = FileUtils.GetIncrementedMod(Patch);
                rec.Value.CreateOutfits(Patch);
                Logger.DebugFormat("Created new outfit record for: " + rec.Key);
            });
        }

        private void ResolveOutfitOverrides()
        {
            if (!Program.Settings.UserSettings.ResolveOutfitConflicts) return;
            Logger.InfoFormat("\nResolving outfit conflicts with armor mods...");

            var outfitContext = State.LoadOrder.PriorityOrder.Outfit()
               .WinningContextOverrides();
            foreach (var outfit in State.LoadOrder.PriorityOrder
                .Where(x=>Program.Settings.UserSettings.ArmorMods.ContainsKey(x.ModKey.FileName.String))
                .WinningOverrides<IOutfitGetter>())
            {
                var winningOtfts = outfitContext.Where(c => c.Record.FormKey == outfit.FormKey).EmptyIfNull();
                if (winningOtfts.Any())
                {
                    List<IItemGetter> oLLs = new();
                    var winningOtft = winningOtfts.First().Record;

                    // Merging outfit's lvls from the armor mods together
                    var context = Program.Settings.Cache.ResolveAllContexts<IOutfit, IOutfitGetter>(winningOtft.FormKey).ToList();
                    var overridenOtfts = context.Where(c => Program.Settings.UserSettings.ArmorMods.ContainsKey(c.ModKey.FileName.String)).ToList();
                    var lastNonModOutfit = context.Where(c => !Program.Settings.UserSettings.ArmorMods.ContainsKey(c.ModKey.FileName.String)).ToList();

                    if (overridenOtfts.Count()>0 && lastNonModOutfit.Count() > 1)
                    {
                        // Reverting Overriden outfit by armor mods added in the patcher
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
            Logger.InfoFormat("Merging unassigned outfits with the new outfits...");
            var cache = State.LoadOrder.ToMutableLinkCache(Patch);
            var outfits = State.LoadOrder.PriorityOrder
                .Where(l => !l.ModKey.FileName.String.StartsWith(Settings.PatcherSettings.PatcherPrefix))
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

                // Merging outfits created by patcher in previous run
                if (otft.EditorID.EndsWith(Settings.PatcherSettings.OutfitSuffix)
                    && otft.EditorID.StartsWith(Settings.PatcherSettings.OutfitPrefix))
                {
                    var tokens = Regex.Replace(otft.EditorID, Settings.PatcherSettings.OutfitSuffix + "|" + Settings.PatcherSettings.OutfitPrefix, "").Split('_');
                    categories.Add(tokens[0]);
                    gender = tokens[1].ToEnum<TGender>();
                    types.Add(tokens[2].ToEnum<TArmorType>());
                }
                //else // Merging outfits which are not assigned to any NPC
                //{
                //    categories = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, otft.EditorID).ToList();
                //    types = OutfitUtils.GetOutfitArmorType(otft.EditorID);
                //    Logger.DebugFormat("Unused outfit: [{0}][{1}]=>[{2}]", otft.FormKey, otft.EditorID, string.Join(", ", categories));
                //}

                foreach (var category in categories) {
                    //Adding missing gender meshes
                    //Patch = FileUtils.GetIncrementedMod(Patch);
                    //OutfitUtils.GetArmorList(State.LinkCache, otft).ForEach(a => AddMissingGenderMeshes(a, true));
                    
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

        private void MergeExistingOutfits()
        {
            Logger.InfoFormat("Merging previously created outfits...");
            var cache = State.LoadOrder.ToMutableLinkCache(Patch);
            var outfits = State.LoadOrder.PriorityOrder
                .Where(l => !Program.Settings.Patches.Contains(l.Mod))
                .WinningOverrides<IOutfitGetter>()
                .Where(o => !Program.Settings.UserSettings.ModsToSkip.Contains(o.FormKey.ModKey)
                    && o.EditorID.EndsWith(Settings.PatcherSettings.OutfitSuffix)
                    && o.EditorID.StartsWith(Settings.PatcherSettings.OutfitPrefix));

            foreach (var otft in outfits)
            {
                // Merging outfits created by patcher in previous run
                var tokens = Regex.Replace(otft.EditorID, Settings.PatcherSettings.OutfitSuffix + "|" + Settings.PatcherSettings.OutfitPrefix, "").Split('_');
                var category = tokens[0];
                var gender = tokens[1].ToEnum<TGender>();
                var type = tokens[2].ToEnum<TArmorType>();

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

        public void CategorizeNPCs()
        {            
            Logger.InfoFormat("\nCategorizing NPCs...");
            Dictionary <string, HashSet<string>> npcArmorTypes = new();
            Dictionary<FormKey, HashSet<string>> IdentifierGroups = new();
            Dictionary<string, Dictionary<string, Dictionary<FormKey, string>>> map = new();

            var keywordFile = Path.Combine(Program.Settings.State.DataFolderPath, Settings.PatcherSettings.KeywordFile);
            var SPID1 = File.ReadAllLines(keywordFile).ToList();

            var order = Program.Settings.State.LoadOrder.PriorityOrder
                .Where(x => Program.Settings.UserSettings.ModsToPatch.Contains(x.ModKey));

            //Class Based
            Logger.InfoFormat("Parsing NPC Classes...");
            order.WinningOverrides<IClassGetter>()
                .Where(c => NPCUtils.IsValidClass(c))
                .ForEach(c =>
                {
                    var id = Regex.Replace(c.EditorID, "class|combat", "", RegexOptions.IgnoreCase);
                    var groups = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, id);
                    if (!groups.Any()) groups.Add("Unknown");
                    IdentifierGroups.GetOrAdd(c.FormKey).UnionWith(groups);
                    groups.ForEach(group =>
                    {
                        HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitTypeRegex, group)
                        .ForEach(t => npcArmorTypes.GetOrAdd(t).Add(group));
                        var types = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.ArmorTypeRegex, c.EditorID).ToList();
                        if (!types.Any()) types.Add("Unknown");
                        types.ForEach(t => map.GetOrAdd(group).GetOrAdd(t).Add(c.FormKey, c.EditorID));
                    });
                });

            //Outfit Based
            Logger.InfoFormat("Parsing NPC Outfits...");
            order.WinningOverrides<INpcGetter>()
            .Where(n => NPCUtils.IsValidActorType(n));
            order.WinningOverrides<IOutfitGetter>()
                .Where(o => OutfitUtils.IsValidOutfit(o))
                .ForEach(c =>
                {
                    var groups = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, c.EditorID);
                    if (!groups.Any()) groups.Add("Unknown");
                    IdentifierGroups.GetOrAdd(c.FormKey).UnionWith(groups);
                    groups.ForEach(group =>
                    {
                        HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitTypeRegex, group)
                        .ForEach(t => npcArmorTypes.GetOrAdd(t).Add(group));
                        var types = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.ArmorTypeRegex, c.EditorID);
                        if (!types.Any()) types.Add("Unknown");
                        types.ForEach(t => map.GetOrAdd(group).GetOrAdd(t).Add(c.FormKey, c.EditorID));
                    });
                });

            // Race Based
            Logger.InfoFormat("Parsing NPC Races...");
            order.WinningOverrides<IRaceGetter>()
                .Where(r => NPCUtils.IsValidRace(r))
                .ForEach(c =>
                {
                    var groups = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, c.EditorID);
                    //if (!groups.Any()) groups.Add("Unknown");
                    IdentifierGroups.GetOrAdd(c.FormKey).UnionWith(groups);
                    groups.ForEach(group =>
                    {
                        var types = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.ArmorTypeRegex, c.EditorID).ToList();
                            //if (!types.Any()) types.Add("Unknown");
                            types.ForEach(t => map.GetOrAdd(group).GetOrAdd(t).Add(c.FormKey, c.EditorID));
                    });
                });

            // Faction Based Distribution
            Logger.InfoFormat("Parsing NPC Factions...");
            order.WinningOverrides<IFactionGetter>()
            .Where(r => NPCUtils.IsValidFaction(r))
            .ForEach(c =>
            {
                var groups = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, c.EditorID);
                if (!groups.Any()) groups.Add("Unknown");
                IdentifierGroups.GetOrAdd(c.FormKey).UnionWith(groups);
                groups.ForEach(group =>
                {
                    HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitTypeRegex, group)
                        .ForEach(t => npcArmorTypes.GetOrAdd(t).Add(group));
                    var types = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.ArmorTypeRegex, c.EditorID).ToList();
                    if (!types.Any()) types.Add("Unknown");
                    types.ForEach(t => map.GetOrAdd(group).GetOrAdd(t).Add(c.FormKey, c.EditorID));
                });
            });

            // Creating keywords
            map.OrderBy(x => x.Key).ForEach(r=> {
                string name = "AADPType"+r.Key;
                var keyword = "AADPTypeValid";
                if (r.Key.StartsWith("Guards")) keyword = "AADPTypeGuards";
                if (r.Key.StartsWith("Child")) keyword = "AADPTypeChildren";

                var filters = r.Value.SelectMany(v=>v.Value).Distinct().Select(v=> "0x"+v.Key.ToString().Replace(":","~").Replace("~Skyrim.esm", ""));
                string line = "Keyword = " + name + "|" + keyword + "|" + string.Join(",", filters);
                SPID1.Add(line);

                //var filters1 = r.Value.SelectMany(v=>v.Value).Distinct().Select(v=> v.Value);                
                //string line1 = ";Keyword = " + name + "|"+keyword+"|" + string.Join(",", filters1);                
                //SPID1.Add(line1);

                // Putting data in armor sets
                GrouppedArmorSets.GetOrAdd(r.Key, () => new TArmorGroup(r.Key)).Identifiers = r.Value;
            });

            SPID1.Add("\n; NPC Armor Type ------------");
            npcArmorTypes.ForEach(t =>
            {
                var filters = t.Value.Select(cls => "AADPType" + cls);
                var name = "AADPTypeArmor" + t.Key;
                string line = "Keyword = " + name + "|" + string.Join(",", filters);
                SPID1.Add(line);
            });

            //SPID1.Add("\n\n\n; Unknown ------------");
            //SPID1.Add(string.Join("\n", map.GetOrAdd("Unknown").SelectMany(x=>x.Value.Values).Distinct().OrderBy(x=>x)));
            //File.WriteAllLines(keywordFile, SPID1, Encoding.UTF8);
            PatchNPCs(IdentifierGroups);
        }

        private void CreateSPID() {
            Logger.InfoFormat("Creating SPID ini file for outfits...");
            var percentage = 100 - Program.Settings.UserSettings.DefaultOutfitPercentage;
            var cache = State.LoadOrder.ToMutableLinkCache();

            foreach (var pair in GrouppedArmorSets.OrderBy(x => x.Key))
            {
                GrouppedArmorSets[pair.Key].GenderOutfit.Where(g => g.Value != null && g.Value.Any())
                    .ForEach(x =>
                    {
                        var gender = x.Key.Equals(TGender.Male) ? "M" : x.Key.Equals(TGender.Common) ? "NONE" : "F";
                        foreach (var armorType in x.Value.Keys)
                        {
                            var outfit = x.Value[armorType];
                            var keywords = "AADPType" + pair.Key + "+AADPType" + armorType;

                            var armorSets = OutfitUtils.GetArmorList(cache, cache.Resolve<IOutfitGetter>(outfit))
                                .Where(x => ArmorUtils.IsBodyArmor(x))
                                .Count();
                            string line1 = string.Format("\n;Outfits for {0}{1}/{2} [{3} armor sets]", pair.Key,
                                armorType, gender.Replace("NONE", "M+F").Replace("/U", ""), armorSets);
                            string line2 = String.Format("Outfit = 0x{0}~{1}|{2}|{3}|NONE|{4}|NONE|{5}",
                                outfit.ID.ToString("X"), outfit.ModKey.FileName, keywords, "NONE", gender, percentage);
                            if (Program.Settings.UserSettings.SkipGuardDistribution && pair.Key.StartsWith("Guards")) line2 = ";"+line2;
                            if (armorSets<1) line2 = ";"+line2;
                            if (Program.Settings.UserSettings.FilterUniqueNPC) SPID.Add(line2.Replace("/U|NONE", "|NONE"));
                            SPID.Add(line1);
                            SPID.Add(line2);
                        }
                        SPID.Add(Environment.NewLine);
                    });
            }

        }

        private void PatchNPCs(Dictionary<FormKey, HashSet<string>> IdentifierGroups) {
            var state = Program.Settings.State;
            var cache = state.LoadOrder.ToMutableLinkCache();
            var npcs = state.LoadOrder.PriorityOrder
                .WinningOverrides<INpcGetter>()
                .Where(x => Program.Settings.UserSettings.ModsToPatch.Contains(x.FormKey.ModKey)
                    && !Program.Settings.UserSettings.NPCToSkip.Contains(x.FormKey)
                    && x.DefaultOutfit != null && NPCUtils.IsValidActorType(x));

            Settings.PatcherSettings.OutfitRegex.Keys
                .Concat(Settings.PatcherSettings.OutfitTypeRegex.Keys).Distinct()
                .ForEach(a => NpcKeywords.GetOrAdd(a, ()=> new(Patch, a.ToString())));

            int count = 0;
            var totalNPCs = npcs.Count();
            foreach (var npc in npcs)
            {
                Patch = FileUtils.GetIncrementedMod(Patch);
                var identifier = string.Empty;
                var npcType = string.Empty;
                var npcTypes = new HashSet<string>();
                var name = NPCUtils.GetName(npc);

                // Faction Based                
                npc.Factions.EmptyIfNull().ForEach(f => {
                    npcTypes.UnionWith(IdentifierGroups.GetOrDefault(f.Faction.FormKey).EmptyIfNull());
                });

                //Class based
                if (state.LinkCache.TryResolve<IClassGetter>(npc.Class.FormKey, out var cls)
                    && NPCUtils.IsValidClass(cls))
                    npcTypes.UnionWith(IdentifierGroups.GetOrDefault(cls.FormKey).EmptyIfNull());

                // Outfit of NPC
                if (state.LinkCache.TryResolve<IOutfitGetter>(npc.DefaultOutfit.FormKey, out var otft)
                    && OutfitUtils.IsValidOutfit(otft))
                    npcTypes.UnionWith(IdentifierGroups.GetOrDefault(otft.FormKey).EmptyIfNull());

                //Race based
                if (state.LinkCache.TryResolve<IRaceGetter>(npc.Race.FormKey, out var race)
                    && NPCUtils.IsValidRace(race))
                    npcTypes.UnionWith(IdentifierGroups.GetOrDefault(race.FormKey).EmptyIfNull());

                // Name/editor id based
                if (NPCUtils.IsValidNPCName(npc)) {
                    var t = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, name);
                    npcTypes.UnionWith(t);
                }

                // Getting cloth or armor type
                HashSet<string> list = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitTypeRegex, string.Join(" ", npcTypes))
                        .ToHashSet();

                if (!list.Any())
                    list = (otft != null && OutfitArmorTypesRev.ContainsKey(otft.EditorID)
                    ? OutfitArmorTypesRev[otft.EditorID] : new()).Select(x => x.ToString()).ToHashSet();

                // Armor types based on skills
                if (list.Count() > 1) {
                    var skillset = npc.PlayerSkills.SkillValues.OrderByDescending(x => x.Value)
                        .ToDictionary(x=> x.Key.ToString(), x=> x.Value).Keys.Take(3);

                    skillset=HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.SkillBasedArmors, string.Join(" ", skillset));
                    var list1 = skillset.Intersect(list).FirstOrDefault();
                    if (!list1.IsNullOrEmpty()) {
                        list.Clear();
                        list.Add(list1);
                    }
                }

                if (!npcTypes.Any() && list.Any() && list.Contains("Wizard"))
                    npcTypes.Add("Mage");

                if (!npcTypes.Any() || !list.Any())
                {
                    var t = !npcTypes.Any() ? "Type" : !list.Any() ? "Outfit" : "Outfit & Type";
                    Logger.DebugFormat("NPC {2} Not Found: [{0}]=>[{1}]", npc.FormKey, identifier, t);
                    continue;
                }

                // Fop Debug
                var npcKey = AllNPCs.GetOrAdd(npc.FormKey.ToString() + "::" + name);
                npcKey.Add(identifier);

                //Adding Keywords for NPCs
                list.Union(npcTypes).Where(x=>!x.Equals("Unknown"))                    
                    .Select(a=> NpcKeywords.GetOrAdd(a, () => new(Patch, a.ToString())))
                .ForEach(k => {
                    //var n = Patch.Npcs.GetOrAddAsOverride(npc);
                    //if (n.Keywords == null) n.Keywords = new();
                    //n.Keywords.Add(k.FormKey.AsLink<IKeywordGetter>());
                    //KeywordedNPCs.GetOrAdd(k.Keyword).Add(identifier);

                    // For Debug
                    npcKey.Add(k.Keyword);
                });

                if (++count > 0 && count % 500 == 0)
                    Logger.InfoFormat("Processed {0}/{1} NPCs...", count.ToString().PadLeft(totalNPCs.ToString().Length), totalNPCs);
            }
            Logger.InfoFormat("Patched total {0} NPCs...", count);
        }

        private void CreateNPCKeywords(ISkyrimMod Patch)
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
            var state = Program.Settings.State;
            var cache = state.LoadOrder.ToMutableLinkCache();
            var npcs = state.LoadOrder.PriorityOrder
                .WinningOverrides<INpcGetter>()
                .Where(x => Program.Settings.UserSettings.ModsToPatch.Contains(x.FormKey.ModKey)
                    && !Program.Settings.UserSettings.NPCToSkip.Contains(x.FormKey)
                    && x.DefaultOutfit != null && NPCUtils.IsValidActorType(x));
            var totalNPCs = npcs.Count();
            foreach (var npc in npcs)
            {
                Patch = FileUtils.GetIncrementedMod(Patch);

                var identifier = string.Empty;
                var npcType = string.Empty;

                // Faction Based                
                    npc.Factions.EmptyIfNull().ForEach(f => {
                        if (state.LinkCache.TryResolve<IFactionGetter>(f.Faction.FormKey, out var fac)
                            && NPCUtils.IsValidFaction(fac))
                            identifier += " " + fac.EditorID;
                    });


                //Class based
                if (state.LinkCache.TryResolve<IClassGetter>(npc.Class.FormKey, out var cls)
                    && NPCUtils.IsValidClass(cls))
                    identifier += " " + Regex.Replace(cls.EditorID, "class|combat", "", RegexOptions.IgnoreCase);

                // Outfit of NPC
                if (state.LinkCache.TryResolve<IOutfitGetter>(npc.DefaultOutfit.FormKey, out var otft)
                    && OutfitUtils.IsValidOutfit(otft))
                {
                    identifier += " " + otft.EditorID;
                    OutfitArmorTypesRev.GetOrAdd(otft.EditorID).UnionWith(OutfitUtils.GetOutfitArmorType(cache, otft));
                }

                //Race based
                if (state.LinkCache.TryResolve<IRaceGetter>(npc.Race.FormKey, out var race)
                    && NPCUtils.IsValidRace(race))
                    identifier += " " + race.EditorID;

                // Name/editor id based
                if (NPCUtils.IsValidNPCName(npc))
                    identifier += " " + NPCUtils.GetName(npc);

                // Getting cloth or armor type
                HashSet<string> list = (otft != null && OutfitArmorTypesRev.ContainsKey(otft.EditorID)
                    ? OutfitArmorTypesRev[otft.EditorID] : new()).Select(x=>x.ToString()).ToHashSet();

                if (!list.Any())
                    list = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.ArmorTypeRegex, identifier)                        
                        .ToHashSet();

                var npcTypes = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, identifier).ToList();
                if (!npcTypes.Any() && list.Any() && list.Contains("Wizard"))                
                    npcTypes.Add("Mage");

                if (!npcTypes.Any() || !list.Any())
                {
                    var t = !npcTypes.Any() ? "Type" : !list.Any() ? "Outfit" : "Outfit & Type";
                    Logger.DebugFormat("NPC {2} Not Found: [{0}]=>[{1}]", npc.FormKey, identifier, t);
                    continue;
                }

                //Adding Keywords for NPCs
                var skippable = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.SkippableRegex, identifier);
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
                    Logger.InfoFormat("Processed {0}/{1} NPCs...", count.ToString().PadLeft(totalNPCs.ToString().Length), totalNPCs);
            }
            Logger.InfoFormat("Patched total {0} NPCs...", count);
        }

        private void AddGroppedArmorSetToSPIDFile()
        {
            Logger.InfoFormat("Creating SPID ini file...");

            // Distributing Outfits
            var percentage = 100 - Program.Settings.UserSettings.DefaultOutfitPercentage;
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

                            if (pair.Key.StartsWith("Child"))
                                keywords = keywords.Replace("," + skippableList.GetOrDefault("SkipChild"), "");
                            if (pair.Key.StartsWith("Guards"))
                                keywords = keywords.Replace("," + skippableList.GetOrDefault("SkipGuards"), "");

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
                            if (Program.Settings.UserSettings.SkipGuardDistribution && pair.Key.StartsWith("Guards")) line2 = ";" + line2;
                            if (Program.Settings.UserSettings.FilterUniqueNPC) SPID.Add(line2.Replace("/U|NONE", "|NONE"));

                            SPID.Add(line1);
                            SPID.Add(line2);        
                        }
                        SPID.Add(Environment.NewLine);
                    });
            }
        }

        private void DumpDebugLogs()
        {
            // Writing Debug logs
            Logger.InfoFormat("Writing Debug logs...");
            
            var cache = State.LoadOrder.ToMutableLinkCache();
            //State.LoadOrder.PriorityOrder
            //    .WinningOverrides<INpcGetter>()
            //    .Where(n => NPCUtils.IsValidActorType(n))
            //    .ForEach(n =>
            //    {
            //        var name = NPCUtils.GetName(n);
            //        var npc = AllNPCs.GetOrAdd(n.FormKey.ToString() + "::" + name);
            //        n.Keywords.EmptyIfNull().ForEach(k => {
            //            var cat = cache.Resolve<IKeywordGetter>(k.FormKey).EditorID;
            //            npc.Add(cat);
            //            if (GrouppedArmorSets.ContainsKey(cat)) GrouppedArmorSets[cat].NPCs.Add(n.FormKey, name);
            //        });
            //    });

            Dictionary<string, object> all = new();
            //all.Add("Categories", GrouppedArmorSets);
            all.Add("AllNPCs", AllNPCs);

            var outputfile = Path.Combine(Settings.LogsDirectory, "OutfitManager.json");
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

        private void FilterGaurdOutfits()
        {
            if (!Program.Settings.UserSettings.SkipGuardDistribution) return;

            Dictionary<FormKey, string> list = new();
            //GrouppedArmorSets.Where(x => x.Key.EndsWith("Armor") || x.Key.StartsWith("Guards") || x.Key.EndsWith("Race"))
            GrouppedArmorSets.Where(x => x.Key.StartsWith("Guards"))
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

        //private void CategorizeArmorsForMissingMeshes()
        //{
        //    Logger.InfoFormat("\nCategorizing armors based on body slots...");
        //    foreach (IArmorGetter armor in State.LoadOrder.PriorityOrder
        //        .WinningOverrides<IArmorGetter>()
        //        .Where(x => ArmorUtils.IsEligibleForMeshMapping(x)))
        //    {
        //        var armorType = ArmorUtils.GetArmorType(armor);
        //        string material = ArmorUtils.GetMaterial(armor);

        //        armor.Armature.ForEach(ar =>{
        //            if (ar.TryResolve<IArmorAddonGetter>(State.LinkCache, out var addon))
        //            {
        //                // Adding Armor sets
        //                var slots = ArmorUtils.GetBodySlots(addon);
        //                slots.Select(x => x).ForEach(slot =>
        //                {
        //                    ArmorsWithSlot.GetOrAdd(armorType).GetOrAdd(slot).TryAdd(armor.FormKey, armor.Value);
        //                });

        //                // Adding Armor meshes male                        
        //                if (addon.WorldModel != null && addon.WorldModel.Male != null)
        //                {
        //                    if (File.Exists(Path.Combine(State.DataFolderPath, "Meshes", addon.WorldModel.Male.File)))
        //                    {
        //                        // Getting body armor related like data material, editorID, armor type etc
        //                        var armorMat = MaleArmorMeshes.GetOrAdd(material);
        //                        slots.ForEach(flag =>
        //                        {
        //                            var slot = flag.ToString();
        //                            if (!armorMat.ContainsKey(slot))
        //                                armorMat.TryAdd(slot, addon.WorldModel.Male.File);
        //                        });
        //                    }
        //                }

        //                // Adding Armor meshes female
        //                if (addon.WorldModel != null && addon.WorldModel.Female != null)
        //                {
        //                    if (File.Exists(Path.Combine(State.DataFolderPath, "Meshes", addon.WorldModel.Female.File)))
        //                    {
        //                        // Getting body armor related like data material, editorID, armor type etc
        //                        var armorMat = FemaleArmorMeshes.GetOrAdd(material);
        //                        slots.ForEach(flag =>
        //                        {
        //                            var slot = flag.ToString();
        //                            if (!armorMat.ContainsKey(slot))
        //                                armorMat.TryAdd(slot, addon.WorldModel.Female.File);
        //                        });
        //                    }
        //                }                        
        //            }
        //        });                
        //    }

        //    // Adding Random Mesh Map data
        //    Dictionary<string, HashSet<string>> map = new();
        //    FemaleArmorMeshes.ForEach(s => s.Value.ForEach(v => {
        //        if (!map.ContainsKey(v.Key)) map.GetOrAdd(v.Key).Add(v.Value);
        //    }));
        //    RandomArmorMeshes["Female"] = map;
        //    map.Clear();

        //    MaleArmorMeshes.ForEach(s => s.Value.ForEach(v => {
        //        if (!map.ContainsKey(v.Key)) map.GetOrAdd(v.Key).Add(v.Value);
        //    }));
        //    RandomArmorMeshes["Male"] = map;
        //}

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
            string material = ArmorUtils.GetMaterial(armor).First();
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
