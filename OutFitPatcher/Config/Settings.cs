using log4net;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Cache.Internals.Implementations;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using OutFitPatcher.Utils;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OutFitPatcher.Config
{
    public class Settings
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(Settings));
        public static PatcherSettings? PatcherSettings;
        public static UserSettings? UserSettings;


        // Properties
        //internal static MutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter>? Cache;
        internal static ILinkCache<ISkyrimMod, ISkyrimModGetter>? Cache;
        internal static LeveledItem.Flag LeveledListFlag;
        internal static LeveledNpc.Flag LeveledNpcFlag;
        internal static ConcurrentDictionary<string, ISkyrimMod>? Patches;
        internal static  IPatcherState<ISkyrimMod, ISkyrimModGetter>? State;

        internal static HashSet<FormKey> NPCs2Skip=new();
        
        internal static void Init(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, UserSettings value)
        {
            string ConfigFile = Path.Combine(state.ExtraSettingsDataPath, "config", "PatcherSettings.json");
            PatcherSettings = FileUtils.ReadJson<PatcherSettings>(ConfigFile);
            Patches = new();
            State = state;
            Cache = state.LinkCache;
            
            LeveledListFlag = LeveledItem.Flag.CalculateForEachItemInCount.Or(LeveledItem.Flag.CalculateFromAllLevelsLessThanOrEqualPlayer);
            LeveledNpcFlag = LeveledNpc.Flag.CalculateForEachItemInCount.Or(LeveledNpc.Flag.CalculateFromAllLevelsLessThanOrEqualPlayer);

            UserSettings = value;
            NPCs2Skip = value.NPCToSkip.ToHashSet();
            Logger.Info("Setting.json file is loaded...");
        }
    }
}
