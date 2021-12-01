using Noggog;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Synthesis.Settings;
using Mutagen.Bethesda.WPF.Reflection.Attributes;
using OutFitPatcher.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OutFitPatcher.Config
{
    public class UserSettings
    {
        [Ignore]
        [SynthesisOrder]
        [JsonDiskName("JewelryForMales")]
        [SynthesisTooltip("Males NPC will aslo has Jewelry")]
        public bool JewelryForMales=false;

        [Ignore]
        [SynthesisOrder]
        [JsonDiskName("AddArmorsToMannequin")]
        [SynthesisTooltip("Mannequin will have outfits from armor mods")]
        public bool AddArmorsToMannequin=false;

        [SynthesisOrder]
        [JsonDiskName("FilterGurads")]
        [SettingName("Filter Gurads")]
        [SynthesisTooltip("Outfits will not be assigned to guards when seleted")]
        public bool FilterGurads = true;

        [SynthesisOrder]
        [JsonDiskName("FilterUniqueNPC")]
        [SettingName("Filter Unique NPC")]
        [SynthesisTooltip("Outfits will not be assigned to unique NPCs when seleted")]
        public bool FilterUniqueNPC = true;

        [SynthesisOrder]
        [SettingName("Armor Mods To Distribute")]
        [SynthesisTooltip("Select the armor mods and the outfit catergory.\n If category is not selected the mod will use Generic Category.\n In Generic category, outfit will be distributed based on the armor material type.")]
        public HashSet<ModKey> ArmorMods= new();

        [SynthesisOrder]
        [JsonDiskName("JewelryForMales")]
        [SettingName("NPCToSkip")]
        [SynthesisTooltip("These npcs will be skipped")]
        public HashSet<FormKey> NPCToSkip=new();

        [SynthesisOrder]
        [JsonDiskName("ModsToSkip")]
        [SettingName("Skippable Mods")]
        [SynthesisTooltip("Select the mods which you dont want to use in patcher")]
        public HashSet<ModKey> ModsToSkip=new();

        [Ignore]
        [SynthesisOrder]
        [JsonDiskName("JewelryMods")]
        [SynthesisTooltip("Mannequin will have outfits from armor mods")]
        public HashSet<ModKey> JewelryMods=new();

        [Ignore]
        [SynthesisOrder]
        [JsonDiskName("SleepingOutfitMods")]
        [SynthesisTooltip("Mannequin will have outfits from armor mods")]
        public HashSet<ModKey> SleepingOutfitMods=new();

        [Ignore]
        [JsonDiskName("ArmorModsForOutfits")]
        public Dictionary<ModKey, List<string>>? ArmorModsForOutfits { get; set; }

        //[Ignore]
        //public readonly List<string> ModCategories = GetCategories();


        public UserSettings() {
        }

        public static List<string> GetCategories() {
            var SettingFile = Path.Combine(Environment.CurrentDirectory, "Data", "config", "PatcherSettings.json");
            Settings.PatcherSettings = FileUtils.ReadJson<PatcherSettings>(SettingFile);
            List<string> list = new List<string>(Settings.PatcherSettings.OutfitRegex.Keys)
                .Where(x => !x.EndsWith("Armor")).ToList();
            foreach (var fac in Settings.PatcherSettings.DividableFactions.Split("|"))
            {
                Settings.PatcherSettings.ArmorTypeRegex.Keys.ForEach(a => list.Add(fac + a));
            }
            list = list.OrderBy(x => x).ToList();
            return list;
        }
    }
}
