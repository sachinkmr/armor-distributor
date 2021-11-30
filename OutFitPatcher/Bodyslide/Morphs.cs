using System.Collections.Generic;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using System.Threading.Tasks;
using Noggog;
using System.IO;
using OutFitPatcher.Utils;
using log4net.Config;
using log4net;
using System.Reflection;
using static OutFitPatcher.Config.Settings;
using OutFitPatcher.Managers;
using OutFitPatcher.Config;
using System;
using System.Text.RegularExpressions;

namespace OutFitPatcher.Bodyslide
{
    public class Morphs
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(Morphs));
        public static void create()
        {
            List<string> essential = new();
            List<string> unique = new();
            List<string> follower = new();
            string essentialFile = Path.Combine(Path.GetTempPath(), "EssentialNPCs.txt");
            string uniqueFile = Path.Combine(Path.GetTempPath(), "UniqueNPCs.txt");
            string followerFile = Path.Combine(Path.GetTempPath(), "FollowerNPCs.txt");
            string uniqueFollowers = Path.Combine(Path.GetTempPath(), "Unique+Followers.txt");
            int npcs = 0;

            foreach (var npc in State.LoadOrder.PriorityOrder
                .WinningOverrides<INpcGetter>()
                .Where(x => !x.Configuration.Flags.HasFlag(NpcConfiguration.Flag.IsCharGenFacePreset)
                 && x.Name != null
                && NPCUtils.IsFemale(x)))
            {
                
                var npcRace = npc.Race.Resolve(Settings.State.LinkCache);
                string race = npcRace.EditorID + " \"" + (npcRace.Name == null ? "" : npcRace.Name.String)
                    + "\" [RACE:" + npcRace.FormKey.IDString() + "]";

                string name = npc.Name == null ? "" : npc.Name.String;
                string mod = npc.FormKey.ModKey.FileName;
                string eid = npc.EditorID;
                string key = npc.FormKey.IDString().PadLeft(8, '0');
                string line = mod + " | " + name + " | " + eid + " | " + race + " | " + key;

                if (NPCUtils.IsFollower(npc)) follower.Add(line);                
                if (NPCUtils.IsUnique(npc)) unique.Add(line);
                if (NPCUtils.IsEssential(npc)) essential.Add(line);
                npcs++;
            }
            File.WriteAllLines(essentialFile, essential);
            File.WriteAllLines(uniqueFile, unique);
            File.WriteAllLines(followerFile, follower);
            File.WriteAllLines(uniqueFollowers, follower.Union(unique).Distinct());

            Logger.InfoFormat("Created File: " + essentialFile);
            Logger.InfoFormat("Created File: " + uniqueFile);
            Logger.InfoFormat("Created File: " + follower);
            Logger.InfoFormat("Created File: " + uniqueFollowers);
            Logger.InfoFormat("Total NPCs for morphing: " + npcs);
            
        }
    }
}
