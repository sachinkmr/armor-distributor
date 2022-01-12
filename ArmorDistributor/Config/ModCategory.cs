using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Synthesis.Settings;
using Mutagen.Bethesda.WPF.Reflection.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArmorDistributor.Config
{
    public class ModCategory
    {
        [MaintainOrder]
        [SettingName("Armor Mod: ")]
        [SynthesisTooltip("Select the armor mods to create outfits")]
        public ModKey ArmorMod = new();

        [MaintainOrder]
        [SettingName("Outfit Categories: ")]
        [SynthesisTooltip("Select the categories for the above selected armor mod to create outfits. " +
            "\nOutfits created by the mod will be distributes among these categories")]
        public List<Categories> Categories = new();

        public ModCategory() { }

        public ModCategory(ModKey ArmorMod, List<Categories> Categories) { 
            this.ArmorMod = ArmorMod;
            this.Categories = Categories;
        }
    }
}
