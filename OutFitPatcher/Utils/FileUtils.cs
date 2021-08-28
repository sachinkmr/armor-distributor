using log4net;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary;
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
using static OutFitPatcher.Config.Configuration;


namespace OutFitPatcher.Utils
{
    public static class FileUtils
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(FileUtils));
        public static BinaryWriteParameters SafeBinaryWriteParameters => new()
        {
            MasterFlag = BinaryWriteParameters.MasterFlagOption.ChangeToMatchModKey,
            ModKey = BinaryWriteParameters.ModKeyOption.CorrectToPath,
            RecordCount = BinaryWriteParameters.RecordCountOption.Iterate,
            LightMasterLimit = BinaryWriteParameters.LightMasterLimitOption.ExceptionOnOverflow,
            MastersListContent = BinaryWriteParameters.MastersListContentOption.Iterate,
            FormIDUniqueness = BinaryWriteParameters.FormIDUniquenessOption.Iterate,
            NextFormID = BinaryWriteParameters.NextFormIDOption.Iterate
        };


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
            ModKey modKey = ModKey.FromNameAndExtension(espName);
            if (Patches.ContainsKey(modKey.FileName))
                return Patches.GetValueOrDefault(modKey.FileName);

            ISkyrimMod patch = new SkyrimMod(modKey, SkyrimRelease.SkyrimSE);
            Patches.TryAdd(modKey.FileName, patch);
            Cache.Add(patch);
            
            //var x = ModListing<ISkyrimModGetter>.CreateEnabled(patch.ModKey);
            //State.LoadOrder.Add((IModListing<ISkyrimModGetter>)x, State.LoadOrder.Count - 2);

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
