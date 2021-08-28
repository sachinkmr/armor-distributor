using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OutFitPatcher.Config
{
    public class PatcherConfig
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
