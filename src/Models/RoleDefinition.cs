using System.Text.Json.Serialization;

namespace Beeching.Models
{
    internal class RoleDefinition
    {
        [JsonPropertyName("properties")]
        public RoleDefinitionProperties Properties { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }
    }
}
