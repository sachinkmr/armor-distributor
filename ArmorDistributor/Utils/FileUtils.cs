using ArmorDistributor.Config;
using log4net;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using static ArmorDistributor.Config.Settings;


namespace ArmorDistributor.Utils
{
    public static class FileUtils
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(FileUtils));
        public static BinaryWriteParameters SafeBinaryWriteParameters => new()
        {
            MasterFlag = MasterFlagOption.ChangeToMatchModKey,
            ModKey = ModKeyOption.CorrectToPath,
            RecordCount = RecordCountOption.Iterate,
            LightMasterLimit = LightMasterLimitOption.ExceptionOnOverflow,
            MastersListContent = MastersListContentOption.Iterate,
            FormIDUniqueness = FormIDUniquenessOption.Iterate,
            NextFormID = NextFormIDOption.Iterate,
            CleanNulls = true,
            MastersListOrdering = MastersListOrderingByLoadOrder.Factory(Settings.State.LoadOrder.ListedOrder.Select(x=>x.ModKey))
        };

        public static ISkyrimMod GetIncrementedMod(ISkyrimMod mod)
        {
            if (mod.EnumerateMajorRecords().Where(x => x.FormKey.ModKey.Equals(mod.ModKey)).Count() < 2040)
                return mod;

            var name = "";
            try
            {
                var indx = Int32.Parse(mod.ModKey.Name.Last().ToString());
                name = mod.ModKey.Name.Replace(indx.ToString(), (indx + 1).ToString());
            }
            catch
            {
                name = mod.ModKey.Name + " 1";
            }
            return FileUtils.GetOrAddPatch(name);
        }

        public static T ReadJson<T>(string filePath)
        {
            using StreamReader r = new(filePath);
            string json = r.ReadToEnd();
            return JsonConvert.DeserializeObject<T>(json);
        }

        public static void WriteJson(string filePath, Object classInfo)
        {
            using StreamWriter r = new(filePath);
            r.Write(JsonConvert.SerializeObject(classInfo, Formatting.Indented));
            r.Flush();
        }

        public static void SaveMod(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, ISkyrimMod patch)
        {
            var patchFile = patch.ModKey.FileName;

            var records = patch.EnumerateMajorRecords().Where(r=> r.FormKey.ModKey.Equals(patch.ModKey));
            if (records.Count() < 2048)
                patch.ModHeader.Flags = SkyrimModHeader.HeaderFlag.LightMaster;
            string location = Path.Combine(state.DataFolderPath, patchFile);
            patch.WriteToBinary(location, FileUtils.SafeBinaryWriteParameters);
            Logger.InfoFormat("Saved Patch: {0} ", patchFile);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static ISkyrimMod GetOrAddPatch(string espName)
        {
            espName = espName.EndsWith(".esp") ? espName : espName + ".esp";
            espName = espName.StartsWith(Settings.PatcherSettings.PatcherPrefix) ? espName : Settings.PatcherSettings.PatcherPrefix + espName;
            ModKey modKey = ModKey.FromNameAndExtension(espName);

            if (State.LoadOrder.HasMod(modKey))
                return Patches.Find(x=>x.ModKey.Equals(modKey));

            ISkyrimMod patch = new SkyrimMod(modKey, SkyrimRelease.SkyrimSE);
            Patches.Add(patch);

            var listing = new ModListing<ISkyrimModGetter>(patch, true);
            var lastListing = State.LoadOrder[State.PatchMod.ModKey];
            State.LoadOrder.RemoveKey(State.PatchMod.ModKey);
            State.LoadOrder.Add(listing);
            State.LoadOrder.Add(lastListing);
            return patch;
        }

        public static void CopyDirectory(string sourcePath, string targetPath)
        {
            //Now Create all of the directories
            Directory.CreateDirectory(targetPath);
            foreach (string dirPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories)) {
                Directory.CreateDirectory(dirPath.Replace(sourcePath, targetPath));
            }

            //Copy all the files & Replaces any files with the same name
            foreach (string newPath in Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories))
                File.Copy(newPath, newPath.Replace(sourcePath, targetPath), true);
        }
    }
}
