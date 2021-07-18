using log4net;
using Mutagen.Bethesda;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using OutFitPatcher.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace OutFitPatcher.Utils
{
    public class OutfitUtils
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(OutfitUtils));
        private static Dictionary<string, FormKey> lls = new();

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

            LeveledItem lvli = PatchMod.LeveledItems.AddNew(editorID);
            lvli.Entries = new ExtendedList<LeveledItemEntry>();
            lvli.Flags = flag;

            AddItemsToLeveledList(PatchMod, lvli, items, 1);
            lls.TryAdd(editorID, lvli.FormKey);
            return lvli;
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

            LeveledItem lvli = PatchMod.LeveledItems.AddNew(editorID);
            lvli.Entries = new ExtendedList<LeveledItemEntry>();
            lvli.Flags = flag;

            AddItemsToLeveledList(PatchMod, lvli, items, 1);
            lls.TryAdd(editorID, lvli.FormKey);
            return lvli;
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
                    sLL = CreateLeveledList(patch, new List<IItemGetter>(), lvli.EditorID + (++j), 1, Configuration.LeveledListFlag);
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
                    sLL = CreateLeveledList(patch, new List<IItemGetter>(), lvli.EditorID + (++j), 1, Configuration.LeveledListFlag);
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

        public static Outfit CreateOutfit(ISkyrimMod patch, IEnumerable<IFormLinkGetter<IOutfitTargetGetter>> items, string prefix)
        {
            Outfit otft = patch.Outfits.AddNew();
            otft.EditorID = prefix + "_OTFT";
            otft.Items = new(items);
            return otft;
        }

        public static Outfit CreateOutfit(ISkyrimMod patch, IFormLinkGetter<IOutfitTargetGetter> item, string prefix)
        {
            Outfit otft = patch.Outfits.AddNew();
            otft.EditorID = prefix + "_OTFT";
            otft.Items = new();
            otft.Items.Add(item);
            return otft;
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

        private static void GetArmorList(ILeveledItemGetter ll, List<IArmorGetter> armors, HashSet<FormKey> processed)
        {
            ILinkCache cache = Configuration.Cache;
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
    }
}
