using Noggog;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Synthesis.Settings;
using Mutagen.Bethesda.WPF.Reflection.Attributes;
using ArmorDistributor.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mutagen.Bethesda.Skyrim;

namespace ArmorDistributor.Config
{
    public class UserSettings
    {
        [SynthesisOrder]
        [JsonDiskName("DefaultOutfitPercentage")]
        [SettingName("Distribute Default Outfits By: ")]
        [SynthesisTooltip("Along with modeed outfits, distribute default outfits as well by mentioned percentage.")]
        public Percentage DefaultOutfitPercentage { get; set; } = Percentage._10;

        [SynthesisOrder]
        [JsonDiskName("FilterUniqueNPC")]
        [SettingName("Filter Unique NPC: ")]
        [SynthesisTooltip("Outfits will not be assigned to unique NPCs when seleted")]
        public bool FilterUniqueNPC = true;

        [SynthesisOrder]
        [JsonDiskName("CreateBashPatch")]
        [SettingName("Bash Patch For Leveled Lists: ")]
        [SynthesisTooltip("Outfits will not be assigned to unique NPCs when seleted")]
        public bool CreateBashPatch = true;

        [SynthesisOrder]
        [SettingName("Armor Mods: ")]
        [SynthesisTooltip("Select the armor mods and the outfit catergory.\nIf category is not selected the mod will use Generic Category.\nFor Generic category, outfit will be created based on the armor material type.")]
        public List<ModCategory> ArmorMods = new();

        [SynthesisOrder]
        [JsonDiskName("NPCToSkip")]
        [SettingName("Skip NPCs: ")]
        [SynthesisTooltip("These npcs will be skipped")]
        public HashSet<FormKey> NPCToSkip = new();

        [SynthesisOrder]
        [JsonDiskName("ModsToSkip")]
        [SettingName("Skip Mods: ")]
        [SynthesisTooltip("Select the mods which you dont want to use in patcher")]
        public HashSet<ModKey> ModsToSkip = new();

        [Ignore]
        [JsonDiskName("ArmorModsForOutfits")]
        public Dictionary<string, List<string>>? ArmorModsForOutfits;

        [Ignore]
        [SynthesisOrder]
        [JsonDiskName("JewelryForMales")]
        [SynthesisTooltip("Males NPC will aslo has Jewelry")]
        public bool JewelryForMales = false;

        [Ignore]
        [SynthesisOrder]
        [JsonDiskName("AddArmorsToMannequin")]
        [SynthesisTooltip("Mannequin will have outfits from armor mods")]
        public bool AddArmorsToMannequin = false;

        [Ignore]
        [SynthesisOrder]
        [JsonDiskName("JewelryMods")]
        [SynthesisTooltip("Mannequin will have outfits from armor mods")]
        public HashSet<ModKey> JewelryMods = new();

        [Ignore]
        [SynthesisOrder]
        [JsonDiskName("SleepingOutfitMods")]
        [SynthesisTooltip("Mannequin will have outfits from armor mods")]
        public HashSet<ModKey> SleepingOutfitMods = new();

        public UserSettings()
        {
            ArmorModsForOutfits = ArmorMods.ToDictionary(x => x.ArmorMod.FileName.ToString(), 
                x => x.Categories.Select(c => c.ToString()).ToList());
            ArmorModsForOutfits.Values.ForEach(x => {
                if (!x.Any()) x.Add(Categories.Generic.ToString());
            });
        }
    }
    public enum Percentage
    {
        _0 = 0, _10 = 10, _20 = 20, _30 = 30, _40 = 40, _50 = 50, _60 = 60, _70 = 70, _80 = 80, _90 = 90, _100 = 100
    }

    

    public enum Categories {
        Generic,
        Altmer,
        Archer,
        Argonian,
        Assassin,
        Bandit,
        Bard,
        BarKeeper,
        Beggar,
        Blacksmith,
        Blade,
        Bosmer,
        Breton,
        Chef,
        Child,
        CitizenPoor,
        CitizenRich,
        CollegeWizard,
        Companion,
        CourtWizard,
        Cultist,
        Daedra,
        Daedric,
        DarkBrotherhood,
        Dawnguard,
        Dunmer,
        EastmarchGuards,
        FalkreathGuards,
        Falmer,
        Farmer,
        Forsworn,
        Greybeard,
        HaafingarGuards,
        Healer,
        HjaalmarchGuards,
        Housecarl,
        Hunter,
        Imperial,
        ImperialGuards,
        Jailor,
        Jarl,
        Khajiit,
        Knight,
        Lumberjack,
        Mage,
        Mercenary,
        Merchant,
        Miner,
        Necromancer,
        Nightingale,
        Nord,
        Orc,
        PaleGuards,
        Prisoner,
        Psijic,
        ReachGuards,
        Redguard,
        RiftGuards,
        Sailor,
        Servent,
        Skaal,
        Stormcloak,
        StormcloakGuards,
        Thalmor,
        Thief,
        Traveller,
        Vampire,
        Vigilant,
        Warlock,
        Warrior,
        WeddingDress,
        WenchTavern,
        WhiterunGuards,
        WinterholdGuards
    }

    public class ModCategory
    {
        [SynthesisOrder]
        [SettingName("Armor Mod: ")]
        [SynthesisTooltip("Select the armor mods to create outfits")]
        public ModKey ArmorMod = new();

        [SynthesisOrder]
        [SettingName("Outfit Categories: ")]
        [SynthesisTooltip("Select the categories for the above selected armor mod to create outfits. " +
            "\nOutfits created by the mod will be distributes among these categories")]
        public List<Categories> Categories = new ();
    }
}
