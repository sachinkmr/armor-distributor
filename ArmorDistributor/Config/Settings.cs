using log4net;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using ArmorDistributor.Utils;
using System.Collections.Generic;
using System.IO;
using Mutagen.Bethesda.Synthesis.Settings;
using Mutagen.Bethesda.WPF.Reflection.Attributes;
using System;
using System.Reflection;
using log4net.Config;

namespace ArmorDistributor.Config
{
    public class Settings
    {
        [Ignore]
        private static readonly ILog Logger = LogManager.GetLogger(typeof(Settings));
        
        [Ignore]
        public static readonly string EXE_LOC;

        [Ignore]
        public static PatcherSettings PatcherSettings;

        //[Ignore]
        //public static UserSettings DefaultUserSettings;

        [SynthesisOrder]
        [JsonDiskName("UserSettings")]
        [SettingName("Patcher Settings: ")]
        public UserSettings UserSettings = new();

        // Properties
        [Ignore]
        public IPatcherState<ISkyrimMod, ISkyrimModGetter>? State;

        [Ignore]
        public ILinkCache<ISkyrimMod, ISkyrimModGetter>? Cache;
        
        [Ignore]
        internal LeveledItem.Flag LeveledListFlag;
        
        [Ignore]
        internal LeveledNpc.Flag LeveledNpcFlag;

        [Ignore]
        internal List<ISkyrimMod> Patches = new();        

        [Ignore]
        public string? IniName;

        [Ignore]
        public static string LogsDirectory;

        static Settings() {
            // Init logger
            var logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
            XmlConfigurator.Configure(logRepository, new FileInfo("log4net.config"));

            EXE_LOC = Directory.GetParent(System.Reflection.Assembly.GetAssembly(typeof(Settings)).Location).FullName;
            string ConfigFile = Path.Combine(EXE_LOC, "data", "config", "PatcherSettings.json");
            PatcherSettings = FileUtils.ReadJson<PatcherSettings>(ConfigFile);
        }

        internal void Init(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            State = state;
            Cache = State.LinkCache;

            // Logs
            LogsDirectory = Path.Combine(State.DataFolderPath, "ArmorDistributionLogs", DateTime.Now.ToString("F").Replace(":", "-"));
            Directory.CreateDirectory(LogsDirectory);

            var appender = (log4net.Appender.FileAppender)LogManager.GetRepository().GetAppenders()[0];
            appender.File = Path.Combine(LogsDirectory, "debug-");
            appender.ActivateOptions();
            Logger.InfoFormat("Logs Directory: " + LogsDirectory);

            LeveledListFlag = LeveledItem.Flag.CalculateForEachItemInCount.Or(LeveledItem.Flag.CalculateFromAllLevelsLessThanOrEqualPlayer);
            LeveledNpcFlag = LeveledNpc.Flag.CalculateForEachItemInCount.Or(LeveledNpc.Flag.CalculateFromAllLevelsLessThanOrEqualPlayer);

            IniName = PatcherSettings.PatcherPrefix + "SPID_DISTR.ini";
            File.Copy(Path.Combine(EXE_LOC, "data", "config", PatcherSettings.KeywordFile), 
                Path.Combine(state.DataFolderPath, PatcherSettings.KeywordFile), true);

            // Parsing SPID Keywords
            Logger.InfoFormat("Parsing SPID Keywords... ");
            PatcherSettings.KeywordsSPID= FileUtils.GetSPIDKeywords(state.DataFolderPath);
            Logger.InfoFormat("Setting.json file is loaded...");
        }
    }
}
