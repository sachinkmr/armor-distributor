using System.Collections.Generic;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using ArmorDistributor.Utils;
using ArmorDistributor.Config;
using log4net;
using System;

namespace ArmorDistributor.Armor
{
    public class TArmor
    {
        public IEnumerable<TBodySlot> BodySlots { get; }
        public FormKey FormKey { get; }
        public string Material { get; }
        public TArmorType Type { get; }
        public string? EditorID { get; }
        public TGender Gender { get; }
        public string  Name { get; }

        public TArmor(IArmorGetter armor, string material)
        {
            FormKey = armor.FormKey;
            EditorID = armor.EditorID;
            Material = material;
            BodySlots = ArmorUtils.GetBodySlots(armor);
            Type = ArmorUtils.GetArmorType(armor);
            Gender = ArmorUtils.GetGender(armor);
            Name = ArmorUtils.GetFullName(armor);
        }

        public override bool Equals(object? obj)
        {
            return obj is TArmor armor &&
                   FormKey.Equals(armor.FormKey) &&
                   Material == armor.Material &&
                   Type == armor.Type;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(FormKey, Material, Type);
        }

        public override string ToString()
        {
            return string.Format("{0}:{1}:{2}:{3}:{4}", Name, Material, Type, Gender, FormKey);
        }
    }
}
