using Beeching.Commands.Interfaces;
using Beeching.Helpers;
using Beeching.Models;
using Spectre.Console;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Beeching.Commands
{
    internal class Axe : IAxe
    {
        private readonly ArmClient _armClient;
        private readonly ResourceDiscoveryHelper _discovery;
        private readonly RoleHelper _roleHelper;
        private readonly LockHelper _lockHelper;

        public Axe(IHttpClientFactory httpClientFactory)
        {
            _armClient = new ArmClient(httpClientFactory);
            _discovery = new ResourceDiscoveryHelper(_armClient);
            _roleHelper = new RoleHelper(_armClient);
            _lockHelper = new LockHelper(_armClient);
        }

        public async Task<int> AxeResources(AxeSettings settings)
        {
            await _armClient.InitializeAsync(settings.Debug);

            var context = new AxeContext(settings);

            AnsiConsole.Markup("[green]=> Determining running user details[/]\n");

            (string userId, string displayName) = AzCliHelper.GetSignedInUser();
            context.UserId = userId;

            AnsiConsole.Markup($"[green]=> Running as user [white]{displayName}[/] // [white]{userId}[/][/]\n");
            AnsiConsole.Markup("[green]=> Determining subscription details[/]\n");

            settings.Subscription = AzCliHelper.GetSubscriptionId(settings);
            if (settings.Subscription == Guid.Empty)
            {
                return -1;
            }

            string name = AzCliHelper.GetSubscriptionName(settings.Subscription.ToString());

            AnsiConsole.Markup($"[green]=> Using subscription [white]{name}[/] // [white]{settings.Subscription}[/][/]\n");

            List<EffectiveRole> subscriptionRoles = await _roleHelper.DetermineSubscriptionRoles(context);

            if (subscriptionRoles.Count > 0)
            {
                string primaryRole = subscriptionRoles.OrderBy(r => r.Priority).First().Name;
                context.SubscriptionRole = primaryRole;
                context.IsSubscriptionRolePrivileged = primaryRole is "Owner" or "Contributor";
                AnsiConsole.Markup(
                    $"[green]=> Role [white]{context.SubscriptionRole}[/] assigned on subscription which will be inherited by all resources[/]\n"
                );
                if (!context.IsSubscriptionRolePrivileged)
                {
                    AnsiConsole.Markup(
                        "[green]=> No privileged subscription role assigned so axe may fail if resource specific role not assigned[/]\n"
                    );
                }
            }
            else
            {
                AnsiConsole.Markup("[green]=> No subscription roles assigned[/]\n");
            }

            List<Resource> resourcesToAxe = await _discovery.DiscoverResources(context);

            await _lockHelper.DetermineLocks(context, resourcesToAxe);
            await _roleHelper.DetermineResourceRoles(context, resourcesToAxe);

            if (settings.WhatIf)
            {
                AnsiConsole.Markup("[cyan]=> +++ RUNNING WHAT-IF +++[/]\n");
            }

            bool showedNoResources = false;
            int unlockedAxeCount = resourcesToAxe.Count(r => !r.IsLocked);
            if ((unlockedAxeCount == 0 && !settings.Force) || resourcesToAxe.Count == 0)
            {
                AnsiConsole.Markup("[cyan]=> No resources to axe[/]\n\n");
                showedNoResources = true;
            }
            else
            {
                foreach (var resource in resourcesToAxe)
                {
                    DisplayResourceRoleInfo(resource);
                    EvaluateSkipStatus(context, resource);
                    DisplayResourceAction(context, resource);
                }
            }

            if (settings.WhatIf)
            {
                AnsiConsole.Markup("[cyan]=> +++ WHAT-IF COMPLETE +++[/]\n");
                return 0;
            }

            if (
                (unlockedAxeCount == 0 && !settings.Force)
                || resourcesToAxe.Count == 0
                || !resourcesToAxe.Any(r => !r.Skip)
            )
            {
                if (!showedNoResources)
                {
                    AnsiConsole.Markup("[cyan]=> No resources to axe[/]\n\n");
                }
                return 0;
            }

            if (!ConfirmAxe(settings, resourcesToAxe))
            {
                return 0;
            }

            return await ExecuteAxeWithRetries(context, resourcesToAxe);
        }

        private static void DisplayResourceRoleInfo(Resource resource)
        {
            if (resource.Roles.Any())
            {
                string role = resource.Roles.OrderBy(r => r.Priority).First().Name;
                AnsiConsole.Markup(
                    $"[green]=> Role [white]{role}[/] assigned on resource [white]{resource.OutputMessage}[/][/]\n"
                );
            }
            else
            {
                AnsiConsole.Markup($"[green]=> No roles assigned on resource [white]{resource.OutputMessage}[/][/]\n");
            }
        }

        private static void EvaluateSkipStatus(AxeContext context, Resource resource)
        {
            resource.Skip = resource.IsLocked && LockHelper.ShouldSkipIfLocked(context, resource);

            if (!resource.Skip && resource.IsLocked && !context.Settings.Force)
            {
                resource.Skip = true;
            }
        }

        private static void DisplayResourceAction(AxeContext context, Resource resource)
        {
            if (resource.Skip && resource.IsLocked && !context.Settings.Force)
            {
                AnsiConsole.Markup(
                    $"[green]=> Found [red]LOCKED[/] resource [white]{resource.OutputMessage}[/] which cannot be axed - [white]SKIPPING[/][/]\n"
                );
            }
            else if (resource.Skip)
            {
                AnsiConsole.Markup(
                    $"[green]=> Found [red]LOCKED[/] resource [white]{resource.OutputMessage}[/] but you do not have permission to remove locks - [white]SKIPPING[/][/]\n"
                );
            }
            else
            {
                bool axeFailWarning = !context.IsSubscriptionRolePrivileged && !resource.Roles.Any();
                string locked = resource.IsLocked ? "LOCKED " : string.Empty;
                string group = context.Settings.ResourceGroups ? " and [red]ALL[/] resources within it" : string.Empty;
                string axeFail = axeFailWarning ? " [red](may fail due to role)[/]" : string.Empty;
                string axeAttemptMessage = axeFailWarning ? "ATTEMPT TO " : string.Empty;
                AnsiConsole.Markup(
                    $"[green]=> [red]WILL {axeAttemptMessage}AXE {locked}[/]resource [white]{resource.OutputMessage}[/]{group}{axeFail}[/]\n"
                );
            }
        }

        private static bool ConfirmAxe(AxeSettings settings, List<Resource> resources)
        {
            if (!settings.SkipConfirmation)
            {
                int axeableCount = resources.Count(r => !r.Skip);
                string title = axeableCount == 1
                    ? "\nAre you sure you want to axe this resource? [red](This cannot be undone)[/]"
                    : $"\nAre you sure you want to axe these {axeableCount} resources? [red](This cannot be undone)[/]";

                var confirm = AnsiConsole.Prompt(new SelectionPrompt<string>().Title(title).AddChoices(new[] { "Yes", "No" }));

                if (confirm == "No")
                {
                    AnsiConsole.Markup("[green]=> Resource axing abandoned[/]\n\n");
                    return false;
                }
            }
            else
            {
                AnsiConsole.Markup("[green]=> Detected --yes. Skipping confirmation[/]\n\n");
            }
            return true;
        }

        private async Task<int> ExecuteAxeWithRetries(AxeContext context, List<Resource> resourcesToAxe)
        {
            var settings = context.Settings;
            int retryCount = 1;
            AxeStatus axeStatus = new();

            while (retryCount < (settings.MaxRetries + 1))
            {
                axeStatus = await SwingTheAxe(context, resourcesToAxe);

                if (axeStatus.AxeList.Count == 0)
                {
                    break;
                }

                AnsiConsole.Markup(
                    $"[green]=>[/] [red]Possibly a dependency issue. Pausing for {settings.RetryPause} seconds and will retry. Attempt {retryCount} of {settings.MaxRetries}[/]\n"
                );
                await Task.Delay(settings.RetryPause * 1000);
                resourcesToAxe = axeStatus.AxeList;
                retryCount++;
            }

            if (retryCount < (settings.MaxRetries + 1) && axeStatus.Status)
            {
                AnsiConsole.Markup("[green]=> All resources axed successfully[/]\n\n");
            }
            else if (retryCount < (settings.MaxRetries + 1) && !axeStatus.Status)
            {
                AnsiConsole.Markup("[green]=> Axe failed on some resources[/]\n\n");
            }
            else
            {
                AnsiConsole.Markup(
                    $"[green]=>[/] [red]Axe failed after {settings.MaxRetries} attempts. Try running the command again with --debug flag for more information[/]\n\n"
                );
            }

            return 0;
        }

        private async Task<AxeStatus> SwingTheAxe(AxeContext context, List<Resource> axeList)
        {
            AxeStatus axeStatus = new();
            foreach (var resource in axeList)
            {
                if (resource.IsLocked && context.Settings.Force)
                {
                    if (!await TryRemoveLocks(context, resource))
                    {
                        axeStatus.Status = false;
                        continue;
                    }
                }

                string group = context.Settings.ResourceGroups ? " and [red]ALL[/] resources within it" : string.Empty;
                AnsiConsole.Markup($"[green]=> [red]AXING[/] [white]{resource.OutputMessage}[/]{group}[/]\n");

                var response = await _armClient.DeleteAsync($"{resource.Id}?api-version={resource.ApiVersion}");

                if (context.Settings.Debug)
                {
                    AnsiConsole.Markup($"[green]=> Response status code is {response.StatusCode}[/]\n");
                    AnsiConsole.Markup($"[green]=> Response content: {await response.Content.ReadAsStringAsync()}[/]\n");
                }

                if (!response.IsSuccessStatusCode)
                {
                    HandleDeleteFailure(response, resource, axeStatus);
                }
                else
                {
                    AnsiConsole.Markup("[green]=> Resource axed successfully[/]\n");
                    await ReapplyLocksIfNeeded(context, resource);
                }
            }

            return axeStatus;
        }

        private async Task<bool> TryRemoveLocks(AxeContext context, Resource resource)
        {
            foreach (var resourceLock in resource.ResourceLocks)
            {
                int retryCount = 1;
                bool lockRemoved = false;

                while (retryCount < (context.Settings.MaxRetries + 1))
                {
                    AnsiConsole.Markup(
                        $"[green]=> Attempting to remove {resourceLock.Scope} lock [white]{resourceLock.Name}[/] for [white]{resource.OutputMessage}[/][/]\n"
                    );

                    var lockResponse = await _armClient.DeleteAsync($"{resourceLock.Id}?api-version=2016-09-01");

                    if (lockResponse.IsSuccessStatusCode)
                    {
                        lockRemoved = true;
                        break;
                    }

                    AnsiConsole.Markup(
                        $"[green]=>[/] [red]Failed to remove lock for {resource.OutputMessage}[/]. Pausing for {context.Settings.RetryPause} seconds and will retry. Attempt {retryCount} of {context.Settings.MaxRetries}[/]\n"
                    );
                    await Task.Delay(context.Settings.RetryPause * 1000);
                    retryCount++;
                }

                if (lockRemoved)
                {
                    AnsiConsole.Markup("[green]=> Lock removed successfully[/]\n");
                }
                else
                {
                    AnsiConsole.Markup($"[green]=>[/] [red]Failed to remove lock for {resource.OutputMessage}[/] - SKIPPING\n");
                    return false;
                }
            }

            return true;
        }

        private static void HandleDeleteFailure(HttpResponseMessage response, Resource resource, AxeStatus axeStatus)
        {
            string responseContent = response.Content.ReadAsStringAsync().Result;

            if (responseContent.Contains("Please remove the lock and try again"))
            {
                AnsiConsole.Markup(
                    "[green]=>[/] [red]Axe failed because the resource is [red]LOCKED[/]. Remove the lock and try again[/]\n"
                );
                axeStatus.Status = false;
            }
            else if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                AnsiConsole.Markup("[green]=>[/] [red]Axe failed: Permission denied - [white]SKIPPING[/][/]\n");
                axeStatus.Status = false;
            }
            else if (response.StatusCode == HttpStatusCode.NotFound)
            {
                AnsiConsole.Markup("[green]=>[/] [red]Axe failed: Resource already axed - [white]SKIPPING[/][/]\n");
                axeStatus.Status = false;
            }
            else
            {
                AnsiConsole.Markup($"[green]=>[/] [red]Axe failed: {response.StatusCode}[/]\n");
                axeStatus.AxeList.Add(resource);
                axeStatus.Status = false;
            }
        }

        private async Task ReapplyLocksIfNeeded(AxeContext context, Resource resource)
        {
            if (!resource.IsLocked || !context.Settings.Force)
            {
                return;
            }

            foreach (var resourceLock in resource.ResourceLocks)
            {
                if (
                    (resourceLock.Scope == "resource group" && !context.Settings.ResourceGroups)
                    || resourceLock.Scope == "subscription"
                )
                {
                    AnsiConsole.Markup(
                        $"[green]=> Reapplying {resourceLock.Scope} lock [white]{resourceLock.Name}[/] for [white]{resource.OutputMessage}[/][/]\n"
                    );

                    var createLockResponse = await _armClient.PutAsync(
                        $"{resourceLock.Id}?api-version=2016-09-01",
                        new StringContent(JsonSerializer.Serialize(resourceLock), Encoding.UTF8, "application/json")
                    );

                    if (!createLockResponse.IsSuccessStatusCode)
                    {
                        AnsiConsole.Markup($"[green]=>[/] [red]Failed to reapply lock for {resource.OutputMessage}[/]\n");
                    }
                }
            }
        }
    }
}
