using System;
using System.IO;
using System.Linq;
using System.Xml;


#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8604 // Dereference of a possibly null reference.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.

namespace Helpers
{
    class XMLUtils
    {
        public static void Set3BAGroupAndSet() {
            string src = @"E:\Modded\SSE\Skybax\mods\3BBB Presets1\CalienteTools\BodySlide\SliderPresets";
            string dest = @"E:\Modded\SSE\Skybax\mods\3BBB Presets\CalienteTools\BodySlide\SliderPresets";
            if (!Directory.Exists(dest))
            {
                Directory.CreateDirectory(dest);
            }


            XmlDocument doc = new XmlDocument();
            string[] allfiles = Directory.GetFiles(src, "*.xml", SearchOption.AllDirectories);

            var group = doc.CreateNode(XmlNodeType.Element, "Group", null);
            group.Attributes.Append(doc.CreateAttribute("name"));

            foreach (string file in allfiles)
            {
                Console.WriteLine("Processing: " + file);
                doc.Load(file);
                XmlNodeList aNodes = doc.SelectNodes("/SliderPresets/Preset");
                foreach (XmlNode aNode in aNodes)
                {
                    aNode.Attributes["set"].Value = "CBBE 3BBB Body Amazing";
                    var groups = aNode.SelectNodes("Group").Cast<XmlNode>();
                    if (!groups.Any(x => x.Attributes["name"].Value == "CBBE Vanilla Outfits Physics"))
                        aNode.PrependChild(group).Attributes["name"].Value = "CBBE Vanilla Outfits Physics";
                    if (!groups.Any(x => x.Attributes["name"].Value == "CBBE Vanilla Outfits"))
                        aNode.PrependChild(group.Clone()).Attributes["name"].Value = "CBBE Vanilla Outfits";
                    if (!groups.Any(x => x.Attributes["name"].Value == "CBBE Bodies"))
                        aNode.PrependChild(group.Clone()).Attributes["name"].Value = "CBBE Bodies";
                    if (!groups.Any(x => x.Attributes["name"].Value == "3BA"))
                        aNode.PrependChild(group.Clone()).Attributes["name"].Value = "3BA";
                    if (!groups.Any(x => x.Attributes["name"].Value == "3BBB"))
                        aNode.PrependChild(group.Clone()).Attributes["name"].Value = "3BBB";
                    if (!groups.Any(x => x.Attributes["name"].Value == "CBBE"))
                        aNode.PrependChild(group.Clone()).Attributes["name"].Value = "CBBE";
                }
                doc.Save(file.Replace(src, dest));
            }
        }
    }
}
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning restore CS8602 // Dereference of a possibly null reference.
#pragma warning restore CS8604 // Dereference of a possibly null reference.
