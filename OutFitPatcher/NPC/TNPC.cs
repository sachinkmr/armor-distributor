using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using OutFitPatcher.Armor;
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
        public string? EditorID;
        public FormKey FormKey;
        public string ClassKey;
        public string ArmorType="";
        public string Identifier = "";
        public string Outfit = "";

        public TNPC(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, INpcGetter npc)
        {
            NpcName = npc.Name == null ? "" : npc.Name.String;
            FormKey = npc.FormKey;
            EditorID = npc.EditorID;

            ClassKey = npc.Class.FormKey.ToString();
            var npcClass = state.LinkCache.Resolve<IClassGetter>(npc.Class.FormKey);
            ClassEID = npcClass.EditorID;

            ClassGroup = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, ClassEID).ToList();
            NameGroup = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, EditorID).ToList();
            FactionGroup = new();
            var cache = state.LoadOrder.ToMutableLinkCache();
            npc.Factions.ForEach(facs =>
            {
                if (facs.Faction.TryResolve<IFactionGetter>(cache, out var faction) && HelperUtils.IsValidFaction(faction.EditorID)) {
                    var fac = faction.EditorID;
                    var list = HelperUtils.GetRegexBasedGroup(Settings.PatcherSettings.OutfitRegex, fac);
                    list.ForEach(l => FactionGroup[l] = fac);
                }                
            });

            var lowPriortyFactions = new string[] { "Bandit", "Merchant" };

            var commonInFactionClass = FactionGroup.Keys.Intersect(ClassGroup).ToList();
            Identifier = commonInFactionClass.Any()? commonInFactionClass.First()
                            : FactionGroup.Any()&&!FactionGroup.Keys.Contains("Daedra") && !FactionGroup.Keys.Contains("Bandit")? FactionGroup.Keys.Last()
                            : ClassGroup.Count() > 0 ? ClassGroup.Last()
                            : NameGroup.Count() > 0 ? NameGroup.Last() 
                            : ClassEID== "Citizen"? "CitizenRich":"Unknown";
            
            if (Regex.IsMatch(Identifier, Settings.PatcherSettings.DividableFactions, RegexOptions.IgnoreCase)) {
                Skill[]? skills = new Skill[] { Skill.HeavyArmor, Skill.LightArmor, Skill.Conjuration, Skill.Alteration, Skill.Destruction, Skill.Illusion, Skill.Restoration };
                var  allSkills = npc.PlayerSkills.SkillValues.Where(x => skills.Contains(x.Key));
                var maxSkill = allSkills.OrderBy(x => x.Value)
                    .ToDictionary(x => x.Key, x => x.Value)
                    .Last().Key;

                ArmorType = maxSkill == Skill.HeavyArmor ? TArmorType.Heavy
                    : maxSkill == Skill.LightArmor ? TArmorType.Light
                    : TArmorType.Wizard;
                Identifier += ArmorType;
            }
        }

        public override string? ToString()
        {
            return string.Format("NPCKey: {0} | Factions: {1} | Classes: {2} | NameGroup: {3} | Id: {4}",
                FormKey.ToString(), string.Join(", ",FactionGroup.Keys), string.Join(", ", ClassGroup), 
                string.Join(", ", NameGroup), Identifier); 
        }
    }
}
