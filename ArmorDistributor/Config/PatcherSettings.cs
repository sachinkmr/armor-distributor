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
        public string? SluttyRegex;
        public string? KeywordFile;
        public string? ValidMaterial;
        public string? InvalidMaterial;

        // Prefixes and suffixes
        public string? PatcherPrefix;
        public string? LeveledListPrefix;
        public string? OutfitPrefix;
        public string? OutfitSuffix;

        public List<string> Masters = new();
        public List<string> KeywordsSPID = new();

        public Dictionary<string, string> OutfitRegex = new();
        public Dictionary<string, string> OutfitTypeRegex = new();
        public Dictionary<string, string> ArmorTypeRegex = new();
        public Dictionary<string, string> SkippableRegex = new();
        public Dictionary<string, string> SkillBasedArmors = new();

        public PatcherSettings init() {
            return this;
        }
    }
}
