
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CyanTools.Model
{
    public class LoadedModelsModel
    {
        [JsonPropertyName("data")]
        public List<ModelData> Data { get; set; }
    }

    public class ModelData
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
    }
}
