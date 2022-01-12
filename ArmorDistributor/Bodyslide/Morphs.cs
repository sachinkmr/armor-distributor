using System.Collections.Generic;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using System.Threading.Tasks;
using Noggog;
using System.IO;
using ArmorDistributor.Utils;
using log4net.Config;
using log4net;
using System.Reflection;
using static ArmorDistributor.Config.Settings;
using ArmorDistributor.Managers;
using ArmorDistributor.Config;
using System;
using System.Text.RegularExpressions;

namespace ArmorDistributor.Bodyslide
{
    public class Morphs
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(Morphs));
        public static void create(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            List<string> essential = new();
            List<string> unique = new();
            List<string> follower = new();
            string essentialFile = Path.Combine(Path.GetTempPath(), "EssentialNPCs.txt");
            string uniqueFile = Path.Combine(Path.GetTempPath(), "UniqueNPCs.txt");
            string followerFile = Path.Combine(Path.GetTempPath(), "FollowerNPCs.txt");
            string uniqueFollowers = Path.Combine(Path.GetTempPath(), "Unique+Followers.txt");
            int npcs = 0;

            foreach (var npc in state.LoadOrder.PriorityOrder
                .WinningOverrides<INpcGetter>()
                .Where(x => !Program.Settings.UserSettings.ModsToSkip.Contains(x.FormKey.ModKey))
                .Where(x => !x.Configuration.Flags.HasFlag(NpcConfiguration.Flag.IsCharGenFacePreset)
                 && x.Name != null
                && NPCUtils.IsFemale(x)))
            {
                
                var npcRace = npc.Race.Resolve(Program.Settings.State.LinkCache);
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

            Console.WriteLine("Created File: " + essentialFile);
            Console.WriteLine("Created File: " + uniqueFile);
            Console.WriteLine("Created File: " + follower);
            Console.WriteLine("Created File: " + uniqueFollowers);
            Console.WriteLine("Total NPCs for morphing: " + npcs);
            
        }
    }
}
