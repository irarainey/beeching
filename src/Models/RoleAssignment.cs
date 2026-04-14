using System.Text.Json.Serialization;

namespace Beeching.Models
{
    internal class RoleAssignment
    {
        [JsonPropertyName("properties")]
        public RoleAssignmentProperties Properties { get; set; }
    }

    internal class RoleAssignmentProperties
    {
        [JsonPropertyName("roleDefinitionId")]
        public string RoleDefinitionId { get; set; }

        [JsonPropertyName("scope")]
        public string Scope { get; set; }
    }
}
