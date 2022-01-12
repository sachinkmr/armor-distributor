using log4net;
using Mutagen.Bethesda;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using ArmorDistributor.Config;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ArmorDistributor.Armor;

namespace ArmorDistributor.Utils
{
    public class OutfitUtils
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(OutfitUtils));
        private static Dictionary<string, FormKey> lls = new();


        public static bool IsValidOutfit(IOutfitGetter outfit)
        {
            return Regex.Match(outfit.EditorID, Settings.PatcherSettings.ValidOutfitRegex, RegexOptions.IgnoreCase).Success
                    || !Regex.Match(outfit.EditorID, Settings.PatcherSettings.InvalidOutfitRegex, RegexOptions.IgnoreCase).Success;
        }

        public static bool IsValidOutfit(string outfit)
        {
            return Regex.Match(outfit, Settings.PatcherSettings.ValidOutfitRegex, RegexOptions.IgnoreCase).Success
                    || !Regex.Match(outfit, Settings.PatcherSettings.InvalidOutfitRegex, RegexOptions.IgnoreCase).Success;
        }


        public static LeveledItem CreateLeveledList(ISkyrimMod PatchMod, IEnumerable<IItemGetter> items, string editorID, short level, LeveledItem.Flag flag)
        {
            if (lls.ContainsKey(editorID)) {
                Logger.DebugFormat("Record already Exists [{0}]. mod has/have same editor ID ", editorID);
                if (PatchMod.ToImmutableLinkCache().TryResolve<LeveledItem>(lls[editorID], out var ll))
                    if (ll.ContainedFormLinks.Select(x => x.FormKey).Except(items.Select(x => x.FormKey)).Count() == 0)
                    return ll;
                else
                    editorID += "_dup";
            }
            PatchMod = FileUtils.GetIncrementedMod(PatchMod);
            LeveledItem lvli = PatchMod.LeveledItems.AddNew(editorID);
            lvli.Entries = new ExtendedList<LeveledItemEntry>();
            lvli.Flags = flag;

            AddItemsToLeveledList(PatchMod, lvli, items, 1);
            lls.TryAdd(editorID, lvli.FormKey);
            return lvli;
        }

        public static LeveledNpc CreateLeveledList(ISkyrimMod PatchMod, IEnumerable<ILeveledNpcGetter> items, string editorID, short level, LeveledNpc.Flag flag)
        {
            if (lls.ContainsKey(editorID))
            {
                Logger.DebugFormat("Record already Exists [{0}]. mod has/have same editor ID ", editorID);
                if (PatchMod.ToImmutableLinkCache().TryResolve<LeveledNpc>(lls[editorID], out var ll))
                    if (ll.ContainedFormLinks.Select(x => x.FormKey).Except(items.Select(x => x.FormKey)).Count() == 0)
                        return ll;
                    else
                        editorID += "_dup";
            }
            PatchMod = FileUtils.GetIncrementedMod(PatchMod);
            LeveledNpc lvli = PatchMod.LeveledNpcs.AddNew(editorID);
            lvli.Entries = new ();
            lvli.Flags = flag;

            AddItemsToLeveledList(PatchMod, lvli, items, 1);
            lls.TryAdd(editorID, lvli.FormKey);
            return lvli;
        }

        internal static Outfit CreateOutfit(ISkyrimMod? PatchedMod, string eid, IEnumerable<FormLink<IItemGetter>> set)
        {
            LeveledItem mLL = CreateLeveledList(PatchedMod, set, "mLL_" + eid, 1, Program.Settings.LeveledListFlag);
            Outfit newOutfit = PatchedMod.Outfits.AddNew(Settings.PatcherSettings.OutfitPrefix + eid + Settings.PatcherSettings.OutfitSuffix);
            newOutfit.Items = new(mLL.AsLink().AsEnumerable());
            return newOutfit;
        }

        public static LeveledItem CreateLeveledList(ISkyrimMod PatchMod, IEnumerable<IFormLink<IItemGetter>> items, string editorID, short level, LeveledItem.Flag flag)
        {
            if (lls.ContainsKey(editorID))
            {
                Logger.DebugFormat("Record already Exists [{0}]. mod has/have same editor ID ", editorID);
                if(PatchMod.ToImmutableLinkCache().TryResolve<LeveledItem>(lls[editorID], out var ll))
                    if (ll.ContainedFormLinks.Select(x => x.FormKey).Except(items.Select(x => x.FormKey)).Count() == 0)
                        return ll;
                else
                    editorID += "_dup";
            }

            PatchMod = FileUtils.GetIncrementedMod(PatchMod);
            LeveledItem lvli = PatchMod.LeveledItems.AddNew(editorID);
            lvli.Entries = new ExtendedList<LeveledItemEntry>();
            lvli.Flags = flag;

            AddItemsToLeveledList(PatchMod, lvli, items, 1);
            lls.TryAdd(editorID, lvli.FormKey);
            return lvli;
        }

        internal static void FixLeveledList(ILeveledItemGetter lvli, HashSet<FormKey> set, Dictionary<FormKey, List<FormKey>> parentChildLL, ILinkCache mCache)
        {
            set.Add(lvli.FormKey);
            if (lvli.Entries==null && !lvli.Entries.Any()) return;
            var itms = lvli.Entries.Select(i => mCache.Resolve<IItemGetter>(i.Data.Reference.FormKey));
            foreach (var itm in itms) {
                if (set.Contains(lvli.FormKey))
                {
                    parentChildLL.GetOrAdd(lvli.FormKey).Add(itm.FormKey);
                }
                else { 
                    if (itm is ILeveledItemGetter) 
                        FixLeveledList((ILeveledItemGetter)itm, set, parentChildLL,mCache);
                }
            }
        }

        public static void AddItemsToLeveledList(ISkyrimMod patch, LeveledItem lvli, IEnumerable<IItemGetter> items,short level)
        {
            LeveledItem? sLL = null;
            bool hasMultiItems = items.Count() + lvli.Entries.Count > 250;
            if (!hasMultiItems) sLL = lvli;

            for (int i = 0, j = 0; i < items.Count(); i++)
            {
                if (hasMultiItems && i % 250 == 0)
                {
                    sLL = CreateLeveledList(patch, new List<IItemGetter>(), lvli.EditorID + (++j), 1, Program.Settings.LeveledListFlag);
                    AddItemToLeveledList(lvli, sLL, 1);
                }
                AddItemToLeveledList(sLL, items.ElementAtOrDefault(i), 1);
            }
        }

        public static void AddItemsToLeveledList(ISkyrimMod patch, LeveledNpc lvli, IEnumerable<ILeveledNpcGetter> items, short level)
        {
            LeveledNpc? sLL = null;
            bool hasMultiItems = items.Count() + lvli.Entries.Count > 250;
            if (!hasMultiItems) sLL = lvli;

            for (int i = 0, j = 0; i < items.Count(); i++)
            {
                if (hasMultiItems && i % 250 == 0)
                {
                    sLL = CreateLeveledList(patch, new List<ILeveledNpcGetter>(), lvli.EditorID + (++j), 1, Program.Settings.LeveledNpcFlag);
                    AddItemToLeveledList(lvli, sLL, 1);
                }
                AddItemToLeveledList(sLL, items.ElementAtOrDefault(i), 1);
            }
        }

        public static void AddItemsToLeveledList(ISkyrimMod patch, LeveledItem lvli, IEnumerable<IFormLink<IItemGetter>> items, short level)
        {
            LeveledItem? sLL = null;
            bool hasMultiItems = items.Count() + lvli.Entries.Count > 250;
            if (!hasMultiItems) sLL = lvli;

            for (int i = 0, j = 0; i < items.Count(); i++)
            {
                if (hasMultiItems && i % 250 == 0)
                {
                    sLL = CreateLeveledList(patch, new List<IItemGetter>(), lvli.EditorID + (++j), 1, Program.Settings.LeveledListFlag);
                    AddItemToLeveledList(lvli, sLL, 1);
                }
                AddItemToLeveledList(sLL, items.ElementAtOrDefault(i), 1);
            }
        }

        public static void AddItemToLeveledList(LeveledItem lvli, IItemGetter item, short level)
        {
            LeveledItemEntry entry = new LeveledItemEntry();
            LeveledItemEntryData data = new LeveledItemEntryData();
            data.Reference = item.AsLink();
            data.Level = level;
            data.Count = 1;
            entry.Data = data;
            lvli.Entries.Add(entry);
        }

        public static void AddItemToLeveledList(LeveledNpc lvli, ILeveledNpcGetter item, short level)
        {
            LeveledNpcEntry entry = new ();
            LeveledNpcEntryData data = new ();
            data.Reference = item.AsLink();
            data.Level = level;
            data.Count = 1;
            entry.Data = data;
            lvli.Entries.Add(entry);
        }

        public static void AddItemToLeveledList(LeveledItem lvli, IFormLink<IItemGetter> item, short level)
        {
            LeveledItemEntry entry = new LeveledItemEntry();
            LeveledItemEntryData data = new LeveledItemEntryData();
            data.Reference = item;
            data.Level = level;
            data.Count = 1;
            entry.Data = data;
            lvli.Entries.Add(entry);
        }

        public static Outfit CreateOutfit(ISkyrimMod patch, string eid, IEnumerable<IFormLinkGetter<IOutfitTargetGetter>> items)
        {
            patch = FileUtils.GetIncrementedMod(patch);
            Outfit otft = patch.Outfits.AddNew();
            otft.EditorID = Settings.PatcherSettings.OutfitPrefix+eid + Settings.PatcherSettings.OutfitSuffix;
            otft.Items = new(items);
            return otft;
        }

        public static Outfit CreateOutfit(ISkyrimMod patch, string eid, IFormLinkGetter<IOutfitTargetGetter> item)
        {
            patch = FileUtils.GetIncrementedMod(patch);
            Outfit otft = patch.Outfits.AddNew();
            otft.EditorID = Settings.PatcherSettings.OutfitPrefix + eid + Settings.PatcherSettings.OutfitSuffix;
            otft.Items = new();
            otft.Items.Add(item);
            return otft;
        }

        public static Outfit CreateOutfit(ISkyrimMod patchedMod, string eid, List<IItemGetter> items)
        {
            LeveledItem mLL = CreateLeveledList(patchedMod, items, "mLL_" + eid, 1, Program.Settings.LeveledListFlag);

            patchedMod = FileUtils.GetIncrementedMod(patchedMod);
            Outfit newOutfit = patchedMod.Outfits.AddNew(Settings.PatcherSettings.OutfitPrefix + eid + Settings.PatcherSettings.OutfitSuffix);
            newOutfit.Items = new(mLL.AsLink().AsEnumerable());
            return newOutfit;
        }

        public static void AddEntriesToLeveledList(ISkyrimMod patch, LeveledItem lvli, IEnumerable<LeveledItemEntry> items)
        {
            if (items.Count() < 255) items.ForEach(item => lvli.Entries.Add(item));
            else
            {
                LeveledItem? sLL = null;
                for (int i = 0; i < items.Count(); i++)
                {
                    if (i % 250 == 0)
                    {
                        sLL = CreateLeveledList(patch, new List<IItemGetter>(), lvli.EditorID + i, 1, lvli.Flags);
                        AddItemToLeveledList(lvli, sLL, 1);
                    }
                    sLL.Entries.Add(items.ElementAtOrDefault(i));
                }
            }

        }

        public static void AddEntriesToLeveledList(ISkyrimMod patch, LeveledNpc lvli, IEnumerable<LeveledNpcEntry> items)
        {
            if (items.Count() < 255) items.ForEach(item => lvli.Entries.Add(item));
            else
            {
                LeveledNpc? sLL = null;
                for (int i = 0; i < items.Count(); i++)
                {
                    if (i % 250 == 0)
                    {
                        sLL = CreateLeveledList(patch, new List<ILeveledNpcGetter>(), lvli.EditorID + i, 1, Program.Settings.LeveledNpcFlag);
                        AddItemToLeveledList(lvli, sLL, 1);
                    }
                    sLL.Entries.Add(items.ElementAtOrDefault(i));
                }
            }

        }

        public static void GetArmorList(IItemGetter ll, ICollection<IArmorGetter> armors, ConcurrentBag<FormKey> processed)
        {
            ILinkCache cache = Program.Settings.State.LinkCache;
            ll.ContainedFormLinks.ForEach(i =>
            {
                if (!processed.Contains(i.FormKey))
                {
                    processed.Add(i.FormKey);
                    if (cache.TryResolve<IArmorGetter>(i.FormKey, out var itm))
                        armors.Add(itm);
                    if (cache.TryResolve<ILeveledItemGetter>(i.FormKey, out var lv))
                        GetArmorList(lv, armors, processed);
                }
            });
        }

        public static string getLLGender(ILeveledItemGetter ll) {
            var matches = Regex.Matches(ll.EditorID, Settings.PatcherSettings.LeveledListPrefix + "[C|M|F|U]_");
            return matches.Any() ? matches.First().Value.Replace(Settings.PatcherSettings.LeveledListPrefix, "").Replace("_", "") : "U";
        }

        public static List<string> GetOutfitArmorType(IOutfitGetter o) {
            var cache = Program.Settings.Cache;
            ConcurrentBag<FormKey> ArmorsFormKey = new();
            var items = o.Items;
            try {
                return o.Items.Select(i => {
                    var item = i.Resolve<IItemGetter>(cache);
                    if (item is IArmorGetter)
                        return ArmorUtils.GetArmorType((IArmorGetter)item);
                    else
                    {
                        List<IArmorGetter> armors = new();
                        GetArmorList(item, armors, ArmorsFormKey);
                        if (!armors.Any()) return TArmorType.Unknown;
                        var armor = armors.Where(a => ArmorUtils.IsBodyArmor(a));
                        return armor.Any() ? ArmorUtils.GetArmorType(armor.First()) 
                            : ArmorUtils.GetArmorType(armors.First());
                    }
                }).Distinct().ToList();
            } catch {
                return new List<string>() { TArmorType.Unknown };
            }
        }

        public static List<IArmorGetter> GetArmorList(IOutfitGetter outfit) {
            List<IArmorGetter> armors = new();
            ConcurrentBag<FormKey> ArmorsFormKey = new();
            outfit.ContainedFormLinks.Where(x=>!x.IsNull)
                .Select(l => {
                    Program.Settings.Cache.TryResolve<IItemGetter>(l.FormKey, out var t);
                    return t;
                }).Where(x=>x!=null)
                .ForEach(i => { 
                    if (i is IArmorGetter) armors.Add((IArmorGetter)i);
                    if(i is ILeveledItem) GetArmorList(i, armors, ArmorsFormKey);
                });
            return armors.Distinct().ToList();
        }
       
        public static List<string> GetOutfitArmorType(string eid)
        {
            return GetOutfitArmorType(Program.Settings.Cache.Resolve<IOutfitGetter>(eid));            
        }

        public static List<string> GetOutfitArmorType(FormKey key)
        {
            return GetOutfitArmorType(Program.Settings.Cache.Resolve<IOutfitGetter>(key));
        }

        public static HashSet<FormKey> GetLeveledLists(IItemGetter ll) {
            HashSet<FormKey> processed = new();
            GetSubLeveledLists(ll, processed) ;
            return processed;
        }

        private static void GetSubLeveledLists(IItemGetter ll, HashSet<FormKey> LLs)
        {
            ILinkCache cache = Program.Settings.State.LinkCache;
            ll.ContainedFormLinks.ForEach(i =>
            {
                if (!LLs.Contains(i.FormKey))
                {
                    LLs.Add(i.FormKey);
                    if (cache.TryResolve<ILeveledItem>(i.FormKey, out var itm)) {
                        LLs.Add(itm.FormKey);
                        GetSubLeveledLists(itm, LLs);
                    }
                }
            });
        }

        public static List<string> GetLeveledListArmorType(ILeveledItem ll)
        {
            var cache = Program.Settings.Cache;
            ConcurrentBag<FormKey> ArmorsFormKey = new();
            try
            {
                return ll.ContainedFormLinks.Select(i => {
                    var item = i.Resolve<IItemGetter>(cache);
                    if (item is IArmorGetter)
                        return ArmorUtils.GetArmorType((IArmorGetter)item);
                    else
                    {
                        List<IArmorGetter> armors = new();
                        GetArmorList(item, armors, ArmorsFormKey);
                        if (!armors.Any()) return TArmorType.Unknown;
                        var armor = armors.Where(a => ArmorUtils.IsBodyArmor(a));
                        return armor.Any() ? ArmorUtils.GetArmorType(armor.First())
                            : ArmorUtils.GetArmorType(armors.First());
                    }
                }).Distinct().ToList();
            }
            catch
            {
                return new List<string>() { TArmorType.Unknown };
            }
        }

        public static List<string> GetLeveledListGenderType(ILeveledItem ll)
        {
            var cache = Program.Settings.Cache;
            ConcurrentBag<FormKey> ArmorsFormKey = new();
            try
            {
                return ll.ContainedFormLinks.Select(i => {
                    var item = i.Resolve<IItemGetter>(cache);
                    if (item is IArmorGetter)
                        return ArmorUtils.GetGender((IArmorGetter)item);
                    else
                    {
                        List<IArmorGetter> armors = new();
                        GetArmorList(item, armors, ArmorsFormKey);
                        if (!armors.Any()) return TArmorGender.Unknown;
                        var armor = armors.Where(a => ArmorUtils.IsBodyArmor(a));
                        return ArmorUtils.GetGender(armor.First());                    }
                }).Distinct().ToList();
            }
            catch
            {
                return new List<string>() { TArmorGender.Unknown };
            }
        }

    }
}
