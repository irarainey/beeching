using Beeching.Models;
using Spectre.Console;

namespace Beeching.Helpers
{
    internal class RoleHelper
    {
        private readonly ArmClient _armClient;
        private readonly Dictionary<string, RoleDefinition> _roleDefinitionCache = new();

        public RoleHelper(ArmClient armClient)
        {
            _armClient = armClient;
        }

        public async Task<List<EffectiveRole>> DetermineSubscriptionRoles(AxeContext context)
        {
            List<EffectiveRole> subscriptionRoles = new();
            string roleUri =
                $"subscriptions/{context.Settings.Subscription}/providers/Microsoft.Authorization/roleAssignments?$filter=principalId eq '{context.UserId}'&api-version=2022-04-01";

            var roles = await _armClient.GetListAsync<RoleAssignment>(roleUri);

            foreach (var role in roles)
            {
                if (role.Properties.Scope != $"/subscriptions/{context.Settings.Subscription}")
                {
                    continue;
                }

                RoleDefinition roleDefinition = await GetRoleDefinition(role.Properties.RoleDefinitionId);
                subscriptionRoles.Add(CreateEffectiveRole(roleDefinition, role.Properties.Scope, "subscription"));
            }

            return subscriptionRoles;
        }

        public async Task DetermineResourceRoles(AxeContext context, List<Resource> resources)
        {
            AnsiConsole.Markup("[green]=> Checking resources for role assignments[/]\n");

            foreach (Resource resource in resources)
            {
                string roleUri =
                    $"{resource.Id}/providers/Microsoft.Authorization/roleAssignments?$filter=principalId eq '{context.UserId}'&api-version=2022-04-01";

                var roles = await _armClient.GetListAsync<RoleAssignment>(roleUri);

                foreach (var role in roles)
                {
                    if (role.Properties.Scope == $"/subscriptions/{context.Settings.Subscription}")
                    {
                        continue;
                    }

                    string[] scopeSections = role.Properties.Scope.Split('/');
                    string scopeType = scopeSections.Length > 5 ? "resource" : "resource group";

                    RoleDefinition roleDefinition = await GetRoleDefinition(role.Properties.RoleDefinitionId);
                    resource.Roles.Add(CreateEffectiveRole(roleDefinition, role.Properties.Scope, scopeType));
                }
            }
        }

        private async Task<RoleDefinition> GetRoleDefinition(string roleDefinitionId)
        {
            string[] sections = roleDefinitionId.Split('/');
            string roleId = sections[^1];

            if (_roleDefinitionCache.TryGetValue(roleId, out var cached))
            {
                return cached;
            }

            string roleUri = $"providers/Microsoft.Authorization/roleDefinitions/{roleId}?api-version=2022-04-01";
            var result = await _armClient.GetAsAsync<RoleDefinition>(roleUri);
            var definition = result ?? new RoleDefinition();
            _roleDefinitionCache[roleId] = definition;
            return definition;
        }

        internal static EffectiveRole CreateEffectiveRole(RoleDefinition roleDefinition, string scope, string scopeType)
        {
            var effectiveRole = new EffectiveRole
            {
                RoleDefinitionId = roleDefinition.Name,
                Scope = scope,
                ScopeType = scopeType,
                Name = roleDefinition.Properties.RoleName,
                Type = roleDefinition.Properties.Type,
                Priority = roleDefinition.Properties.RoleName switch
                {
                    "Owner" => 0,
                    "Contributor" => 1,
                    _ => 2
                }
            };

            bool hasFullPermission = roleDefinition.Properties.Permissions.Any(r => r.Actions.Contains("*"));
            bool hasFullAuthPermission = roleDefinition.Properties.Permissions
                .Any(r => r.Actions.Contains("Microsoft.Authorization/*"));
            bool allAuthPermissionBlocked = roleDefinition.Properties.Permissions
                .Any(r => r.NotActions.Contains("Microsoft.Authorization/*"));
            bool deleteAuthPermissionBlocked = roleDefinition.Properties.Permissions
                .Any(r => r.NotActions.Contains("Microsoft.Authorization/*/Delete"));
            bool writeAuthPermissionBlocked = roleDefinition.Properties.Permissions
                .Any(r => r.NotActions.Contains("Microsoft.Authorization/*/Write"));

            effectiveRole.CanManageLocks = (hasFullPermission || hasFullAuthPermission)
                && !allAuthPermissionBlocked && !deleteAuthPermissionBlocked && !writeAuthPermissionBlocked;

            return effectiveRole;
        }
    }
}
