using System.Text.Json.Serialization;

namespace Beeching.Models
{
    internal class ArmListResponse<T>
    {
        [JsonPropertyName("value")]
        public List<T> Value { get; set; } = new();
    }
}
