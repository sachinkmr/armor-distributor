using Mutagen.Bethesda;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins.Cache;
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
            return IsChild(Settings.Cache.Resolve<IRaceGetter>(npc.Race.FormKey))
                || IsChild(Settings.Cache.Resolve<IClassGetter>(npc.Class.FormKey).EditorID);
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
            return !Settings.NPCs2Skip.Contains(npc.FormKey) 
                && !IsChild(npc) && isValidActorType(npc)
                && (Regex.IsMatch(npc.EditorID, Settings.PatcherSettings.ValidNpcRegex, RegexOptions.IgnoreCase)
                    || !Regex.IsMatch(npc.EditorID, Settings.PatcherSettings.InvalidNpcRegex, RegexOptions.IgnoreCase));
        }

        public static bool isValidActorType(INpcGetter npc)
        {
            return isValidActorType(npc, Settings.Cache);
        }

        public static bool isValidActorType(INpcGetter npc, ILinkCache cache)
        {
            var r = cache.Resolve<IRaceGetter>(npc.Race.FormKey);
            return (r.HasKeyword(Skyrim.Keyword.ActorTypeNPC)
                || r.HasKeyword(Skyrim.Keyword.ActorTypeDaedra)
                || r.HasKeyword(Skyrim.Keyword.ActorTypeGhost)
                || r.HasKeyword(Skyrim.Keyword.ActorTypePrisoner))
                && !(r.HasKeyword(Skyrim.Keyword.ActorTypeAnimal)
                || r.HasKeyword(Skyrim.Keyword.ActorTypeCow)
                || r.HasKeyword(Skyrim.Keyword.ActorTypeCreature)
                || r.HasKeyword(Skyrim.Keyword.ActorTypeDragon)
                || r.HasKeyword(Skyrim.Keyword.ActorTypeDwarven)
                || r.HasKeyword(Skyrim.Keyword.ActorTypeFamiliar)
                || r.HasKeyword(Skyrim.Keyword.ActorTypeGiant)
                || r.HasKeyword(Skyrim.Keyword.ActorTypeHorse)
                || r.HasKeyword(Skyrim.Keyword.ActorTypeTroll)
                || r.HasKeyword(Skyrim.Keyword.ActorTypeUndead));
        }

        public static bool IsGuard(INpcGetter npc) {
            return npc.Factions.Any(x => x.Faction.FormKey.Equals(Skyrim.Faction.GuardDialogueFaction));
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
            return npc.Factions.Any(r => r.Faction.Resolve(Settings.State.LinkCache).EditorID.Contains("FollowerFaction"));
        }
    }
}
