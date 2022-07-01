using System.IO;
using System.Linq;

namespace Helpers
{
    class Program
    {
        public static void Main(string[] args)
        {

            XMLUtils.Set3BAGroupAndSet();
            //FileUtils.MoveMods(@"E:\Modded\SSE-Aldrnari142\KinkyMods.txt", @"E:\Modded\SSE-Aldrnari142\mods", @"E:\Modded\SSE-Aldrnari\mods");
            //FileUtils.MergeMods(@"E:\Modded\SSE-Aldrnari\profiles\Aldrnari - Armors\Merging.txt", @"E:\Modded\SSE-Aldrnari\mods");

            //FileUtils.MergePlugins(@"E:\Modded\SSE-Aldrnari142\mods\Armor Merged\merge - Armor Merged\merge.json", true);
            //return;

            //var esps = Directory.EnumerateFiles(@"E:\Modded\SSE-Aldrnari\mods\Armor Merged", "*.*", SearchOption.AllDirectories)
            //.Where(s => s.EndsWith("_DISTR.ini")).ToList();
            //FileUtils.UpdateSPIDFile(@"E:\Modded\SSE-Aldrnari\mods\Armor Merged\merge - Armor Merged\map.json", esps, "Armor Merged.esp");
        }
    }
}
