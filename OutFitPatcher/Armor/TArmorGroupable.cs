using System;
using System.Collections.Generic;
using System.Linq;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda;
using OutFitPatcher.Utils;
using Noggog;
using System.Collections.Concurrent;
using OutFitPatcher.Config;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Cache.Implementations;

namespace OutFitPatcher.Armor
{
    public class TArmorGroupable
    {
        public string Name { get; }

        public FormKey LLKey=FormKey.Null;
        public FormKey OutfitKey = FormKey.Null;

        public ConcurrentBag<TArmorSet> Armors { get; }
        public ConcurrentDictionary<FormKey, string> Outfits { get; }

        public TArmorGroupable(string name) {
            Name = name;
            Armors = new();
            Outfits = new();
        }

        public void AddArmorSet(TArmorSet set)
        {
            if (!Armors.Contains(set))
                Armors.Add(set);
        }

        public void AddArmorSets(IEnumerable<TArmorSet> sets)
        {
            sets.ForEach(s => AddArmorSet(s));
        }

        public void AddOutfit(IOutfitGetter outfit)
        {
            if (!Outfits.ContainsKey(outfit.FormKey))
                Outfits.TryAdd(outfit.FormKey, outfit.EditorID);
        }

        public void AddOutfits(IEnumerable<IOutfitGetter> outfits)
        {
            outfits.ForEach(o => AddOutfit(o));
        }

        public void AddOutfits(MutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> cache, IEnumerable<KeyValuePair<FormKey, string>> outfits)
        {            
            outfits.ForEach(o => {
                var ot = cache.Resolve<IOutfitGetter>(o.Key);
                AddOutfit(ot);
            });
        }


        public FormKey GetLeveledListUsingArmorSets(ISkyrimMod PatchMod, bool createLL = false)
        {
            LeveledItem ll = null;
            if (LLKey == FormKey.Null || createLL)
            {
                var list = Armors.Select(a => a.CreateLeveledList().AsLink<IItemGetter>());
                ll = OutfitUtils.CreateLeveledList(PatchMod, list, Configuration.Patcher.LeveledListPrefix + Name, 1, Configuration.LeveledListFlag);
                LLKey = ll.FormKey;
            }
            return LLKey;
        }

        public IEnumerable<FormLink<IItemGetter>> GetLeveledListsUsingArmorSets()
        {
            return Armors.Select(a => a.CreateLeveledList().AsLink<IItemGetter>()); ;
        }

        public override string? ToString()
        {
            return Name;
        }
    }
}
