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
        [Ignore]
        [SynthesisOrder]
        [JsonDiskName("JewelryForMales")]
        [SynthesisTooltip("Males NPC will aslo has Jewelry")]
        public bool JewelryForMales = false;

        [Ignore]
        [SynthesisOrder]
        [JsonDiskName("AddArmorsToMannequin")]
        [SynthesisTooltip("Mannequin will have outfits from armor mods")]
        public bool AddArmorsToMannequin=false;

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
        public HashSet<FormKey> NPCToSkip=new();

        [SynthesisOrder]
        [JsonDiskName("ModsToSkip")]
        [SettingName("Skip Mods: ")]
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
        public Dictionary<ModKey, List<string>>? ArmorModsForOutfits;

        //public static List<string> GetCategories() {
        //    var SettingFile = Path.Combine(Environment.CurrentDirectory, "Data", "config", "PatcherSettings.json");
        //    Settings.PatcherSettings = FileUtils.ReadJson<PatcherSettings>(SettingFile);
        //    List<string> list = new List<string>(Settings.PatcherSettings.OutfitRegex.Keys)
        //        .Where(x => !x.EndsWith("Armor")).ToList();
        //    foreach (var fac in Settings.PatcherSettings.DividableFactions.Split("|"))
        //    {
        //        Settings.PatcherSettings.ArmorTypeRegex.Keys.ForEach(a => list.Add(fac + a));
        //    }
        //    list = list.OrderBy(x => x).ToList();
        //    return list;
        //}        
    }
    public enum Percentage
    {
        _0 = 0, _10 = 10, _20 = 20, _30 = 30, _40 = 40, _50 = 50, _60 = 60, _70 = 70, _80 = 80, _90 = 90, _100 = 100
    }

    public enum Categories {
        Generic,
        Greybeard,
        Skaal,
        WeddingDress,
        Dunmer,
        Altmer,
        Bosmer,
        Falmer,
        Nord,
        Breton,
        Orc,
        Vampire,
        Argonian,
        Khajiit,
        FalkreathGuards,
        HjaalmarchGuards,
        ReachGuards,
        PaleGuards,
        RiftGuards,
        HaafingarGuards,
        WhiterunGuards,
        EastmarchGuards,
        WinterholdGuards,
        ImperialGuards,
        StormcloakGuards,
        Housecarl,
        Redguard,
        Vigilant,
        Thief,
        Warrior,
        WenchTavern,
        Daedric,
        Thalmor,
        Imperial,
        Stormcloak,
        Dawnguard,
        Bandit,
        Mercenary,
        Forsworn,
        CollegeWizard,
        CourtWizard,
        Psiijic,
        Bard,
        Blacksmith,
        Merchant,
        Miner,
        Farmer,
        BarKeeper,
        Chef,
        Hunter,
        Companion,
        DarkBrotherhood,
        Servent,
        Jarl,
        Daedra,
        Cultist,
        Warlock,
        Blade,
        Sailor,
        Lumberjack,
        Traveller,
        Prisoner,
        Beggar,
        CitizenRich,
        CitizenPoor,
        Necromancer,
        Mage,
        Healer,
        Archer,
        Assassin,
        Jailor,
        Knight,
        Nightingale,
        Child,
        Psijic
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
