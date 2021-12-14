using System;
using System.Collections.Generic;
using System.Linq;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda;
using ArmorDistributor.Utils;
using Noggog;
using System.Collections.Concurrent;
using ArmorDistributor.Config;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache.Internals.Implementations;
using static ArmorDistributor.Config.Settings;

namespace ArmorDistributor.Armor
{
    public class TArmorCategory
    {
        public string Name { get; }

        public Dictionary<string, Dictionary<string, FormKey>> GenderOutfit;

        public ConcurrentBag<TArmorSet> Armors { get; }

        public ConcurrentDictionary<FormKey, string> Outfits { get; }

        public TArmorCategory(string name) {
            Name = name;
            Armors = new();
            Outfits = new();
            GenderOutfit=new();
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

        //public void CreateOutfits(ISkyrimMod? PatchedMod)
        //{
        //    var GenderedArmors = Armors.GroupBy(x => x.Gender).ToDictionary(x=> x.Key, x=>x.Select(a=>a));
        //    GenderedArmors.ForEach(x =>
        //    {
        //        var ll= x.Value.Select(a => a.CreateLeveledList(PatchedMod).AsLink<IItemGetter>());
        //        string eid = Settings.PatcherSettings.LeveledListPrefix + "mLL_" + Name + "_" + x.Key;
        //        LeveledItem mLL = OutfitUtils.CreateLeveledList(PatchedMod, ll, eid, 1, LeveledListFlag);
        //        Outfit newOutfit = PatchedMod.Outfits.AddNew(eid);
        //        newOutfit.Items = new(mLL.AsLink().AsEnumerable());
        //        GenderOutfit[x.Key] = newOutfit.FormKey;
        //    });
        //}

        public void CreateOutfits(ISkyrimMod? PatchedMod)
        {
            var GenderedArmors = Armors.GroupBy(x => x.Gender).ToDictionary(x => x.Key, x => x.Select(a => a));
            var armors = GenderedArmors .ToDictionary(x => x.Key, x=>x.Value
                .GroupBy(x => x.Type).ToDictionary(x => x.Key, x => x.Select(a => a)));
            
            foreach (var g in armors.Keys) {
                foreach (var t in armors[g].Keys) {
                    string eid = Settings.PatcherSettings.LeveledListPrefix + "mLL_" + Name + "_" + g+"_"+t;
                    var set = armors[g][t].Select(a => a.CreateLeveledList(PatchedMod).AsLink<IItemGetter>());
                    LeveledItem mLL = OutfitUtils.CreateLeveledList(PatchedMod, set, eid, 1, LeveledListFlag);
                    Outfit newOutfit = PatchedMod.Outfits.AddNew(eid);
                    newOutfit.Items = new(mLL.AsLink().AsEnumerable());
                    GenderOutfit.GetOrAdd(g).Add(t, newOutfit.FormKey);
                }
            }
        }

        public override bool Equals(object? obj)
        {
            return obj is TArmorCategory category &&
                   Name == category.Name;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Name);
        }

        public override string? ToString()
        {
            return Name;
        }
    }
}
