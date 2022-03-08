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

        public static string GetName(IArmorGetter armor)
        {
            return armor.Name == null || armor.Name.String.IsNullOrEmpty()
                ? HelperUtils.SplitString(armor.EditorID) : armor.Name.ToString();
        }

        public static bool IsMissingMatchingArmor(ILinkCache cache, IArmorGetter armor) {
            if(!IsEligibleForMeshMapping(armor))  return false;
            var addon = armor.Armature.FirstOrDefault().Resolve<IArmorAddonGetter>(cache);
            return addon.WorldModel == null || addon.WorldModel.Male == null || addon.WorldModel.Female == null;
        }

        private static string GetClothingType(IArmorGetter armor, bool armorType=true)
        {
            // Matching Clothing and Robes types
            string name = GetFullName(armor);
            var matches = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, name);

            if (!matches.Any())
                matches = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.ArmorTypeRegex, name);
            if (matches.Contains(TArmorType.Wizard.ToString()))
                return TArmorType.Wizard.ToString();

            if (matches.Contains(TArmorType.Cloth.ToString()))
                return TArmorType.Cloth.ToString();
            Logger.DebugFormat("Unknown Clothing Type: [{0}][{1}]", armor.FormKey, name);
            return TArmorType.Unknown.ToString();
        }

        private static string GetArmorMaterial(IArmorGetter armor)
        {
            Regex mRegex = Settings.PatcherSettings.ArmorMaterialRegex;
            ILinkCache cache = Program.Settings.Cache;
            string fullNmae = "";
            armor.Keywords.EmptyIfNull().Where(x=> !x.IsNull)
                .Select(x => cache.Resolve<IKeywordGetter>(x.FormKey).EditorID)
                .ForEach(x => fullNmae += x);

            fullNmae += GetFullName(armor);
            var matches = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, fullNmae);
            if(matches.Any())
                return matches.First();
            Logger.DebugFormat("Unknown Material Type: [{0}][{1}]", armor.FormKey, fullNmae);
            return TArmorType.Unknown.ToString();
        }

        public static string GetMaterial(IArmorGetter item)
        {
            // Checking for material first
            ILinkCache cache = Program.Settings.Cache;
            string fullName = "";
            item.Keywords.EmptyIfNull().Where(x => !x.IsNull)
                .Select(x => cache.Resolve<IKeywordGetter>(x.FormKey).EditorID)
                .ForEach(x => fullName += x);

            fullName += GetFullName(item);
            var matches = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, fullName);
            if (matches.Any())
                return matches.First();

            if (!matches.Any())
                matches = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.ArmorTypeRegex, fullName);
            
            if (matches.Contains(TArmorType.Wizard.ToString()))
                return TArmorType.Wizard.ToString();

            if (matches.Contains(TArmorType.Cloth.ToString()))
                return TArmorType.Cloth.ToString();

            Logger.DebugFormat("Unknown Armor/Clothing Type: [{0}][{1}]", item.FormKey, fullName);
            return TArmorType.Unknown.ToString();
        }

        public static bool IsCloth(IArmorGetter item)
        {
            return item.HasKeyword(Skyrim.Keyword.ArmorClothing)
                || item.BodyTemplate.ArmorType.Equals(ArmorType.Clothing);
        }

        public static bool IsValidArmor(IArmorGetter armor)
        {
            var name = GetFullName(armor);
            return Regex.IsMatch(name, Settings.PatcherSettings.ValidArmorsRegex, RegexOptions.IgnoreCase)
                    || !Regex.IsMatch(name, Settings.PatcherSettings.InvalidArmorsRegex, RegexOptions.IgnoreCase);
        }

        public static TArmorType GetArmorType(IArmorGetter armor)
        {
            if (IsCloth(armor))
                return Regex.IsMatch(armor.EditorID, Settings.PatcherSettings.ArmorTypeRegex["Wizard"], RegexOptions.IgnoreCase)
                    ? TArmorType.Wizard : TArmorType.Cloth;
            return armor.BodyTemplate.ArmorType == ArmorType.HeavyArmor ? TArmorType.Heavy : TArmorType.Light;
        }

        public static TGender GetGender(IArmorGetter armor)
        {
            if (armor.Armature != null && armor.Armature.Count > 0 && Program.Settings.Cache.TryResolve<IArmorAddonGetter>(armor.Armature.FirstOrDefault().FormKey, out var addon)) {
                if (addon.WorldModel == null) return TGender.Unknown;
                if (addon.WorldModel.Male != null && addon.WorldModel.Female != null)
                    return TGender.Common;
                if (addon.WorldModel.Male == null)
                    return TGender.Female;
                if (addon.WorldModel.Female == null)
                    return TGender.Male;
            }
            return TGender.Unknown;
        }

        public static IEnumerable<TBodySlot> GetBodySlots(IArmorGetter armor)
        {
            if (armor.Armature.FirstOrDefault().TryResolve<IArmorAddonGetter>(Program.Settings.Cache, out var addon)) {
                return GetBodySlots(addon);
            }
            return Enumerable.Empty<TBodySlot>();
        }

        public static IEnumerable<TBodySlot> GetBodySlots(IArmorAddonGetter addon)
        {
            var flags = addon.BodyTemplate.FirstPersonFlags;
            return ArmorSlots.Where(x => flags.HasFlag((BipedObjectFlag)x));
        }

        public static bool IsUpperArmor(IArmorGetter x)
        {
            var addons = x.Armature.EmptyIfNull().Select(x => x.Resolve(Program.Settings.Cache));
            return addons.EmptyIfNull().Any(addon => addon.BodyTemplate.FirstPersonFlags.HasFlag(BipedObjectFlag.Body)
                || addon.BodyTemplate.FirstPersonFlags.HasFlag((BipedObjectFlag)TBodySlot.Chest)
                || addon.BodyTemplate.FirstPersonFlags.HasFlag((BipedObjectFlag)TBodySlot.ChestUnder));
        }

        public static bool IsUpperArmor(TBodySlot x)
        {
            return x.Equals(BipedObjectFlag.Body)
            || x.Equals(TBodySlot.Chest)
            || x.Equals(TBodySlot.ChestUnder);
        }

        public static bool IsBodyArmor(IArmorGetter x)
        {
            var slots = GetBodySlots(x);
            var status =  slots.Contains(TBodySlot.Body) 
            && !(slots.Contains(TBodySlot.Back)
             || slots.Contains(TBodySlot.Decapitate)
             || slots.Contains(TBodySlot.DecapitateHead));
            return status;
        }

        public static bool IsLowerArmor(IArmorGetter x)
        {
            var addons = x.Armature.EmptyIfNull().Select(x => x.Resolve(Program.Settings.Cache));
            return addons.EmptyIfNull().Any(addon => addon.BodyTemplate.FirstPersonFlags.HasFlag((BipedObjectFlag)TBodySlot.Pelvis)
            || addon.BodyTemplate.FirstPersonFlags.HasFlag((BipedObjectFlag)TBodySlot.PelvisUnder));
        }

        public static bool IsJewelry(IArmorGetter x) {
            return x.HasKeyword(Skyrim.Keyword.ArmorJewelry)
                || x.HasKeyword(Skyrim.Keyword.ClothingNecklace);
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

        public static string GetFullName(IArmorGetter item)
        {
            var words = HelperUtils.SplitString(item.EditorID + (item.Name == null ? "" : item.Name.String)).Split(' ');
            return string.Join(" ", new HashSet<string>(words));
        }

        public static string GetFullName(IWeaponGetter item)
        {
            var words = HelperUtils.SplitString(item.EditorID + (item.Name == null ? "" : item.Name.String)).Split(' ');
            return string.Join(" ", new HashSet<string>(words));
        }


        public static string ResolveItemName(IArmorGetter item) {
            return item.Name == null || item.Name.String.Length < 1 ? item.EditorID : item.Name.ToString();
        }

        public static string ResolveItemName(IWeaponGetter item)
        {
            return item.Name == null || item.Name.String.Length < 1 ? item.EditorID : item.Name.ToString();
        }

        public static bool IsEligibleForMeshMapping(IArmorGetter armor)
        {
            return IsValidArmor(armor)
                    && !Regex.IsMatch(GetFullName(armor), "Shield", RegexOptions.IgnoreCase)
                    && armor.Armature != null
                    && armor.Armature.Count > 0
                    && !armor.HasKeyword(Skyrim.Keyword.ArmorShield)
                    && !GetBodySlots(armor).Contains(TBodySlot.Shield);
        }

        //public static void AddArmorsToMannequin(IEnumerable<TArmorSet> armorSets)
        //{
        //    Logger.InfoFormat("Distributing Armor sets to Mannequins...");

        //    ISkyrimMod patch = FileUtils.GetOrAddPatch(Settings.PatcherSettings.PatcherPrefix + "Mannequins.esp");
        //    var form = patch.FormLists != null && patch.FormLists.Any()
        //        ? patch.FormLists.First() : patch.FormLists.AddNew("MannequinsArmorForm");

        //    armorSets = armorSets.Distinct();
        //    var lls = armorSets.Select(set => set.CreateLeveledList(patch).AsLink<IItemGetter>());
        //    form.Items.AddRange(lls);
        //    Logger.InfoFormat("Distributed Armor sets to Mannequins...\n\n");
        //}

    }
}
