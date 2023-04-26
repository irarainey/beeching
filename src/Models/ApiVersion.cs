using System.Text.Json.Serialization;

namespace Beeching.Models
{
    internal class ApiVersion
    {
        [JsonPropertyName("resourceType")]
        public string ResourceType { get; set; }

        [JsonPropertyName("locations")]
        public List<string> Locations { get; set; }

        [JsonPropertyName("apiVersions")]
        public List<string> ApiVersions { get; set; }

        [JsonPropertyName("defaultApiVersion")]
        public string DefaultApiVersion { get; set; }

        [JsonPropertyName("apiProfiles")]
        public List<ApiProfile> ApiProfiles { get; set; }

        [JsonPropertyName("capabilities")]
        public string Capabilities { get; set; }
    }
}
