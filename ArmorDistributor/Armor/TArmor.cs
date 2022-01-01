using System.Collections.Generic;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using ArmorDistributor.Utils;
using ArmorDistributor.Config;
using log4net;

namespace ArmorDistributor.Armor
{
    public class TArmor
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(TArmor));

        public IEnumerable<TBodySlot> BodySlots { get; }
        public FormKey FormKey { get; }
        public string Material { get; }
        public string Type { get; }
        public string? EditorID { get; }
        public string Gender { get; }
        public string?  Name { get; }

        public TArmor(IArmorGetter armor, string material)
        {
            FormKey = armor.FormKey;
            EditorID = armor.EditorID;
            Material = material;
            BodySlots = ArmorUtils.GetBodySlots(armor);
            Type = ArmorUtils.GetArmorType(armor);
            Gender = ArmorUtils.GetGender(armor);
            Name = armor.Name == null || armor.Name.String.IsNullOrEmpty() ? HelperUtils.SplitString( armor.EditorID ): armor.Name.ToString();
            //if (IsBody()) {
            //    var addon = Settings.Cache.Resolve<IArmorAddon>(armor.Armature.FirstOrDefault().FormKey);
            //    if (addon.WorldModel == null && addon.WorldModel.Male == null || addon.WorldModel.Female == null) {
            //        Logger.WarnFormat("Matching addon not found: {0}::{1}", armor.EditorID, armor.FormKey);
            //    }
            //}            
        }

        public TArmor(IArmorGetter armor): 
            this(armor, ArmorUtils.GetMaterial(armor))
        {            
        }

        public bool IsBody() { return BodySlots.Any(x=> ArmorUtils.IsUpperArmor(x)); }

    }
}
