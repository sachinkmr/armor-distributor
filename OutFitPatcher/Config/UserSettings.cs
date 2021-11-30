using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Synthesis.Settings;
using Mutagen.Bethesda.WPF.Reflection.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OutFitPatcher.Config
{
    public class UserSettings
    {
        //[Ignore]
        [SynthesisOrder]
        [SynthesisTooltip("Males NPC will aslo has Jewelry")]
        public bool JewelryForMales=false;


        //[Ignore]
        [SynthesisOrder]
        public bool AddArmorsToMannequin=false;

        //[Ignore]
        [SynthesisOrder]
        public bool FilterGurads = true;

        [SynthesisOrder]
        public bool FilterUniqueNPC =true;

        public List<ModKey> ModsToSkip=new();
        public List<ModKey> JewelryMods = new();
        public List<ModKey> SleepingOutfitMods = new();
        public List<FormKey> NPCToSkip = new();
        public Dictionary<ModKey, List<string>>? ArmorModsForOutfits = new();
    }
}
