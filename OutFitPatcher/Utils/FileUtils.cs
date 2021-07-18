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
        public static ISkyrimMod GetOrAddPatch(string EspName, bool forLVLI = false)
        {
            if (!(EspName.Contains(Patcher.PatcherSuffix) || EspName.Contains(Patcher.PatcherLLSuffix)))
            {
                var suffix = forLVLI ? Patcher.PatcherLLSuffix : Patcher.PatcherSuffix;
                EspName = EspName.Replace(".esp", "") + suffix;
            }

            ModKey modKey = ModKey.FromNameAndExtension(EspName);
            if (Patches.ContainsKey(modKey.FileName))
                return Patches.GetValueOrDefault(modKey.FileName);

            ISkyrimMod patch = new SkyrimMod(modKey, SkyrimRelease.SkyrimSE);
            Patches.TryAdd(modKey.FileName, patch);
            Cache.Add(patch);
            //var x = ModListing<ISkyrimModGetter>.CreateEnabled(patch.ModKey);
            //State.LoadOrder.Add((IModListing<ISkyrimModGetter>)x, State.LoadOrder.Count - 2);

            return patch;
        }
    }
}
