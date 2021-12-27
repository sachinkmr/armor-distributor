using log4net;
using Mutagen.Bethesda;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using ArmorDistributor.Config;
using ArmorDistributor.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks.Dataflow;
using Mutagen.Bethesda.Strings;

namespace ArmorDistributor.Armor
{
    public class TArmorSet
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(TArmorSet));
        public HashSet<TArmor> Armors { get; }
        public HashSet<FormKey> Weapons { get; }

        public TArmor Body { get; }
        public string Gender { get; }
        public string Material { get; }
        public string Type { get; }
        public string Prefix { get; }

        public bool hasShield;
        public bool hasHalmet;

        private FormKey LLFormKey = FormKey.Null;

        private readonly ISkyrimMod? Patch;

        public TArmorSet(TArmor body, bool hasPatch = false)
        {
            Body = body;
            Armors = new();
            Weapons = new();
            Material = body.Material;
            Type = body.Type;
            Gender = body.Gender;
            Prefix = Settings.PatcherSettings.LeveledListPrefix + Body.Gender + "_" + Body.EditorID;
            Armors.Add(body);
            if (!hasPatch)
                Patch = FileUtils.GetOrAddPatch(Body.FormKey.ModKey.FileName+" - LVLI.esp");
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

        public void CreateMatchingSetFrom(IEnumerable<IArmorGetter> others, bool addAll, int commonStrCount, int commanEID)
        {
            CreateMatchingSetFrom(others.Select(x => new TArmor(x)), addAll, commonStrCount, commanEID);
        }

        public void CreateMatchingSetFrom(IEnumerable<TArmor> others, bool addAll, int commonName, int commanEID)
        {
            if (!addAll)
            {
                ConcurrentDictionary<TBodySlot, ConcurrentDictionary<int, TArmor>> armors = new();
                if (!others.Any())
                {
                    Logger.DebugFormat("No matching armor found for {0}: {1}", Body.EditorID, Body.FormKey);
                    return;
                }
                var bname = Body.Name;
                foreach (var a in others) {
                    // Name based matching
                    var aname = a.Name;
                    int c = HelperUtils.GetMatchingWordCount(bname, aname) - commonName;
                    if (c > 0) a.BodySlots.ForEach(flag => armors.GetOrAdd(flag).TryAdd(c, a));
                }

                if (!armors.Any()) {
                    var beid = HelperUtils.SplitString(Body.EditorID);
                    foreach (var a in others)
                    {
                        // EditorID based matching
                        var aeid = HelperUtils.SplitString(a.EditorID);
                        int d = HelperUtils.GetMatchingWordCount(beid, aeid)- commanEID;
                        if (d > 0) a.BodySlots.ForEach(flag => armors.GetOrAdd(flag).TryAdd(d, a));
                    }
                }
                var marmors = armors.Values.Select(x => x.OrderBy(k => k.Key).Last().Value).Distinct();
                this.AddArmors(marmors);
                Logger.DebugFormat("Created Armors Set: {0}=> [{1}]", Body.FormKey.ToString(),
                    string.Join(", ", Armors.Select(x => x.Name)));
            }
            else this.AddArmors(others);
        }
    }
}
