using Newtonsoft.Json;
using Noggog;
using System;
using System.Collections.Generic;


namespace ArmorDistributor.Converters
{
    public class DictionaryConverter : JsonConverter<Dictionary<object, object>>
    {
        public override Dictionary<object, object> ReadJson(JsonReader reader, Type objectType, Dictionary<object, object> existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override void WriteJson(JsonWriter writer, Dictionary<object, object> dic, JsonSerializer options)
        {
            writer.WriteStartObject();
            dic.ForEach(x => {
                writer.WriteValue(x.Key.ToString() +": "+x.Value);
            }) ;
        }
    }
}
    