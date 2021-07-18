using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using OutFitPatcher.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OutFitPatcher.Utils
{
    public class NPCUtils
    {
        public static bool IsChild(INpcGetter npc) {
            return IsChild(Configuration.Cache.Resolve<IRaceGetter>(npc.Race.FormKey))
                || IsChild(Configuration.Cache.Resolve<IClassGetter>(npc.Class.FormKey).EditorID);
        }

        public static bool IsChild(IRaceGetter race)
        {
            return IsChild(race.EditorID);
        }

        public static bool IsChild(string race)
        {
            return Regex.IsMatch(race, "child", RegexOptions.IgnoreCase);
        }

        public static bool IsValidNPC(INpcGetter npc) {
            return !Configuration.NPCs2Skip.Contains(npc.FormKey) 
                && !IsChild(npc) && !IsGhost(npc)
                && (Regex.IsMatch(npc.EditorID, Configuration.Patcher.ValidNpcRegex, RegexOptions.IgnoreCase)
                    || !Regex.IsMatch(npc.EditorID, Configuration.Patcher.InvalidNpcRegex, RegexOptions.IgnoreCase));
        }

        public static bool IsUnique(INpcGetter npc)
        {
            return npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Unique);
        }

        public static bool IsEssential(INpcGetter npc)
        {
            return npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Essential);
        }

        public static bool IsProtected(INpcGetter npc)
        {
            return npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Protected);
        }

        public static bool IsGhost(INpcGetter npc)
        {
            return npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.IsGhost);
        }

        public static bool IsFemale(INpcGetter npc)
        {
            return npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Female);
        }

        public static bool IsFollower(INpcGetter npc)
        {
            return npc.Factions.Any(r =>r.Faction.Resolve<IFactionGetter>(Configuration.Cache).EditorID.Contains("FollowerFaction"));
        }
    }
}
