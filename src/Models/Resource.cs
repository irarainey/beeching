using System.Text.Json.Serialization;

namespace Beeching.Models
{
    internal class Resource
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

        public string ResourceGroup { get; set; }

        public string? ApiVersion { get; set; }

        public string OutputMessage { get; set; }

        public bool IsLocked { get; set; }

        public bool Skip { get; set; }

        public List<ResourceLock> ResourceLocks { get; set; }

        public List<EffectiveRole> Roles { get; set; }

        public Resource()
        {
            ResourceLocks = new();
            Roles = new();
        }
    }
}
