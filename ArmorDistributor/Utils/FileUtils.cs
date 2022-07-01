using ArmorDistributor.Config;
using ArmorDistributor.Converters;
using log4net;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Newtonsoft.Json;
using Noggog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

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
            MastersListOrdering = MastersListOrderingByLoadOrder.Factory(Program.Settings.State.LoadOrder.ListedOrder.Select(x=>x.ModKey))
        };

        public static ISkyrimMod GetIncrementedMod(ISkyrimMod mod, bool forceCreate=false)
        {
            if (!Program.Settings.UserSettings.CreateESLs || (!forceCreate && GetMasters(mod).Count() < 250 && CanESLify(mod)))
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
            return GetOrAddPatch(name);
        }

        public static List<string> GetMasters(ISkyrimMod mod) {
            return mod.EnumerateMajorRecords()
                .Where(x => !x.FormKey.ModKey.Equals(mod.ModKey))
                .Select(x=> x.FormKey.ModKey.FileName.String)
                .Distinct()
                .ToList();
        }

        public static bool CanESLify(ISkyrimMod mod, int count = 2000) {
            return mod.EnumerateMajorRecords().Where(x => x.FormKey.ModKey.Equals(mod.ModKey)).Count() < count;
        }

        public static T ReadJson<T>(string filePath)
        {
            using StreamReader r = new(filePath);
            string json = r.ReadToEnd();
            return JsonConvert.DeserializeObject<T>(json);
        }

        public static void WriteJson(string filePath, Object classInfo)
        {
            JsonConverter[] converters = new JsonConverter[] {
                new FormKeyConverter(),
                new BodySlotConverter(),
                new DictionaryConverter()
            };
            //File.SetAttributes(filePath, FileAttributes.Normal);
            using (StreamWriter r = File.CreateText(filePath)) {
                r.Write(JsonConvert.SerializeObject(classInfo, Formatting.Indented, converters));
                r.Flush();
            }            
        }

        public static void SaveMod(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, ISkyrimMod patch)
        {
            var patchFile = patch.ModKey.FileName;
            var records = patch.EnumerateMajorRecords().Where(r=> r.FormKey.ModKey.Equals(patch.ModKey));
            if (CanESLify(patch, 2047))
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

            if (Program.Settings.State.LoadOrder.HasMod(modKey))
                return Program.Settings.Patches.Find(x=>x.ModKey.Equals(modKey));

            ISkyrimMod patch = new SkyrimMod(modKey, SkyrimRelease.SkyrimSE);
            Program.Settings.Patches.Add(patch);

            var listing = new ModListing<ISkyrimModGetter>(patch, true);
            Program.Settings.State.LoadOrder.Add(listing);
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

        public static List<string> GetSPIDKeywords(string path)
        {
            HashSet<string> lines = new();
            HashSet<string> keywrds = new();
            var regex = @"^Keyword\s*=\s*(.+?)\|";
            string[] filePaths = Directory.GetFiles(path, "*_DISTR.ini");
            foreach (string f in filePaths) {
                File.ReadAllLines(f)
                    .ForEach(l =>
                    {
                        var m = Regex.Match(l.Trim(), regex);
                        if (m.Success) keywrds.Add(m.Groups[1].Value);
                    });
                
                //lines.UnionWith(ls);    
            }
            return keywrds.ToList(); 
        }
    }
}
