using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace TonyMods
{
    // Unity's native serializer may not discover types from dynamically loaded mods.
    internal static class HorseJson
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Converters = new JsonConverter[] { new VectorConverter() },
            MaxDepth = 16,
            TypeNameHandling = TypeNameHandling.None
        };

        public static string Serialize(object value)
        { return JsonConvert.SerializeObject(value, Settings); }

        public static T Deserialize<T>(string json)
        { return JsonConvert.DeserializeObject<T>(json, Settings); }

        // Serialize coordinates only, never Unity's recursive normalized property.
        private sealed class VectorConverter : JsonConverter
        {
            public override bool CanConvert(Type type) { return type == typeof(Vector3); }
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                Vector3 v = (Vector3)value;
                writer.WriteStartObject();
                writer.WritePropertyName("x"); writer.WriteValue(v.x);
                writer.WritePropertyName("y"); writer.WriteValue(v.y);
                writer.WritePropertyName("z"); writer.WriteValue(v.z);
                writer.WriteEndObject();
            }
            public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer)
            {
                JObject value = JObject.Load(reader);
                if (value["x"] == null || value["y"] == null || value["z"] == null)
                    throw new JsonSerializationException("Horse position requires x, y, z.");
                return new Vector3((float)value["x"], (float)value["y"], (float)value["z"]);
            }
        }
    }
}
