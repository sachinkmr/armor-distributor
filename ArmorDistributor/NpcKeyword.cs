using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArmorDistributor
{
    public class NpcKeyword
    {
        public FormKey FormKey { get; }

        public string Name { get; }

        public string Keyword { get; }

        public NpcKeyword(ISkyrimMod Patch, string key)
        {
            Name = key;
            Keyword = "ADPType" + key;
            var keyword = Patch.Keywords.AddNew(Keyword);            
            FormKey = keyword.FormKey;
        }
    }
}
