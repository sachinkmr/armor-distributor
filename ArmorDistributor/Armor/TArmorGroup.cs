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
using Newtonsoft.Json;

namespace ArmorDistributor.Armor
{
    public class TArmorGroup
    {
        public string Name { get; }
        
        public Dictionary<TGender, Dictionary<TArmorType, FormKey>> GenderOutfit = new();

        public List<TArmorSet> Armorsets { get; }

        public Dictionary<FormKey, string> Outfits { get; }

        public Dictionary<FormKey, string> NPCs;
        
        public Dictionary<string, Dictionary<FormKey, string>> Identifiers;

        public TArmorGroup(string name)
        {
            Name = name;
            NPCs = new();
            Armorsets = new();
            Outfits = new();
            Identifiers = new();
        }

        public void AddArmorSet(TArmorSet set)
        {
            if (!Armorsets.Contains(set))
                Armorsets.Add(set);
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
            outfits.ForEach(o =>
            {
                var ot = cache.Resolve<IOutfitGetter>(o.Key);
                AddOutfit(ot);
            });
        }

        public ISkyrimMod CreateOutfits(ISkyrimMod Patch)
        {
            var GenderedArmors = Armorsets.GroupBy(x => x.Gender).ToDictionary(x => x.Key, x => x.Select(a => a));
            var armors = GenderedArmors.ToDictionary(x => x.Key, x => x.Value
               .GroupBy(x => x.Type).ToDictionary(x => x.Key, x => x.Select(a => a)));

            foreach (var g in armors.Keys)
            {
                //List<FormLink<IItemGetter>> list = new();
                foreach (var t in armors[g].Keys)
                {
                    string eid = Name + "_" + g + "_" + t + "_" + (Program.Settings.UserSettings.CreateOutfitsOnly ? Guid.NewGuid().ToString() : "");
                    Patch = FileUtils.GetIncrementedMod(Patch);
                    var set = armors[g][t].Select(a => a.LLFormKey.AsLink<IItemGetter>());
                    Outfit newOutfit = OutfitUtils.CreateOutfit(Patch, eid, set);
                    GenderOutfit.GetOrAdd(g).Add(t, newOutfit.FormKey);
                    //list.AddRange(newOutfit.Items.Select(i => i.FormKey.AsLink<IItemGetter>()));
                }

                //// All outfits
                //string eidAll = Name + "_" + g + "_ALL_" + (Program.Settings.UserSettings.CreateOutfitsOnly ? Guid.NewGuid().ToString() : "");
                //Outfit newOutfit1 = OutfitUtils.CreateOutfit(Patch, eidAll, list);
                //GenderOutfit.GetOrAdd(g).Add(TArmorType.ALL, newOutfit1.FormKey);
            }
            return Patch;
        }

        public override bool Equals(object? obj)
        {
            return obj is TArmorGroup category &&
                   Name == category.Name;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Name);
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
