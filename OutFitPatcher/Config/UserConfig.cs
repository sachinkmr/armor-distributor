using System.Collections.Generic;

namespace OutFitPatcher
{
    public class UserConfig
    {
        public bool JewelryForMales;
        public bool MergePatcherMods;
        public bool AddArmorsToMannequin;
        public bool PatchGurads;

        public List<string>? ModsToSkip;
        public List<string>? JewelryMods;
        public List<string>? SleepingOutfitMods;
        public List<string>? NPCToSkip;
        public Dictionary<string, List<string>>? ArmorModsForOutfits;
    }
}
