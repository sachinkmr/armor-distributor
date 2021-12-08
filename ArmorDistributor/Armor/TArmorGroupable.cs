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
    public abstract class TArmorGroupable
    {
        public string Name { get; }

        public Dictionary<string, Dictionary<string, FormKey>> GenderOutfit;

        public ConcurrentBag<TArmorSet> Armors { get; }

        public ConcurrentDictionary<FormKey, string> Outfits { get; }

        public TArmorGroupable(string name) {
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

        public void CreateOutfits(ISkyrimMod? PatchedMod)
        {
            var GenderedArmors = Armors.GroupBy(x => x.Gender).ToDictionary(x=> x.Key, x=>x.Select(a=>a));
            var TypedArmors = Armors.GroupBy(x=>x.Type).ToDictionary(x => x.Key, x => x.Select(a => a));

            Dictionary<string, List<FormLink<IItemGetter>>> LLs = new();
            foreach (var gKey in GenderedArmors.Keys)
            {
                var gVal = GenderedArmors[gKey];
                foreach (var aKey in TypedArmors.Keys) {
                    var tVal = TypedArmors[aKey];
                    var common = tVal.Intersect(gVal)
                        .Select(a => a.CreateLeveledList(PatchedMod).AsLink<IItemGetter>())
                        .ToList();
                    LLs.Add(aKey+"_"+ gKey, common);
                }
            }

            LLs.ForEach(x => {
                string eid = Settings.PatcherSettings.LeveledListPrefix + "mLL_" + Name + "_" + x.Key;
                LeveledItem mLL = OutfitUtils.CreateLeveledList(PatchedMod, x.Value, eid, 1, LeveledListFlag);
                Outfit newOutfit = PatchedMod.Outfits.AddNew(eid);
                newOutfit.Items = new(mLL.AsLink().AsEnumerable());

                var keys = x.Key.Split("_");
                GenderOutfit.GetOrAdd(keys[1]).Add(keys[0], newOutfit.FormKey);
            });
        }

        public override string? ToString()
        {
            return Name;
        }
    }
}
