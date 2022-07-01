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

        public static List<string> GetClothingMaterial(IArmorGetter armor, string name)
        {
            // Matching Clothing and Robes types
            List<string> results = new();
            name = name.IsNullOrEmpty()? GetFullName(armor):name;
            var matches = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, name);

            if (matches.Any()) results.AddRange(matches);

            if (!matches.Any())
                matches = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.ArmorTypeRegex, name);

            if (matches.Contains(TArmorType.Wizard.ToString()))
                results.Add("Mage");

            if (results.Count() > 1 && results.Contains("Mage") && results.Contains("Citizen") && (armor.ObjectEffect == null))
                results.Remove("Mage");
                
            if (!results.Any()) {
                Logger.DebugFormat("Unknown Clothing Type: [{0}][{1}]", armor.FormKey, name);
                results.Add(TArmorType.Unknown.ToString());
            }
            return results.Distinct().ToList();
        }

        public static List<string> GetArmorMaterial(IArmorGetter item, string name)
        {
            List<string> results = new();
            string mRegex = @"(?:Armor|Weap(?:on))Materi[ae]l(\w+)";
            ILinkCache cache = Program.Settings.Cache;
            name = name.IsNullOrEmpty() ? GetFullName(item) : name;
            item.Keywords.EmptyIfNull().Where(x => !x.IsNull)
                .Select(x => cache.Resolve<IKeywordGetter>(x.FormKey).EditorID)
                .ForEach(x =>
                {
                    var match = Regex.Match(x, mRegex, RegexOptions.IgnoreCase);                    
                    if (match.Success)
                    {
                        var val = match.Groups.Values.Last().Value;
                        var cats = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, val);
                        results.AddRange(cats);
                    }
                });

            var armorType = GetArmorType(item);
            var matches = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, name)
                .Where(m=> !HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitTypeRegex, m).Contains("Cloth"));
            if (matches.Any()) {
                if (armorType == TArmorType.Heavy)
                    results.AddRange(matches.Where(m => !HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitTypeRegex, m).Contains("Wizard")));
                else
                    results.AddRange(matches);
            }

            if (!results.Any())
            {
                results.Add(TArmorType.Unknown.ToString());
                Logger.DebugFormat("Unknown Armor/Clothing Type: [{0}][{1}]", item.FormKey, name);
            }
            return results.Distinct().ToList();
        }

        public static List<string> GetMaterial(IArmorGetter item)
        {
            // Checking for material first
            string fullName = GetFullName(item);
            return IsCloth(item) ? GetClothingMaterial(item, fullName)
                : GetArmorMaterial(item, fullName);
        }

        public static bool IsCloth(IArmorGetter item)
        {
            return item.HasKeyword(Skyrim.Keyword.ArmorClothing)
                || item.BodyTemplate.ArmorType.Equals(ArmorType.Clothing);
        }

        public static bool IsValidArmor(IArmorGetter armor)
        {
            var name = GetFullName(armor);
            bool isSlutty = Program.Settings.UserSettings.SkipSluttyOutfit && Regex.IsMatch(name, Settings.PatcherSettings.SluttyRegex, RegexOptions.IgnoreCase);
            return !isSlutty && (Regex.IsMatch(name, Settings.PatcherSettings.ValidArmorsRegex, RegexOptions.IgnoreCase)
                    || !Regex.IsMatch(name, Settings.PatcherSettings.InvalidArmorsRegex, RegexOptions.IgnoreCase));
        }

        public static bool IsValidMaterial(string name)
        {
            return (Regex.IsMatch(name, Settings.PatcherSettings.ValidMaterial, RegexOptions.IgnoreCase)
                    || !Regex.IsMatch(name, Settings.PatcherSettings.InvalidMaterial, RegexOptions.IgnoreCase));
        }

        public static TArmorType GetArmorType(IArmorGetter armor)        {
            if (IsCloth(armor))
                return Regex.IsMatch(armor.EditorID, Settings.PatcherSettings.ArmorTypeRegex["Wizard"], RegexOptions.IgnoreCase)
                    ? TArmorType.Wizard : TArmorType.Cloth;
            return armor.BodyTemplate.ArmorType == ArmorType.HeavyArmor ? TArmorType.Heavy : TArmorType.Light;
        }

        public static TGender GetGender(IArmorGetter armor)
        {
            if (armor.Armature != null && armor.Armature.Count > 0 
                && Program.Settings.Cache.TryResolve<IArmorAddonGetter>(armor.Armature.FirstOrDefault().FormKey, out var addon))
            {
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
            var flags = armor.BodyTemplate.FirstPersonFlags;
            return ArmorSlots.Where(x => flags.HasFlag((BipedObjectFlag)x));
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
            var words = HelperUtils.SplitString(item.EditorID +" " +(item.Name == null ? "" : item.Name.String)).Split(' ');
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
