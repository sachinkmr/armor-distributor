using ArmorDistributor.Utils;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArmorDistributor.Armor
{
    public class TWeapon
    {
        public FormKey FormKey { get; }
        public string Type  { get; }
        public string Name { get; }

        public TWeapon(IWeaponGetter weapon)
        {
            FormKey = weapon.FormKey;
            Name = ArmorUtils.GetFullName(weapon);
            Type = weapon.Data.AnimationType.ToString();
        }
    }
}
