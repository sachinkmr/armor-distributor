using log4net;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Cache.Internals.Implementations;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using ArmorDistributor.Utils;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mutagen.Bethesda.Synthesis.Settings;
using Mutagen.Bethesda.WPF.Reflection.Attributes;
using System;

namespace ArmorDistributor.Config
{
    public class Settings
    {
        [Ignore]
        private static readonly ILog Logger = LogManager.GetLogger(typeof(Settings));

        [Ignore]
        public static PatcherSettings PatcherSettings;

        [Ignore]
        public static UserSettings DefaultUserSettings;

        [SynthesisOrder]
        [JsonDiskName("UserSettings")]
        [SettingName("Patcher Settings: ")]
        public UserSettings UserSettings = new();

        // Properties
        [Ignore]
        public ILinkCache<ISkyrimMod, ISkyrimModGetter>? Cache;
        [Ignore]
        internal LeveledItem.Flag LeveledListFlag;
        
        [Ignore]
        internal LeveledNpc.Flag LeveledNpcFlag;

        [Ignore]
        internal List<ISkyrimMod> Patches = new();

        [Ignore]
        internal  IPatcherState<ISkyrimMod, ISkyrimModGetter>? State;

        [Ignore]
        public string? IniName;

        static Settings() {
            string exeLoc = Directory.GetParent(System.Reflection.Assembly.GetAssembly(typeof(Settings)).Location).FullName;
            string ConfigFile = Path.Combine(exeLoc, "data", "config", "PatcherSettings.json");
            PatcherSettings = FileUtils.ReadJson<PatcherSettings>(ConfigFile).init();

            ConfigFile = Path.Combine(exeLoc, "data", "config", "UserSettings.json");
            DefaultUserSettings = FileUtils.ReadJson<UserSettings>(ConfigFile);
            PatcherSettings.PatcherPrefix = DefaultUserSettings.PatcherPrefix;

            foreach (var pair in DefaultUserSettings.ArmorMods) {
                if (ModKey.TryFromNameAndExtension(pair.Key, out var modKey) && Program.PatcherEnv.LoadOrder.ContainsKey(modKey)) {
                    List<Categories> cats = new();
                    pair.Value.ForEach(c=> { 
                        if(Enum.TryParse(c, out Categories cat))  cats.Add(cat);
                    });
                    var item = new ModCategory(modKey, cats);
                    DefaultUserSettings.PatchableArmorMods.Add(item);
                }
            }
        }

        internal void Init(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            State = state;
            Cache = state.LinkCache;
            
            LeveledListFlag = LeveledItem.Flag.CalculateForEachItemInCount.Or(LeveledItem.Flag.CalculateFromAllLevelsLessThanOrEqualPlayer);
            LeveledNpcFlag = LeveledNpc.Flag.CalculateForEachItemInCount.Or(LeveledNpc.Flag.CalculateFromAllLevelsLessThanOrEqualPlayer);

            IniName = PatcherSettings.PatcherPrefix + "SPID_DISTR.ini";
            Logger.Info("Setting.json file is loaded...");
        }
    }
}
