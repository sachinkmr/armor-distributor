using System.Collections.Generic;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.IO;
using System.Text.RegularExpressions;
using System;
using OutFitPatcher.Utils;
using log4net;
using OutFitPatcher.Config;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;

namespace OutFitPatcher.Managers
{
    public class JewelaryManager
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(JewelaryManager));

        public static void ProcessAndDistributeJewelary(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            if (!Settings.UserSettings.JewelryMods.Any()) return;
            Logger.InfoFormat("Creating Leveled list for Jewelary.....");
            ISkyrimMod patchedMod = FileUtils.GetOrAddPatch(Settings.PatcherSettings.PatcherPrefix+"Jewelry.esp");
            Dictionary<string, HashSet<IArmorGetter>> jewelleries = new();

            // Adding all the patches to load order
            foreach (IModListing<ISkyrimModGetter> modlist in state.LoadOrder.PriorityOrder
                .Where<IModListing<ISkyrimModGetter>>(x => Settings.UserSettings.JewelryMods.Contains(x.ModKey.FileName)
                && x.Mod.Armors.Count > 0))
            {
                // Getting Jewelary Armors
                ISkyrimModGetter mod = modlist.Mod;
                IEnumerable<IArmorGetter> armors = mod.Armors
                    .Where(x => ArmorUtils.IsValidArmor(x)
                        && x.Name != null);

                for (int i = 0; i < armors.Count(); i++)
                {
                    IArmorGetter armor = armors.ElementAtOrDefault(i);
                    IArmorAddonGetter addon = armor.Armature.FirstOrDefault().Resolve(Settings.State.LinkCache);

                    string gender = (addon.WorldModel.Male != null && addon.WorldModel.Female != null
                                    ? "_C_" : addon.WorldModel.Male == null ? "_F_" : "_M_");

                    var bodyFlags = armor.BodyTemplate.FirstPersonFlags;
                    var key = bodyFlags.ToString() + gender;
                    if (!jewelleries.ContainsKey(key)) jewelleries.Add(key, new HashSet<IArmorGetter>());
                    jewelleries.GetValueOrDefault(key).Add(armor);
                }
            }

            // Creating leveled list for the jewelleries
            string prefix = Settings.PatcherSettings.LeveledListPrefix + "_LL_Jewels_";
            jewelleries.Where(x => !Regex.Match(x.Key.ToString(), "Decapitate", RegexOptions.IgnoreCase).Success)
                .ForEach(j =>
                {
                    string lvli_eid = prefix + j.Key.ToString().Replace(" ", "_");
                    OutfitUtils.CreateLeveledList(patchedMod, j.Value, lvli_eid, 1, LeveledItem.Flag.CalculateForEachItemInCount);
                });
            Logger.InfoFormat("Leveled List created for Jewelary....");

            // Writing patched mod to disk
            DistributeJewelaryUsingSPID(state, patchedMod);
            Logger.InfoFormat("Distribution of Jewelaries is completed...\n\n");
        }

        private static void DistributeJewelaryUsingSPID(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, ISkyrimMod mod)
        {
            Logger.InfoFormat("Distributing Jewelary using Spell Perk Item Distributor.... ");
            string iniName = mod.ModKey.Name + "_DISTR.ini";
            List<string> lines = new();
            Random random = new();
            

            // Distributing jewelry
            string jPrefix = Settings.PatcherSettings.LeveledListPrefix + "_LL_Jewels_";
            foreach (ILeveledItemGetter ll in mod.LeveledItems
                .Where(x => x.EditorID.Contains(jPrefix)))
            {
                string eid = ll.EditorID;
                string gender = Settings.UserSettings.JewelryForMales 
                    && Regex.Match(eid, "Amulet|Ring|Circlet", RegexOptions.IgnoreCase).Success
                    && !eid.Contains("_F_") ? "NONE" : "F";
                string line = "Item = 0x00" +
                    ll.FormKey.ToString().Replace(":", " - ") +
                    " | ActorTypeNPC " +
                    " | Citizen " +
                    " | NONE " +
                    " | " + gender +
                    " | 1 " +
                    " | " + random.Next(50, 99);
                lines.Add(line);
            }
            File.WriteAllLines(Path.Combine(state.DataFolderPath, iniName), lines);
        }
    }
}