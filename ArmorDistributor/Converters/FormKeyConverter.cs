using Mutagen.Bethesda.Plugins;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArmorDistributor
{
    public class FormKeyConverter : JsonConverter<FormKey>
    {
        public override FormKey ReadJson(JsonReader reader, Type objectType, FormKey existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override void WriteJson(JsonWriter writer, FormKey value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToString());
        }
    }
}
