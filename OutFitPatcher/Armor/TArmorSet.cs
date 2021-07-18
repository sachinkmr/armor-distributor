using log4net;
using Mutagen.Bethesda;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using OutFitPatcher.Config;
using OutFitPatcher.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks.Dataflow;

namespace OutFitPatcher.Armor
{
    public class TArmorSet
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(TArmorSet));
        public HashSet<TArmor> Armors { get; }
        public HashSet<FormKey> Weapons { get; }

        public TArmor Body { get; }
        public string Material { get; }
        public string Type { get; }
        public string Prefix { get; }

        private FormKey LLFormKey = FormKey.Null;
        private FormKey OutfitFormKey = FormKey.Null;

        private readonly ISkyrimMod? Patch;

        public TArmorSet(TArmor body, bool hasPatch = false)
        {
            Body = body;
            Armors = new();
            Weapons = new();
            Material = body.Material;
            Type = body.Type;
            Prefix = Configuration.Patcher.LeveledListPrefix + Body.Gender + "_" + Body.EditorID;
            Armors.Add(body);
            if (!hasPatch)
                Patch = FileUtils.GetOrAddPatch(Body.FormKey.ModKey.FileName, true);
        }

        public TArmorSet(IArmorGetter body, bool hasPatch = false)
            : this(new TArmor(body))
        {
        }
        public TArmorSet(TArmor body, ISkyrimMod patch)
            : this(body, true)
        {
            Patch = patch;
        }

        public TArmorSet(IArmorGetter body, ISkyrimMod patch)
            : this(new TArmor(body), true)
        {
            Patch = patch;
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

        public void AddWeapon(IWeaponGetter weapon)
        {
            Weapons.Add(weapon.FormKey);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public FormKey CreateLeveledList(bool forceCreate = false)
        {
            return CreateLeveledList(Patch, forceCreate);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public FormKey CreateLeveledList(ISkyrimMod patchMod, bool forceCreate = false)
        {
            LeveledItem ll = null;
            if (forceCreate || LLFormKey == FormKey.Null)
            {
                var items = Armors.Select(a => a.FormKey.AsLink<IItemGetter>())
                    .Union(Weapons.Select(a => a.AsLink<IItemGetter>()).EmptyIfNull());
                ll = OutfitUtils.CreateLeveledList(patchMod, items, Prefix, 1, LeveledItem.Flag.UseAll);
                LLFormKey = ll.FormKey;
            }
            return LLFormKey;
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
            ConcurrentDictionary<string, SortedSet<IWeaponGetter>> map = new();
            ConcurrentDictionary<string, ConcurrentDictionary<string, IWeaponGetter>> matchedMap = new();
            bool matched = false;
            if (!addAll)
            {
                var block = new ActionBlock<IWeaponGetter>(
                   weapon =>
                   {
                       var weaponName = ArmorUtils.ResolveItemName(weapon);
                       if (HelperUtils.GetMatchingWordCount(Body.Name, weaponName) > 0)
                       {
                           matched = true;
                           var type = weapon.Data.AnimationType.ToString();
                           if (!matchedMap.ContainsKey(type)) matchedMap.TryAdd(type,
                              new ConcurrentDictionary<string, IWeaponGetter>());
                           matchedMap.GetValueOrDefault(type)
                            .TryAdd(HelperUtils.GetMatchingWordCount(Body.Name, weaponName).ToString(),weapon);
                       }
                   }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 10 });

                foreach (var weapon in weapons)
                    block.Post(weapon);
                block.Complete();
                block.Completion.Wait();

                var weaps = matchedMap.Values.Select(x => x.OrderBy(k => k.Key).Last().Value);
                this.AddWeapons(weaps);
            }
            else if(addAll || (!matched && bodyCounts < 5)) this.AddWeapons(weapons);
        }

        public void CreateMatchingSetFrom(IEnumerable<IArmorGetter> others, bool addAll = false)
        {
            ConcurrentDictionary<string, ConcurrentDictionary<string, TArmor>> matchedArmors1 = new();
            IEnumerable<TArmor> armorParts = others
                .Where(x => (ArmorUtils.GetMaterial(x).Equals(Material)
                || x.HasKeyword(Skyrim.Keyword.ArmorJewelry))
                && Type.Equals(ArmorUtils.GetArmorType(x)))
                .Select(x => new TArmor(x));
            if (!addAll)
            {
                var block = new ActionBlock<TArmor>(
                   armor =>
                   {
                       var armorName = armor.Name;
                       if (HelperUtils.GetMatchingWordCount(Body.Name, armorName) > 0)
                           armor.BodySlots.Select(x => x.ToString()).ForEach(flag =>
                           {
                               if (!matchedArmors1.ContainsKey(flag)) matchedArmors1.TryAdd(flag,
                               new ConcurrentDictionary<string, TArmor>());
                               matchedArmors1.GetValueOrDefault(flag)
                                .TryAdd(HelperUtils.GetMatchingWordCount(Body.Name, armorName).ToString(), armor);
                   });
               }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 30 });

                foreach (var armor in armorParts)                
                    block.Post(armor);
                block.Complete();
                block.Completion.Wait();

                var armors = matchedArmors1.Values.Select(x => x.OrderBy(k => k.Key).Last().Value);
                this.AddArmors(armors);
                Logger.DebugFormat("Created Armors Set: {0}=> [{1}]", Body.FormKey.ModKey.FileName, 
                    string.Join(", ", Armors.Select(x => x.Name)));
            }
            else this.AddArmors(armorParts);            
        }
    }
}
