using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.IO;
using System;
using ArmorDistributor.Utils;
using System.Collections.Generic;
using System.Linq;
using ArmorDistributor.Armor;
using log4net;


namespace ArmorDistributor.Managers
{
    public class SleepingOutfitManager
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(SleepingOutfitManager));

        private ISkyrimMod? Patch;
        private Random Random = new Random();
        readonly IPatcherState<ISkyrimMod, ISkyrimModGetter> State;

        public SleepingOutfitManager(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            State = state;
        }

        public ISkyrimMod Process(ISkyrimMod patch)
        {
            // Validations
            bool hasSleepMod = false;
            hasSleepMod = hasSleepMod || HasMod("Immersive Indoor Attire and Etiquette.esp");
            hasSleepMod = hasSleepMod || HasMod("SleepTight.esp");
            if (!hasSleepMod) return patch;

            Patch = FileUtils.GetIncrementedMod(patch);
            if (!Program.Settings.UserSettings.SleepingOutfit.Any())
            {
                Logger.DebugFormat("No mods for sleeping outfits found, Skipping...");
                return Patch;
            }

            Logger.InfoFormat("\n\nCreating matching armor sets for sleeping outfit mods...");
            var armorsets = new List<IArmorGetter>();
            foreach (var mod in State.LoadOrder.PriorityOrder
                        .Where(x => Program.Settings.UserSettings.SleepingOutfit.Contains(x.ModKey.FileName)
                            && x.Mod.Armors.Count > 0)
                        .Select(x => x.Mod))
            {
                List<IArmorGetter> bodies = new();
                List<TArmor> others = new();
                List<TArmor> jewelries = new();

                mod.Armors
                    .Where(x => x.Keywords != null && x.Armature != null && x.Armature.Any())
                    .ForEach(armor =>
                    {
                        Patch = FileUtils.GetIncrementedMod(Patch);
                        if (ArmorUtils.IsBodyArmor(armor)) bodies.Add(armor);
                        else if (ArmorUtils.IsJewelry(armor)) jewelries.Add(new(armor, "Unknown"));
                        else {
                            var mats = ArmorUtils.GetMaterial(armor);
                            if (mats.Count() > 1 && mats.Contains("Unknown"))
                                mats.Remove("Unknown");

                            mats.Distinct()
                            .ForEach(m => {
                                TArmor ar = new(armor, m);
                                others.Add(ar);
                            });
                        }
                    });

                int bodyCount = bodies.Count;

                var commanName = 0;
                Dictionary<TArmorType, Dictionary<string, List<TArmor>>> armorGroups = new();
                others.GroupBy(x => x.Type).ToDictionary(x => x.Key, x => x.Select(a => a)).ForEach(x => {
                    Dictionary<string, List<TArmor>> d1 = new();
                    x.Value.ForEach(a => d1.GetOrAdd(a.Material).Add(a));
                    armorGroups.Add(x.Key, d1);
                });

                for (int i = 0; i < bodyCount; i++)
                {
                    // Creating armor sets and LLs
                    var body = bodies.ElementAt(i);
                    var bMats = ArmorUtils.GetMaterial(body).Distinct().ToList();
                    if (bMats.Contains("Unknown") && bMats.Count() > 1)
                        bMats.Remove("Unknown");

                    bMats.ForEach(bMat => {
                        var bArmor = new TArmor(body, bMat);
                        TArmorSet armorSet = new(bArmor, bMat);
                        List<TArmor> armors = armorGroups.GetOrAdd(armorSet.Type)
                            .GetOrDefault(bMat).EmptyIfNull()
                            .Union(jewelries).ToList();

                        //if(others.Any() && !armors.Any()) continue;
                        armorSet.CreateMatchingSetFrom(armors, bodyCount == 1, commanName);
                        if (armorSet.Armors.Count() == 1)
                        {
                            armorsets.Add(body);
                            if (i > 0 && (i + 1) % 100 == 0)
                                Logger.InfoFormat("Created {0}/{1} sleeping set for: {2}", i + 1, bodyCount, mod.ModKey.FileName);
                        }
                    });
                }
                Logger.InfoFormat("Created {0}/{0} sleeping armor-set for: {1}", bodyCount, mod.ModKey.FileName);
            }
            Logger.InfoFormat("Created {0} matching sleeping armor sets from armor mods...\n", armorsets.Count());

            AddToFormList(armorsets, "_IIA_SUB_Robes", "Immersive Indoor Attire and Etiquette.esp");
            AddToFormList(armorsets, "_SLPRobesList", "SleepTight.esp");
            return Patch;
        }

        private bool HasMod(string modFile) {
            if (!File.Exists(Path.Combine(State.DataFolderPath, modFile)))
            {
                Logger.DebugFormat("Mod \"{0}\" not found...", modFile);
                return false;
            }
            return true;
        }

        private void AddToFormList(List<IArmorGetter> armorsets, string eid, string modFile)
        {
            Patch = FileUtils.GetIncrementedMod(Patch);
            var formlist = State.LoadOrder.PriorityOrder
                    .WinningOverrides<IFormListGetter>()
                    .Where(x => x.FormKey.ToString().Contains(modFile) && x.EditorID.Equals(eid));
            if (!formlist.Any()) return;

            var record = Patch.FormLists.GetOrAddAsOverride(formlist.First());
            AssignMissingMesh(armorsets, record);

            record.Items.Clear();
            record.Items.AddRange(armorsets.Select(x => x.AsLinkGetter()));
        }

        private void AssignMissingMesh(List<IArmorGetter> armorsets, FormList record)
        {
            var cache = State.LinkCache;
            var meshes = record.Items
                            .Select(x => cache.Resolve<IArmorGetter>(x.FormKey))
                            .Select(x=> x.Armature.FirstOrDefault().Resolve(cache).WorldModel.Male.File);
            armorsets.Select(x => cache.Resolve<IArmorGetter>(x.FormKey))
                .Select(x => x.Armature.FirstOrDefault().Resolve(cache))
                .ForEach(a=> {
                    if (a.WorldModel.Male == null)
                    {
                        int i = Random.Next(meshes.Count());
                        var addon = Patch.ArmorAddons.GetOrAddAsOverride(a);
                        addon.WorldModel.Male = new();
                        addon.WorldModel.Male.File = meshes.ElementAt(i);
                    }
                });
        }
    }
}
