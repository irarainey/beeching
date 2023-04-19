using System.Text.Json.Serialization;

namespace Beeching.Models
{
    public class Resources
    {
        [JsonPropertyName("value")]
        public List<Resource> Value;
    }
}
