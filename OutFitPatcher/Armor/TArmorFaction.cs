using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OutFitPatcher.Armor
{
    public class TArmorFaction : TArmorGroupable
    {
        public TArmorFaction(string name) : base(name)
        {
        }

        public override bool Equals(object? obj)
        {
            return obj is TArmorFaction faction &&
                   Name == faction.Name;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Name);
        }
        
    }
}
