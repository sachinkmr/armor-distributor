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
using Mutagen.Bethesda.Plugins.Cache.Internals.Implementations;

namespace OutFitPatcher.Armor
{
    public class TArmorGroupable
    {
        public string Name { get; }

        public Dictionary<string, FormKey> GenderOutfit;

        public ConcurrentBag<TArmorSet> Armors { get; }

        public ConcurrentDictionary<FormKey, string> Outfits { get; }

        public TArmorGroupable(string name) {
            Name = name;
            Armors = new();
            Outfits = new();
            GenderOutfit=new();
            GenderOutfit.Add("M", FormKey.Null);
            GenderOutfit.Add("C", FormKey.Null);
            GenderOutfit.Add("F", FormKey.Null);
            GenderOutfit.Add("U", FormKey.Null);
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

        public void CreateGroupOutfits(ISkyrimMod? PatchedMod) {
            foreach (var g in GenderOutfit.Keys) {
                var lls = GetLeveledListsUsingArmorSets(g);
                if (lls.Any()) {
                    LeveledItem mLLC = OutfitUtils.CreateLeveledList(PatchedMod, lls, "mLL_" + Name + "_" + g, 1, Configuration.LeveledListFlag);
                    Outfit gOTFT = PatchedMod.Outfits.AddNew(Name + "_Outfit_"+g);
                    gOTFT.Items = new();
                    gOTFT.Items.Add(mLLC);
                    GenderOutfit[g] = gOTFT.FormKey;
                }                
            }           
        }

        public IEnumerable<FormLink<IItemGetter>> GetLeveledListsUsingArmorSets(string gender="C")
        {
            return Armors.Where(x=>x.Gender== gender)
                .Select(a => a.CreateLeveledList().AsLink<IItemGetter>()); ;
        }

        public override string? ToString()
        {
            return Name;
        }
    }
}
