using ArmorDistributor.Utils;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8604 // Dereference of a possibly null reference.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CA1416 // Validate platform compatibility

namespace Helpers
{
    public class FileUtils
    {
        public static void MoveMods(string modNames, string src, string dest) {
            var lines =  File.ReadAllLines(modNames).Select(x=>x.Substring(1));
            foreach (var line in lines) {
                CopyDirectory(Path.Combine(src,line), Path.Combine(dest, line));
            }
        }


        public static void MergeMods(string modNames, string src, string mergeName="All Armor Mods") {
            var lines = File.ReadAllLines(modNames).Select(x => x.Substring(1)).Reverse();
            var dest = Path.Combine(src, mergeName);
            foreach (var line in lines)
            {
                var mod = Path.Combine(src, line);
                CopyDirectory(mod, dest);
                Console.WriteLine("Copied: " + mod);
            }
        }

        public static void GetSovnNPCsWithOutfits(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, string outFile) {
            // Getting NPC using Sovn armors
            var sovnOTFTs = state.LoadOrder.PriorityOrder
                .Where(x => x.ModKey.FileName.String.Equals("Sovn's Armor and Weapons Merge.esp"))
                .Outfit().WinningOverrides().Where(x => x.FormKey.ToString().Contains("Sovn"))
                .Select(x => x.FormKey);


            var skippableNPC = state.LoadOrder.PriorityOrder.WinningOverrides<INpcGetter>()
                 .Where(n => NPCUtils.IsUnique(n)
                             && n.DefaultOutfit != null
                             && sovnOTFTs.Contains(n.DefaultOutfit.FormKey))
                 .Select(x => x.FormKey)
                 .ToList();
            ArmorDistributor.Utils.FileUtils.WriteJson(outFile, skippableNPC);
        }

        public static void MergePlugins(string loc, bool show)
        {
            JObject data = JObject.Parse(File.ReadAllText(loc));
            var plugins = data.GetValue("plugins");
            foreach (var plugin in plugins)
            {
                var filename = plugin.Value<string>("filename");
                var fileLoc = plugin.Value<string>("dataFolder");

                var src = show ? Path.Combine(fileLoc, "optional", filename) : Path.Combine(fileLoc, filename);
                var dest = show ? Path.Combine(fileLoc, filename) : Path.Combine(fileLoc, "optional", filename);
                if (!File.Exists(Path.GetDirectoryName(dest))) Directory.CreateDirectory(Path.GetDirectoryName(dest));
                try
                {
                    if (File.Exists(src)) File.Move(src, dest, true);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }

        public static void UpdateSPIDFile(string mergeMap, List<string> spidINIs, string mergedMod)
        {
            var trimmer = new Char[] { '0' };
            JObject data = JObject.Parse(File.ReadAllText(mergeMap));
            string spid = "";

            foreach (var v2 in spidINIs)
            {
                var spidLines = File.ReadAllLines(v2);
                spid = spid + "\n;Merging SPID Records for " + Path.GetFileName(v2) + "\n" + File.ReadAllText(v2);
                var esps = spidLines.Where(l => l.Contains(".esp|")).Select(line => {
                    int start = line.IndexOf('~') + 1;
                    return line.Substring(start, line.IndexOf('|') - start);
                }).Distinct();
                esps.ToList().ForEach(esp => {
                    data.GetValue(esp).Select(v => v.ToString().Replace("\"", "").Replace(" ", "").Split(':').Select(a => a.TrimStart(trimmer))).ToList().ForEach(v => {
                        var str1 = "0x" + v.ElementAt(0) + "~" + esp;
                        var str2 = "0x" + v.ElementAt(1) + "~" + mergedMod;
                        //spid = spid.Replace(str1, str2);
                        spid = Regex.Replace(spid, str1, str2, RegexOptions.IgnoreCase);
                    });
                });
            }
            var file = Path.Combine(Directory.GetParent(Directory.GetParent(mergeMap).FullName).FullName, Path.GetFileNameWithoutExtension(mergedMod) + "_DISTR.ini");
            File.WriteAllText(file, spid);
        }

        public static void CopyDirectory(string sourcePath, string targetPath)
        {
            if (File.Exists(targetPath)) {
                Console.WriteLine("Skipping: " + targetPath);
                return;
            }

            //Now Create all of the directories
            Directory.CreateDirectory(targetPath);
            foreach (string dirPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(sourcePath, targetPath));
            }

            //Copy all the files & Replaces any files with the same name
            foreach (string newPath in Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories))
                File.Copy(newPath, newPath.Replace(sourcePath, targetPath), true);
        }        
    }
}
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning restore CS8602 // Dereference of a possibly null reference.
#pragma warning restore CS8604 // Dereference of a possibly null reference.
#pragma warning restore CA1416 // Validate platform compatibility