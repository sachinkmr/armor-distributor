using System.Collections.Generic;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Skyrim;
using System.Text.RegularExpressions;
using log4net;
using ArmorDistributor.Armor;
using ArmorDistributor.Config;
using Noggog;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using System;

namespace ArmorDistributor.Utils
{
    public class ArmorUtils
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(ArmorUtils));
        internal static Dictionary<string, FormLink<IKeywordGetter>> RobesType = new();
        internal static Dictionary<string, FormLink<IKeywordGetter>> ClothesType = new();
        internal static HashSet<FormLink<IKeywordGetter>> ArmorMaterials
            = new FormLink<IKeywordGetter>[] {
                Skyrim.Keyword.ArmorMaterialDaedric,
                Skyrim.Keyword.ArmorMaterialDragonplate,
                Skyrim.Keyword.ArmorMaterialDragonscale,
                Skyrim.Keyword.ArmorMaterialDwarven,
                Skyrim.Keyword.ArmorMaterialEbony,
                Skyrim.Keyword.ArmorMaterialElven,
                Skyrim.Keyword.ArmorMaterialElvenGilded,
                Skyrim.Keyword.ArmorMaterialGlass,
                Skyrim.Keyword.ArmorMaterialHide,
                Skyrim.Keyword.ArmorMaterialImperialHeavy,
                Skyrim.Keyword.ArmorMaterialImperialLight,
                Skyrim.Keyword.ArmorMaterialImperialStudded,
                Skyrim.Keyword.ArmorMaterialIron,
                Skyrim.Keyword.ArmorMaterialIronBanded,
                Skyrim.Keyword.ArmorMaterialLeather,
                Skyrim.Keyword.ArmorMaterialOrcish,
                Skyrim.Keyword.ArmorMaterialScaled,
                Skyrim.Keyword.ArmorMaterialSteel,
                Skyrim.Keyword.ArmorMaterialSteelPlate,
                Skyrim.Keyword.ArmorMaterialStormcloak,
                Skyrim.Keyword.ArmorMaterialStudded}
        .ToHashSet();
        internal static IEnumerable<TBodySlot> ArmorSlots = HelperUtils.GetEnumValues<TBodySlot>();


        private static string GetClothingType(IArmorGetter armor)
        {
            // MAtching Clothing and Robes types
            string eid = armor.EditorID;
            string name = armor.Name == null || armor.Name.String == null ? "" : armor.Name.String;
            var matches = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, armor.EditorID, name);
            if (!matches.Any())
                matches = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, name);

            if (!matches.Any())
                matches = Settings.PatcherSettings.RobesType.Where(x => Regex.Match(name, x, RegexOptions.IgnoreCase).Success
            || Regex.Match(eid, x, RegexOptions.IgnoreCase).Success);
            
            if (!matches.Any())
                matches = Settings.PatcherSettings.ClothesType
                    .Where(x => Regex.Match(name, x, RegexOptions.IgnoreCase).Success
                    || Regex.Match(eid, x, RegexOptions.IgnoreCase).Success);
            if (matches.Any()) 
                return matches.First();
            
            var type = "Unknown";
            //if (!armor.ObjectEffect.IsNull) type= "Robes";
            //type=armor.Value > 3 ? "Fine Clothes" : "Poor Clothes";
            Logger.DebugFormat("Unknown: {0}, {1} | Assigned: {2} ", eid, name, type);
            return type;
        }

        private static string GetItemMaterial(IArmorGetter armor)
        {
            Regex mRegex = new(@"(?:Armor|Weap(?:on)?)?Materi[ae]l(\w+)", RegexOptions.IgnoreCase);
            ILinkCache cache = Settings.Cache;
            if (armor.Keywords != null)
                foreach (FormLink<IKeywordGetter> keyword in armor.Keywords)
                    if (ArmorMaterials.Contains(keyword))
                    {
                        var val = mRegex.Match(cache.Resolve<IKeywordGetter>(keyword.FormKey).EditorID);
                        return val.Groups[1].Value+"Armor";
                    }
            Logger.DebugFormat("Missing Armor Material Keyword: " + armor.FormKey.ToString());
            var matches = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, armor.EditorID);
            return matches.Any() ? matches.First() : "Unknown";
        }

        public static string GetMaterial(IArmorGetter item)
        {
            if (item.HasKeyword(Skyrim.Keyword.ArmorJewelry)
                || item.HasKeyword(Skyrim.Keyword.ClothingNecklace)
                || item.HasKeyword(Skyrim.Keyword.VendorItemJewelry))
                return TArmorType.Jewelry;

            return !IsCloth(item) ? GetItemMaterial(item) :
                HelperUtils.ToCamelCase(GetClothingType((IArmorGetter)item));
        }

        public static bool IsCloth(IArmorGetter item)
        {
            return item.HasKeyword(Skyrim.Keyword.ArmorClothing)
                || item.BodyTemplate.ArmorType.Equals(ArmorType.Clothing);
        }

        public static bool IsValidArmor(IArmorGetter armor)
        {
            return !armor.MajorFlags.HasFlag(Mutagen.Bethesda.Skyrim.Armor.MajorFlag.NonPlayable)
                    && armor.Armature != null && armor.Armature.Count > 0
                    && (Regex.Match(armor.EditorID, Settings.PatcherSettings.ValidArmorsRegex, RegexOptions.IgnoreCase).Success
                    || !Regex.Match(armor.EditorID, Settings.PatcherSettings.InvalidArmorsRegex, RegexOptions.IgnoreCase).Success);
        }

        public static bool IsValidOutfit(IOutfitGetter outfit)
        {
            return Regex.Match(outfit.EditorID, Settings.PatcherSettings.ValidOutfitRegex, RegexOptions.IgnoreCase).Success
                    || !Regex.Match(outfit.EditorID, Settings.PatcherSettings.InvalidOutfitRegex, RegexOptions.IgnoreCase).Success;
        }

        public static bool IsValidOutfit(string outfit)
        {
            return Regex.Match(outfit, Settings.PatcherSettings.ValidOutfitRegex, RegexOptions.IgnoreCase).Success
                    || !Regex.Match(outfit, Settings.PatcherSettings.InvalidOutfitRegex, RegexOptions.IgnoreCase).Success;
        }

        public static string GetArmorType(IArmorGetter armor)
        {
            if (armor.HasKeyword(Skyrim.Keyword.ArmorJewelry)
                || armor.HasKeyword(Skyrim.Keyword.ClothingNecklace)
                || armor.HasKeyword(Skyrim.Keyword.VendorItemJewelry))
                return TArmorType.Jewelry;

            if (armor.HasKeyword(Skyrim.Keyword.ArmorShield))
                return TArmorType.Shield;

            if (armor.HasKeyword(Skyrim.Keyword.ArmorHeavy)
                || armor.BodyTemplate.ArmorType.Equals(ArmorType.HeavyArmor))
                return TArmorType.Heavy;

            if (armor.HasKeyword(Skyrim.Keyword.ArmorLight)
                || armor.BodyTemplate.ArmorType.Equals(ArmorType.LightArmor))
                return TArmorType.Light;

            if (armor.HasKeyword(Skyrim.Keyword.ArmorHelmet))
                return TArmorType.Helmet;

            if (IsCloth(armor))
                return Regex.IsMatch(armor.EditorID, Settings.PatcherSettings.ArmorTypeRegex["Wizard"], RegexOptions.IgnoreCase)
                    || !armor.ObjectEffect.IsNull ? TArmorType.Wizard : TArmorType.Cloth;
            return TArmorType.Unknown;
        }

        public static string GetGender(IArmorGetter armor)
        {
            IArmorAddonGetter addon = armor.Armature.FirstOrDefault().Resolve(Settings.Cache);

            if (addon.WorldModel == null) return TArmorGender.Unknown;
            if (addon.WorldModel.Male != null && addon.WorldModel.Female != null)
                return TArmorGender.Common;
            if (addon.WorldModel.Male == null)
                return TArmorGender.Female;
            if (addon.WorldModel.Female == null)
                return TArmorGender.Male;
            return TArmorGender.Unknown;
        }

        public static IEnumerable<TBodySlot> GetBodySlots(IArmorGetter armor)
        {
            IArmorAddonGetter addon = armor.Armature.FirstOrDefault().Resolve(Settings.Cache);
            return GetBodySlots(addon);
        }

        public static IEnumerable<TBodySlot> GetBodySlots(IArmorAddonGetter addon)
        {
            List<TBodySlot> list = new();
            var flags = addon.BodyTemplate.FirstPersonFlags;
            return ArmorSlots.Where(x => flags.HasFlag((BipedObjectFlag)x));
        }

        public static bool IsUpperArmor(IArmorGetter x)
        {
            var addon = x.Armature.FirstOrDefault().Resolve(Settings.Cache);
            return addon.BodyTemplate.FirstPersonFlags.HasFlag(BipedObjectFlag.Body)
            || addon.BodyTemplate.FirstPersonFlags.HasFlag((BipedObjectFlag)TBodySlot.Chest)
            || addon.BodyTemplate.FirstPersonFlags.HasFlag((BipedObjectFlag)TBodySlot.ChestUnder);
        }

        public static bool IsUpperArmor(TBodySlot x)
        {
            return x.Equals(BipedObjectFlag.Body)
            || x.Equals(TBodySlot.Chest)
            || x.Equals(TBodySlot.ChestUnder);
        }

        public static bool IsBodyArmor(IArmorGetter x)
        {
            var addons = x.Armature.EmptyIfNull().Select(x => x.Resolve(Settings.Cache));
            return addons.EmptyIfNull().Any(addon => addon.BodyTemplate.FirstPersonFlags.HasFlag(BipedObjectFlag.Body));
        }

        public static bool IsLowerArmor(IArmorGetter x)
        {
            var addon = x.Armature.FirstOrDefault().Resolve(Settings.Cache);
            return addon.BodyTemplate.FirstPersonFlags.HasFlag((BipedObjectFlag)TBodySlot.Pelvis)
            || addon.BodyTemplate.FirstPersonFlags.HasFlag((BipedObjectFlag)TBodySlot.PelvisUnder);
        }

        public static string GetOutfitArmorType(string outfitEID)
        {
            var m = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.ArmorTypeRegex, outfitEID).ToList();            
            return !m.Any() ? "" : m.First();
        }

        public static Comparer<TArmor> GetArmorComparer(string fullName)
        {
            return Comparer<TArmor>.Create((a, b) =>
            {
                return HelperUtils.GetMatchingWordCount(fullName, a.Name)
                    .CompareTo(HelperUtils.GetMatchingWordCount(fullName, b.Name));
            });
        }

        public static Comparer<IWeaponGetter> GetWeaponComparer(string fullName)
        {
            return Comparer<IWeaponGetter>.Create((a, b) =>
            {
                return HelperUtils.GetMatchingWordCount(fullName, ResolveItemName(a))
                    .CompareTo(HelperUtils.GetMatchingWordCount(fullName, ResolveItemName(b)));
            });
        }

        public static string ResolveItemName(IArmorGetter item) {
            return item.Name == null || item.Name.String.Length < 1 ? item.EditorID : item.Name.ToString();
        }

        public static string ResolveItemName(IWeaponGetter item)
        {
            return item.Name == null || item.Name.String.Length < 1 ? item.EditorID : item.Name.ToString();
        }

        public static void AddArmorsToMannequin(IEnumerable<TArmorSet> armorSets)
        {
            Logger.InfoFormat("Distributing Armor sets to Mannequins...");

            ISkyrimMod patch = FileUtils.GetOrAddPatch(Settings.PatcherSettings.PatcherPrefix + "Mannequins.esp");
            var form = patch.FormLists != null && patch.FormLists.Any()
                ? patch.FormLists.First() : patch.FormLists.AddNew("MannequinsArmorForm");

            armorSets = armorSets.Distinct();
            var lls = armorSets.Select(set => set.CreateLeveledList(patch).AsLink<IItemGetter>());
            form.Items.AddRange(lls);
            Logger.InfoFormat("Distributed Armor sets to Mannequins...\n\n");
        }

    }
}
