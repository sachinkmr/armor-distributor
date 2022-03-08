using ArmorDistributor.Utils;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArmorDistributor.Armor
{
    public class TOutfit
    {
        public string Name { get; }
        public FormKey FormKey;
        public string ArmorType { get; }
        public string Gender { get; }
        public List<TArmorSet> Armorsets { get; }
        
        public TOutfit(string name) {
            this.Name = name;
            var token = name.Split("_");
            this.Gender = token[0];
            this.ArmorType = token[1];
            Armorsets = new();
        }

        public void AddArmorSets(IEnumerable<TArmorSet> sets)
        {
            sets.ForEach(s => AddArmorSet(s));
        }

        public void AddArmorSet(TArmorSet set)
        {
            if (!Armorsets.Contains(set))
                Armorsets.Add(set);
        }

        public ISkyrimMod CreateOutfit(ISkyrimMod Patch)
        {
            Patch = FileUtils.GetIncrementedMod(Patch);
            var set = Armorsets.Select(a => a.LLFormKey.AsLink<IItemGetter>());
            Outfit newOutfit = OutfitUtils.CreateOutfit(Patch, Name, set);
            FormKey = newOutfit.FormKey;
            return Patch;
        }

        public override bool Equals(object? obj)
        {
            return obj is TOutfit outfit &&
                   Name == outfit.Name;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Name);
        }

        public static bool operator ==(TOutfit? left, TOutfit? right)
        {
            return EqualityComparer<TOutfit>.Default.Equals(left, right);
        }

        public static bool operator !=(TOutfit? left, TOutfit? right)
        {
            return !(left == right);
        }
    }
}
