using System.Text.Json.Serialization;

namespace Beeching.Models
{
    internal sealed class ResourceLockProperties
    {
        [JsonPropertyName ("level")]
        public string Level { get; set; }

        [JsonPropertyName ("notes")]
        public string Notes { get; set; }
    }
}
