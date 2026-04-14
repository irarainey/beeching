using Beeching.Models;
using Spectre.Console;

namespace Beeching.Helpers
{
    internal class LockHelper
    {
        private readonly IArmClient _armClient;

        public LockHelper(IArmClient armClient)
        {
            _armClient = armClient;
        }

        public async Task DetermineLocks(AxeContext context, List<Resource> resources)
        {
            AnsiConsole.Markup("[green]=> Checking resources for locks[/]\n");

            if (context.Settings.Force)
            {
                AnsiConsole.Markup("[green]=> Detected --force. Resource locks will be removed and reapplied where possible[/]\n");
            }

            string uri = $"/subscriptions/{context.Settings.Subscription}/providers/Microsoft.Authorization/locks?api-version=2016-09-01";

            var resourceLocks = await _armClient.GetListAsync<ResourceLock>(uri);

            foreach (var resource in resources)
            {
                string[] sections = resource.Id.Split('/');
                if (sections.Length < 5)
                {
                    continue;
                }

                foreach (var resourceLock in resourceLocks)
                {
                    string lockId = resourceLock.Id.ToLower();
                    string resourceGroupId =
                        $"/subscriptions/{context.Settings.Subscription}/resourceGroups/{sections[4]}/providers/{resourceLock.Type}/{resourceLock.Name}".ToLower();
                    string subscriptionId =
                        $"/subscriptions/{context.Settings.Subscription}/providers/{resourceLock.Type}/{resourceLock.Name}".ToLower();

                    string? scope = null;

                    if (lockId.StartsWith(resource.Id.ToLower()))
                    {
                        scope = resource.Type.Equals("microsoft.resources/resourcegroups", StringComparison.OrdinalIgnoreCase)
                            ? "resource group"
                            : "resource";
                    }
                    else if (lockId == resourceGroupId)
                    {
                        scope = "resource group";
                    }
                    else if (lockId == subscriptionId)
                    {
                        scope = "subscription";
                    }

                    if (scope != null)
                    {
                        var lockCopy = new ResourceLock
                        {
                            Properties = resourceLock.Properties,
                            Id = resourceLock.Id,
                            Type = resourceLock.Type,
                            Name = resourceLock.Name,
                            Scope = scope
                        };
                        resource.ResourceLocks.Add(lockCopy);
                        resource.IsLocked = true;
                    }
                }

            }
        }

        public static bool ShouldSkipIfLocked(AxeContext context, Resource resource)
        {
            bool hasSubscriptionLockPowers = context.SubscriptionRole == "Owner";
            bool hasResourceLockPowers = resource.Roles.Any(r => r.CanManageLocks);

            if (!hasSubscriptionLockPowers && !hasResourceLockPowers)
            {
                return true;
            }

            if (hasSubscriptionLockPowers)
            {
                return false;
            }

            if (resource.ResourceLocks.Any(r => r.Scope == "subscription"))
            {
                return true;
            }

            if (context.Settings.ResourceGroups)
            {
                return false;
            }

            bool hasGroupLocks = resource.ResourceLocks.Any(r => r.Scope == "resource group");

            if (!hasGroupLocks)
            {
                return false;
            }

            return !resource.Roles.Any(r => r.ScopeType == "resource group" && r.Name == "Owner");
        }
    }
}
