using System.Text.Json.Serialization;

namespace Beeching.Models
{
    internal sealed class Resource
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("location")]
        public string Location { get; set; }

        [JsonPropertyName("tags")]
        public Dictionary<string, string> Tags { get; set; }
    }
}
