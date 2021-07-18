using log4net;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Cache.Implementations;
using Mutagen.Bethesda.Plugins;
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
    public class Configuration
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(Configuration));
        internal static PatcherConfig? Patcher;
        internal static UserConfig? User;


        // Properties
        internal static MutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter>? Cache;
        internal static LeveledItem.Flag LeveledListFlag;
        internal static ConcurrentDictionary<string, ISkyrimMod>? Patches;
        internal static  IPatcherState<ISkyrimMod, ISkyrimModGetter>? State;

        internal static HashSet<FormKey> NPCs2Skip=new();
        internal static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, FormKey>> ArmorsWithSlot = new();

        internal static void Init(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            string configFile = Path.Combine(state.ExtraSettingsDataPath, "config", "patcher-settings.json");
            Patcher = FileUtils.ReadJson<PatcherConfig>(configFile);
            Patches = new();
            State = state;
            Cache = state.LoadOrder.ToMutableLinkCache();
            
            LeveledListFlag = LeveledItem.Flag.CalculateForEachItemInCount.Or(LeveledItem.Flag.CalculateFromAllLevelsLessThanOrEqualPlayer);

            // Loading user defined data
            configFile = Path.Combine(state.ExtraSettingsDataPath, "settings.json");
            User = FileUtils.ReadJson<UserConfig>(configFile);

            User.NPCToSkip.ForEach(key => {
                if (Cache.TryResolve<INpcGetter>(FormKey.Factory(key), out var npc))
                    NPCs2Skip.Add(npc.FormKey);
            });
            Logger.Info("Setting.json file is loaded...");
        }
    }
}
