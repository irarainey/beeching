using System.Text.Json.Serialization;

namespace Beeching.Models
{
    public class ApiVersions
    {
        [JsonPropertyName("value")]
        public List<ApiVersion> Value { get; set; }
    }
}
