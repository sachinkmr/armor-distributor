using Mutagen.Bethesda.Plugins;
using System.Collections.Generic;
using System.Text.RegularExpressions;

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
        //public string? DividableFactions;

        // Prefixes and sufixes
        public string? PatcherPrefix;
        public string? LeveledListPrefix;
        public string? OutfitPrefix;
        public string? OutfitSuffix;
        public string? SLPLeveledListPrefix;
        public string? OutfitPatchedKeywordEID;

        public Regex ArmorMaterialRegex = new(@"(?:Armor|Weap(?:on)?)?Materi[ae]l(\w+)", RegexOptions.IgnoreCase);
        public bool AddArmorsToMannequin = false;
        public string MannequinOutfitEID="";
        public FormKey OutfitPatchedKeyword = FormKey.Null;
        public List<string> Masters = new();

        public Dictionary<string, string> OutfitRegex = new();
        public Dictionary<string, string> ArmorTypeRegex = new();
        public Dictionary<string, string> SkippableRegex = new();

        public PatcherSettings init() {
            MannequinOutfitEID = "ZZZ_Mannequins" + this.OutfitSuffix;
            return this;
        }
    }
}
