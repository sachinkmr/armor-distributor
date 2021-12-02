using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda;
using ArmorDistributor.Utils;
using System.Runtime.InteropServices;
using Noggog;
using System.Collections.Concurrent;

namespace ArmorDistributor.Armor
{
    public class TArmorMaterial: TArmorGroupable
    {

        public TArmorMaterial(string material) : base(material)
        {            
        }

        public override bool Equals(object? obj)
        {
            return obj is TArmorMaterial material &&
                   Name == material.Name;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Name);
        }
    }
}
