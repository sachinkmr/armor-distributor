using Mutagen.Bethesda.Plugins;
using System.Collections.Generic;


namespace ArmorDistributor.Config
{
    public class PatcherSettings
    {
        // Settings.json properties
        public string? InvalidOutfitRegex;
        public string? InvalidArmorsRegex;
        public string? InvalidFactionRegex;
        public string? ValidFactionRegex;
        public string? ValidArmorsRegex;
        public string? ValidOutfitRegex;
        public string? ValidNpcRegex;
        public string? InvalidNpcRegex;
        public string? DividableFactions;

        // Prefixes and sufixes
        public string? PatcherPrefix;
        public string? LeveledListPrefix;
        public string? SLPLeveledListPrefix;
        public string? OutfitPatchedKeywordEID;

        public bool AddArmorsToMannequin = false;
        public string MannequinOutfitEID = "ZZZ_Mannequins_OTFT";
        public FormKey OutfitPatchedKeyword = FormKey.Null;

        public List<string> ClothesType = new();
        public List<string> RobesType = new();
        public List<string> Masters = new();

        public Dictionary<string, string> OutfitRegex = new();
        public Dictionary<string, string> ArmorTypeRegex = new();
        public List<string> MaleSleepingWears = new();
    }
}
