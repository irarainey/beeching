using System.Text.Json.Serialization;

namespace Beeching.Models
{
    internal sealed class ResourceLock
    {
        [JsonPropertyName ("properties")]
        public ResourceLockProperties Properties { get; set; }

        [JsonPropertyName ("id")]
        public string Id { get; set; }

        [JsonPropertyName ("type")]
        public string Type { get; set; }

        [JsonPropertyName ("name")]
        public string Name { get; set; }

        [Newtonsoft.Json.JsonIgnore] 
        public string Scope { get; set; }
    }
}
