using System.Text.Json.Serialization;

namespace Beeching.Models
{
    internal class RoleDefinitionProperties
    {
        [JsonPropertyName("roleName")]
        public string RoleName { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("assignableScopes")]
        public List<string> AssignableScopes { get; set; }

        [JsonPropertyName("permissions")]
        public List<RoleDefinitionPermission> Permissions { get; set; }

        [JsonPropertyName("createdOn")]
        public DateTime CreatedOn { get; set; }

        [JsonPropertyName("updatedOn")]
        public DateTime UpdatedOn { get; set; }

        [JsonPropertyName("createdBy")]
        public string? CreatedBy { get; set; }

        [JsonPropertyName("updatedBy")]
        public string? UpdatedBy { get; set; }

        public RoleDefinitionProperties()
        {
            AssignableScopes = new();
            Permissions = new();
        }
    }
}
