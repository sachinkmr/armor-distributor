using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using OutFitPatcher.Config;
using OutFitPatcher.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OutFitPatcher.NPC
{
    public class TNPC
    {
        public Dictionary<string, string> FactionGroup; // facName, FacEID
        public List<string> ClassGroup;  // ClassGrp, [ClassEID]
        public List<string> NameGroup;  // ClassGrp, [ClassEID]
        public string? ClassEID;
        public string? NpcName;
        public string? Eid;
        public string NPCKey;
        public string ClassKey;
        public string ArmorType="";
        public string Identifier = "";
        public string Outfit = "";

        public TNPC(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, INpcGetter npc)
        {
            NpcName = npc.Name == null ? "" : npc.Name.String;
            NPCKey = npc.FormKey.ToString();
            Eid = npc.EditorID;

            ClassKey = npc.Class.FormKey.ToString();
            var npcClass = state.LinkCache.Resolve<IClassGetter>(npc.Class.FormKey);
            ClassEID = npcClass.EditorID;

            ClassGroup = HelperUtils.GetRegexBasedGroup(Configuration.Patcher.OutfitRegex, ClassEID).ToList();
            NameGroup = HelperUtils.GetRegexBasedGroup(Configuration.Patcher.OutfitRegex, Eid).ToList();
            FactionGroup = new();
            npc.Factions.ForEach(facs =>
            {
                var fac = facs.Faction.Resolve(state.LinkCache).EditorID;
                if (HelperUtils.IsValidFaction(fac)) {
                    var list = HelperUtils.GetRegexBasedGroup(Configuration.Patcher.OutfitRegex, fac);
                    list.ForEach(l => FactionGroup[l] = fac);
                }                
            });

            Identifier = FactionGroup.Count() > 0 ? FactionGroup.Keys.Last()
                            : ClassGroup.Count() > 0 ? ClassGroup.First()
                            : NameGroup.Count() > 0 ? NameGroup.First() 
                            : ClassEID== "Citizen"? "CitizenRich":"";

            string facregex = Configuration.Patcher.DividableFactions;
            Match matcher = Regex.Match(Identifier, facregex, RegexOptions.IgnoreCase);
            if (matcher.Success)
            {
                var faction = matcher.Value;
                var list = HelperUtils.GetRegexBasedGroup(Configuration.Patcher.ArmorTypeRegex, ClassEID)
                    .Select(x => x.Replace(faction, "")).EmptyIfNull();
                ArmorType = list.Count() == 0 ? "" : list.First();
            }

        }

        public override string? ToString()
        {
            return Eid;
        }

    }
}
