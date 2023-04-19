using System.Text.Json.Serialization;

namespace Beeching.Models
{
    internal sealed class ApiProfile
    {
        [JsonPropertyName("profileVersion")]
        public string ProfileVersion { get; set; }

        [JsonPropertyName("apiVersion")]
        public string ApiVersion { get; set; }
    }
}
