using log4net;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using ArmorDistributor.Config;
using ArmorDistributor.Utils;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace ArmorDistributor.Armor
{
    public class TArmorSet
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(TArmorSet));
        public HashSet<TArmor> Armors { get; }
        public HashSet<FormKey> Weapons { get; }
        public TArmor Body { get; }
        public TGender Gender { get; }
        public string Material { get; }
        public TArmorType Type { get; }
        public string Prefix { get; }
        public string LoadOrder { get; set; }

        public bool hasShield;
        public bool hasHalmet;

        public FormKey LLFormKey = FormKey.Null;

        public TArmorSet(TArmor body, string material)
        {
            Body = body;
            Armors = new();
            Weapons = new();
            Material = material;
            Type = body.Type;
            Gender = body.Gender;
            Prefix = Settings.PatcherSettings.LeveledListPrefix + Body.Gender + "_" + Material + "_"+ Body.EditorID;
            LoadOrder = "";
            Armors.Add(body);
        }
      
        public void AddArmor(TArmor armor)
        {
            Armors.Add(armor);
        }

        public void AddArmors(IEnumerable<TArmor> armors)
        {
            armors.ForEach(a => Armors.Add(a));
        }

        public void AddWeapons(IEnumerable<IWeaponGetter> weapons)
        {
            weapons.ForEach(a => Weapons.Add(a.FormKey));
        }

        public void AddWeapons(IEnumerable<TWeapon> weapons)
        {
            weapons.ForEach(a => Weapons.Add(a.FormKey));
        }

        public void AddWeapon(IWeaponGetter weapon)
        {
            Weapons.Add(weapon.FormKey);
        }

        public ISkyrimMod CreateLeveledList(ISkyrimMod Patch)
        {
            LeveledItem ll = null;
            Patch = FileUtils.GetIncrementedMod(Patch);
            var items = Armors.Select(a => a.FormKey.AsLink<IItemGetter>())
                    .Union(Weapons.Select(a => a.AsLink<IItemGetter>()).EmptyIfNull());
            ll = OutfitUtils.CreateLeveledList(Patch, items, Prefix, 1, LeveledItem.Flag.UseAll);
            LLFormKey = ll.FormKey;
            return Patch;
        }

        public override string ToString()
        {
            return string.Format("{0}:{1}:{2}:{3}:{4}", Body.Name, Material, Type, Gender, Armors.Count());
        }

        public override bool Equals(object? obj)
        {
            return obj is TArmorSet set &&
                   Prefix == set.Prefix;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Prefix);
        }

        public void CreateMatchingSetFrom(IEnumerable<IWeaponGetter> weapons, int bodyCounts, bool addAll = false)
        {
            CreateMatchingSetFrom(weapons.Select(w => new TWeapon(w)).ToHashSet(), bodyCounts, addAll);
        }

        public void CreateMatchingSetFrom(HashSet<TWeapon> weapons, int bodyCounts, bool addAll = false)
        {
            Dictionary<string, Dictionary<int, TWeapon>> matchedMap = new();
            bool matched = false;
            if (!addAll)
            {
                foreach (var weapon in weapons)
                {
                    if (HelperUtils.GetMatchingWordCount(Body.Name, weapon.Name, false) > 0)
                    {
                        matched = true;
                        matchedMap.GetOrAdd(weapon.Type)
                         .GetOrAdd(HelperUtils.GetMatchingWordCount(Body.Name, weapon.Name, false), () => weapon);
                    }
                }
                var weaps = matchedMap.Values.Select(x => x.OrderBy(k => k.Key).Last().Value);
                this.AddWeapons(weaps);
            }
            else if (addAll || (!matched && bodyCounts < 5)) this.AddWeapons(weapons);
        }

        public void CreateMatchingSetFrom(IEnumerable<TArmor> others, bool addAll, int commonName)
        {
            if (!addAll)
            {
                Dictionary<TBodySlot, Dictionary<int, TArmor>> armors = new();
                if (!others.Any())
                {
                    Logger.DebugFormat("No matching armor found for {0}: {1}", Body.EditorID, Body.FormKey);
                    return;
                }
                var bname = Body.Name;
                foreach (var a in others) {
                    // Name based matching
                    var aname = a.Name;
                    int c = HelperUtils.GetMatchingWordCount(bname, aname, false) - commonName;
                    if (c > 0) a.BodySlots.ForEach(flag => armors.GetOrAdd(flag).TryAdd(c, a));
                }

                var marmors = armors.Values.Select(x => x.OrderBy(k => k.Key).Last().Value).Distinct();
                this.AddArmors(marmors);
            }
            else this.AddArmors(others);
        }
    }
}
