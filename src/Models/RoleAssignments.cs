using System.Text.Json.Serialization;

namespace Beeching.Models
{
    public class RoleAssignment
    {
        [JsonPropertyName ("condition")]
        public string? Condition { get; set; }

        [JsonPropertyName ("conditionVersion")]
        public string? ConditionVersion { get; set; }

        [JsonPropertyName ("createdBy")]
        public string CreatedBy { get; set; }

        [JsonPropertyName ("createdOn")]
        public DateTime CreatedOn { get; set; }

        [JsonPropertyName ("delegatedManagedIdentityResourceId")]
        public string? DelegatedManagedIdentityResourceId { get; set; }

        [JsonPropertyName ("description")]
        public string? Description { get; set; }

        [JsonPropertyName ("id")]
        public string Id { get; set; }

        [JsonPropertyName ("name")]
        public string Name { get; set; }

        [JsonPropertyName ("principalId")]
        public string PrincipalId { get; set; }

        [JsonPropertyName ("principalName")]
        public string PrincipalName { get; set; }

        [JsonPropertyName ("principalType")]
        public string PrincipalType { get; set; }

        [JsonPropertyName ("roleDefinitionId")]
        public string RoleDefinitionId { get; set; }

        [JsonPropertyName ("roleDefinitionName")]
        public string RoleDefinitionName { get; set; }

        [JsonPropertyName ("scope")]
        public string Scope { get; set; }

        [JsonPropertyName ("type")]
        public string Type { get; set; }

        [JsonPropertyName ("updatedBy")]
        public string UpdatedBy { get; set; }

        [JsonPropertyName ("updatedOn")]
        public DateTime UpdatedOn { get; set; }
    }
}
