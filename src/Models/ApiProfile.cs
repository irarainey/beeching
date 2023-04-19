using System.Text.Json.Serialization;

namespace Beeching.Models
{
    public class ApiProfile
    {
        [JsonPropertyName("profileVersion")]
        public string ProfileVersion { get; set; }

        [JsonPropertyName("apiVersion")]
        public string ApiVersion { get; set; }
    }
}
