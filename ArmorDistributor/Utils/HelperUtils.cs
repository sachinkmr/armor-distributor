using log4net;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using static ArmorDistributor.Config.Settings;


namespace ArmorDistributor.Utils
{

    public static class HelperUtils
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(HelperUtils));

        public static IEnumerable<T> GetEnumValues<T>()
        {
            return Enum.GetValues(typeof(T)).Cast<T>();
        }

        public static bool IsValidFaction(IFactionGetter faction)
        {
            return Regex.Match(faction.EditorID, PatcherSettings.ValidFactionRegex, RegexOptions.IgnoreCase).Success
                    || !Regex.Match(faction.EditorID, PatcherSettings.InvalidFactionRegex, RegexOptions.IgnoreCase).Success;
        }

        public static bool IsValidFaction(string faction)
        {
            return Regex.Match(faction, PatcherSettings.ValidFactionRegex, RegexOptions.IgnoreCase).Success
                    || !Regex.Match(faction, PatcherSettings.InvalidFactionRegex, RegexOptions.IgnoreCase).Success;
        }

        public static IEnumerable<string> GetRegexBasedGroup(Dictionary<string, string> regx, string str, string? optionalStr = null)
        {
            List<string> group = new();
            regx.ForEach(pair =>
             {
                 string[] regex = pair.Value.Split("::");
                 if ((regex.Length == 1 ? Regex.Match(str ?? "", regex[0], RegexOptions.IgnoreCase).Success
                         : Regex.Match(str ?? "", regex[0], RegexOptions.IgnoreCase).Success
                         && !Regex.Match(str ?? "", regex[1], RegexOptions.IgnoreCase).Success))
                 {
                     group.Add(pair.Key);
                 }
                 if (optionalStr != null && (regex.Length == 1 ? Regex.Match(optionalStr ?? "", regex[0], RegexOptions.IgnoreCase).Success
                         : Regex.Match(optionalStr ?? "", regex[0], RegexOptions.IgnoreCase).Success
                         && !Regex.Match(optionalStr ?? "", regex[1], RegexOptions.IgnoreCase).Success))
                 {
                     group.Add(pair.Key);
                 }
             });
            return group.Distinct();
        }

        public static string ToCamelCase(this string text)
        {
            return string.Join(" ", text
                                .Split()
                                .Select(i => char.ToUpper(i[0]) + i.Substring(1)));
        }
         
        public static string GetLongestCommonSubstring(string s1, string s2)
        {
            int[,] a = new int[s1.Length + 1, s2.Length + 1];
            int row = 0;    // s1 index
            int col = 0;    // s2 index

            for (var i = 0; i < s1.Length; i++)
                for (var j = 0; j < s2.Length; j++)
                    if (s1[i] == s2[j])
                    {
                        int len = a[i + 1, j + 1] = a[i, j] + 1;
                        if (len > a[row, col])
                        {
                            row = i + 1;
                            col = j + 1;
                        }
                    }

            return s1.Substring(row - a[row, col], a[row, col]);
        }

        public static HashSet<string> GetCommonItems(List<string> list)
        {
            if (!list.Any()) return new();
            var hash = new HashSet<string>(list.First().Split(' '));
            for (int i = 1; i < list.Count; i++)
                hash.IntersectWith(list[i].Split(' '));
            return hash;
        }

        public static int GetMatchingWordCount(string strOne, string strTwo)
        {
            strOne = SplitString(strOne);
            strTwo = SplitString(strTwo);

            var tokensOne = strOne.Split(" ", StringSplitOptions.RemoveEmptyEntries);
            var list = tokensOne.Where(x => strTwo.Contains(x));
            return list.Count();
        }
        
        public static string SplitString(string input)
        {
            var underscore = Regex.Replace(input, "[_/-]", " ", RegexOptions.Compiled).Trim();
            return Regex.Replace(underscore, "([a-z0-9])([A-Z])", "$1 $2", RegexOptions.Compiled).Trim();
        }
    }
}
