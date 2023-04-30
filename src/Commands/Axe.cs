using Beeching.Commands.Interfaces;
using Beeching.Helpers;
using Beeching.Models;
using Newtonsoft.Json;
using Polly;
using Spectre.Console;
using System.Net.Http.Headers;
using System.Text;

namespace Beeching.Commands
{
    internal class Axe : IAxe
    {
        private readonly HttpClient _client;

        public Axe(IHttpClientFactory httpClientFactory)
        {
            _client = httpClientFactory.CreateClient("ArmApi");
        }

        public async Task<int> AxeResources(AxeSettings settings)
        {
            // Get the access token and add it to the request header for the http client
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                await AuthHelper.GetAccessToken(settings.Debug)
            );

            AnsiConsole.Markup($"[green]=> Determining running user details[/]\n");

            (string, string) userInformation = AzCliHelper.GetSignedInUser();
            settings.UserId = userInformation.Item1;

            AnsiConsole.Markup($"[green]=> Running as user [white]{userInformation.Item2}[/] // [white]{userInformation.Item1}[/][/]\n");
            AnsiConsole.Markup($"[green]=> Determining subscription details[/]\n");

            settings.Subscription = AzCliHelper.GetSubscriptionId(settings);
            if (settings.Subscription == Guid.Empty)
            {
                return -1;
            }

            string name = AzCliHelper.GetSubscriptionName(settings.Subscription.ToString());

            AnsiConsole.Markup($"[green]=> Using subscription [white]{name}[/] // [white]{settings.Subscription}[/][/]\n");

            List<EffectiveRole> subscriptionRoles = await DetermineSubscriptionRoles(settings);

            if (subscriptionRoles.Count > 0)
            {
                string primaryRole = subscriptionRoles.OrderBy(r => r.Priority).First().Name;
                settings.SubscriptionRole = primaryRole;
                settings.IsSubscriptionRolePrivileged = primaryRole == "Owner" || primaryRole == "Contributor";
                AnsiConsole.Markup(
                    $"[green]=> Role [white]{settings.SubscriptionRole}[/] assigned on subscription which will be inherited by all resources[/]\n"
                );
                if (settings.IsSubscriptionRolePrivileged == false)
                {
                    AnsiConsole.Markup(
                        $"[green]=> No privileged subscription role assigned so axe may fail if resource specific role not assigned[/]\n"
                    );
                }
            }
            else
            {
                settings.SubscriptionRole = "None";
                AnsiConsole.Markup($"[green]=> No subscription roles assigned[/]\n");
            }

            // Get the list of resources to axe based on the supplied options
            List<Resource> resourcesToAxe = await GetAxeResourceList(settings);

            // If we are in what-if mode then just output the details of the resources to axe
            if (settings.WhatIf)
            {
                AnsiConsole.Markup($"[cyan]=> +++ RUNNING WHAT-IF +++[/]\n");
            }

            bool showedNoResources = false;
            int unlockedAxeCount = resourcesToAxe.Where(r => r.IsLocked == false).Count();
            if ((unlockedAxeCount == 0 && settings.Force == false) || resourcesToAxe.Count == 0)
            {
                AnsiConsole.Markup($"[cyan]=> No resources to axe[/]\n\n");
                showedNoResources = true;
            }
            else
            {
                foreach (var resource in resourcesToAxe)
                {
                    // Determine our primary role for the resource
                    string primaryResourceRole = string.Empty;
                    if (resource.Roles.Any())
                    {
                        primaryResourceRole = resource.Roles.OrderBy(r => r.Priority).First().Name;
                        AnsiConsole.Markup(
                            $"[green]=> Role [white]{primaryResourceRole}[/] assigned on resource [white]{resource.OutputMessage}[/][/]\n"
                        );
                    }
                    else
                    {
                        AnsiConsole.Markup($"[green]=> No roles assigned on resource [white]{resource.OutputMessage}[/][/]\n");
                    }

                    // Determine if we're skipping this resource because it's locked
                    resource.Skip = resource.IsLocked == true && Axe.ShouldSkipIfLocked(settings, resource);

                    string skipMessage =
                        resource.Skip == true ? " so will not be able to remove any locks - [white]SKIPPING[/]" : string.Empty;
                    string lockedState = resource.IsLocked == true ? "[red]LOCKED[/] " : string.Empty;

                    // Are we skipping this resource because it's locked?
                    if (resource.Skip == true)
                    {
                        AnsiConsole.Markup(
                            $"[green]=> Found [red]LOCKED[/] resource [white]{resource.OutputMessage}[/] but you do not have permission to remove locks - [white]SKIPPING[/][/]\n"
                        );
                    }
                    else if (resource.IsLocked == true && settings.Force == false)
                    {
                        resource.Skip = true;
                        AnsiConsole.Markup(
                            $"[green]=> Found [red]LOCKED[/] resource [white]{resource.OutputMessage}[/] which cannot be axed - [white]SKIPPING[/][/]\n"
                        );
                    }
                    else
                    {
                        bool axeFailWarning = settings.IsSubscriptionRolePrivileged == false && resource.Roles.Any() == false;
                        string locked = resource.IsLocked == true ? "LOCKED " : string.Empty;
                        string group = settings.ResourceGroups == true ? " and [red]ALL[/] resources within it" : string.Empty;
                        string axeFail = axeFailWarning == true ? " [red](may fail due to role)[/]" : string.Empty;
                        string axeAttemptMessage = axeFailWarning == true ? "ATTEMPT TO " : string.Empty;
                        AnsiConsole.Markup(
                            $"[green]=> [red]WILL {axeAttemptMessage}AXE {locked}[/]resource [white]{resource.OutputMessage}[/]{group}{axeFail}[/]\n"
                        );
                    }
                }
            }

            // If we're running what-if then just drop out here
            if (settings.WhatIf)
            {
                AnsiConsole.Markup($"[cyan]=> +++ WHAT-IF COMPLETE +++[/]\n");
                return 0;
            }

            // If we had some resources, but now we don't because they're locked then drop out here
            if (
                (unlockedAxeCount == 0 && settings.Force == false)
                || resourcesToAxe.Count == 0
                || resourcesToAxe.Where(r => r.Skip == false).Any() == false
            )
            {
                if (showedNoResources == false)
                {
                    AnsiConsole.Markup($"[cyan]=> No resources to axe[/]\n\n");
                }
                return 0;
            }

            // If you want to skip confirmation then go ahead - make my day, punk.
            if (settings.SkipConfirmation == false)
            {
                string title =
                    $"\nAre you sure you want to axe these {resourcesToAxe.Where(r => r.Skip == false).Count()} resources? [red](This cannot be undone)[/]";
                if (resourcesToAxe.Count == 1)
                {
                    title = "\nAre you sure you want to axe this resource? [red](This cannot be undone)[/]";
                }

                var confirm = AnsiConsole.Prompt(new SelectionPrompt<string>().Title(title).AddChoices(new[] { "Yes", "No" }));

                if (confirm == "No")
                {
                    AnsiConsole.Markup($"[green]=> Resource axing abandoned[/]\n\n");
                    return 0;
                }
            }
            else
            {
                AnsiConsole.Markup($"[green]=> Detected --yes. Skipping confirmation[/]\n\n");
            }

            int retryCount = 1;
            AxeStatus axeStatus = new();
            while (retryCount < (settings.MaxRetries + 1))
            {
                // Iterate through the list of resources to axe and make the delete requests
                axeStatus = await SwingTheAxe(settings, resourcesToAxe);

                if (axeStatus.AxeList.Count == 0)
                {
                    break;
                }

                AnsiConsole.Markup(
                    $"[green]=>[/] [red]Probably a dependency issue. Pausing for {settings.RetryPause} seconds and will retry. Attempt {retryCount} of {settings.MaxRetries}[/]\n"
                );
                await Task.Delay(settings.RetryPause * 1000);
                resourcesToAxe = axeStatus.AxeList;
                retryCount++;
            }

            if (retryCount < (settings.MaxRetries + 1) && axeStatus.Status == true)
            {
                AnsiConsole.Markup($"[green]=> All resources axed successfully[/]\n\n");
            }
            else if (retryCount < (settings.MaxRetries + 1) && axeStatus.Status == false)
            {
                AnsiConsole.Markup($"[green]=> Axe failed on some resources[/]\n\n");
            }
            else
            {
                AnsiConsole.Markup(
                    $"[green]=>[/] [red]Axe failed after {settings.MaxRetries} attempts. Try running the command again with --debug flag for more information[/]\n\n"
                );
            }

            return 0;
        }

        private async Task<AxeStatus> SwingTheAxe(AxeSettings settings, List<Resource> axeUriList)
        {
            AxeStatus axeStatus = new();
            foreach (var resource in axeUriList)
            {
                bool skipAxe = false;
                if (resource.IsLocked && settings.Force)
                {
                    foreach (var resourceLock in resource.ResourceLocks)
                    {
                        AnsiConsole.Markup(
                            $"[green]=> Removing {resourceLock.Scope} lock [white]{resourceLock.Name}[/] for [white]{resource.OutputMessage}[/][/]\n"
                        );
                        var lockResponse = await _client.DeleteAsync(
                            new Uri($"{resourceLock.Id}?api-version=2016-09-01", UriKind.Relative)
                        );
                        if (!lockResponse.IsSuccessStatusCode)
                        {
                            AnsiConsole.Markup($"[green]=>[/] [red]Failed to remove lock for {resource.OutputMessage}[/] - SKIPPING\n");
                            skipAxe = true;
                        }
                    }
                }

                // If we can't remove the lock then skip the axe
                if (skipAxe)
                {
                    continue;
                }

                string group = settings.ResourceGroups == true ? " and [red]ALL[/] resources within it" : string.Empty;

                // Output the details of the delete request
                AnsiConsole.Markup($"[green]=> [red]AXING[/] [white]{resource.OutputMessage}[/]{group}[/]\n");

                // Make the delete request
                var response = await _client.DeleteAsync(new Uri($"{resource.Id}?api-version={resource.ApiVersion}", UriKind.Relative));

                if (settings.Debug)
                {
                    AnsiConsole.Markup($"[green]=> Response status code is {response.StatusCode}[/]");
                    AnsiConsole.Markup($"[green]=> Response content: {await response.Content.ReadAsStringAsync()}[/]");
                }

                if (!response.IsSuccessStatusCode)
                {
                    string responseContent = await response.Content.ReadAsStringAsync();
                    if (responseContent.Contains("Please remove the lock and try again"))
                    {
                        AnsiConsole.Markup(
                            $"[green]=>[/] [red]Axe failed because the resource is [red]LOCKED[/]. Remove the lock and try again[/]\n"
                        );
                        axeStatus.Status = false;
                        continue;
                    }
                    else if (response.StatusCode.ToString() == "Forbidden")
                    {
                        AnsiConsole.Markup($"[green]=>[/] [red]Axe failed: Permission denied - [white]SKIPPING[/][/]\n");
                        axeStatus.Status = false;
                        continue;
                    }
                    else
                    {
                        AnsiConsole.Markup($"[green]=>[/] [red]Axe failed: {response.StatusCode}[/]\n");
                        axeStatus.AxeList.Add(resource);
                        axeStatus.Status = false;
                    }
                }
                else
                {
                    AnsiConsole.Markup($"[green]=> Resource axed successfully[/]\n");

                    if (resource.IsLocked && settings.Force)
                    {
                        foreach (var resourceLock in resource.ResourceLocks)
                        {
                            if (
                                (resourceLock.Scope == "resource group" && settings.ResourceGroups == false)
                                || resourceLock.Scope == "subscription"
                            )
                            {
                                AnsiConsole.Markup(
                                    $"[green]=> Reapplying {resourceLock.Scope} lock [white]{resourceLock.Name}[/] for [white]{resource.OutputMessage}[/][/]\n"
                                );

                                var createLockResponse = await _client.PutAsync(
                                    new Uri($"{resourceLock.Id}?api-version=2016-09-01", UriKind.Relative),
                                    new StringContent(JsonConvert.SerializeObject(resourceLock), Encoding.UTF8, "application/json")
                                );

                                if (!createLockResponse.IsSuccessStatusCode)
                                {
                                    AnsiConsole.Markup($"[green]=>[/] [red]Failed to reapply lock for {resource.OutputMessage}[/]\n");
                                    skipAxe = true;
                                }
                            }
                        }
                    }
                }
            }

            return axeStatus;
        }

        private async Task<string?> GetLatestApiVersion(AxeSettings settings, string provider, string type)
        {
            var apiVersion = await _client.GetAsync(
                $"subscriptions/{settings.Subscription}/providers/{provider}/resourceTypes?api-version=2021-04-01"
            );

            string apiJson = await apiVersion.Content.ReadAsStringAsync();
            List<ApiVersion> allApiVersions = new();

            if (apiJson.Contains("Microsoft.Resources' does not contain sufficient information to enforce access control policy"))
            {
                AnsiConsole.Markup(
                    $"[green]=>[/] [red]You do not have sufficient permissions determine latest API version. Please check your subscription permissions and try again[/]\n"
                );
                return null;
            }

            allApiVersions = JsonConvert.DeserializeObject<Dictionary<string, List<ApiVersion>>>(apiJson)!["value"];

            if (allApiVersions == null)
            {
                return null;
            }

            ApiVersion apiTypeVersion = allApiVersions.Where(x => x.ResourceType == type).First();

            return apiTypeVersion.DefaultApiVersion ?? apiTypeVersion.ApiVersions.First();
        }

        private async Task<List<Resource>> GetAxeResourceList(AxeSettings settings)
        {
            bool useNameFilter = !string.IsNullOrEmpty(settings.Name);
            List<Resource> resourcesFound = new();

            if (settings.ResourceGroups)
            {
                if (useNameFilter)
                {
                    List<string> names = new();

                    if (settings.Name.Contains(':'))
                    {
                        names = settings.Name.Split(':').ToList();
                    }
                    else
                    {
                        names.Add(settings.Name);
                    }

                    foreach (string name in names)
                    {
                        AnsiConsole.Markup($"[green]=> Searching for resource groups where name contains [white]{name}[/][/]\n");
                        HttpResponseMessage response = await _client.GetAsync(
                            $"subscriptions/{settings.Subscription}/resourcegroups?api-version=2021-04-01"
                        );
                        string jsonResponse = await response.Content.ReadAsStringAsync();
                        if (jsonResponse != null)
                        {
                            List<Resource> resources = JsonConvert.DeserializeObject<Dictionary<string, List<Resource>>>(jsonResponse)![
                                "value"
                            ];
                            resourcesFound.AddRange(resources.Where(x => x.Name.Contains(name, StringComparison.OrdinalIgnoreCase)));
                        }
                    }
                }
                else
                {
                    List<string> tag = settings.Tag.Split(':').ToList();

                    AnsiConsole.Markup(
                        $"[green]=> Searching for resource groups where tag [white]{tag[0]}[/] equals [white]{tag[1]}[/][/]\n"
                    );
                    HttpResponseMessage response = await _client.GetAsync(
                        $"subscriptions/{settings.Subscription}/resourcegroups?$filter=tagName eq '{tag[0]}' and tagValue eq '{tag[1]}'&api-version=2021-04-01"
                    );

                    string jsonResponse = await response.Content.ReadAsStringAsync();

                    if (jsonResponse != null)
                    {
                        resourcesFound.AddRange(JsonConvert.DeserializeObject<Dictionary<string, List<Resource>>>(jsonResponse)!["value"]);
                    }
                }
            }
            else
            {
                if (useNameFilter)
                {
                    List<string> names = new();

                    if (settings.Name.Contains(':'))
                    {
                        names = settings.Name.Split(':').ToList();
                    }
                    else
                    {
                        names.Add(settings.Name);
                    }

                    foreach (string name in names)
                    {
                        AnsiConsole.Markup($"[green]=> Searching for resources where name contains [white]{name}[/][/]\n");
                        HttpResponseMessage response = await _client.GetAsync(
                            $"subscriptions/{settings.Subscription}/resources?$filter=substringof('{name}',name)&api-version=2021-04-01"
                        );
                        string jsonResponse = await response.Content.ReadAsStringAsync();
                        if (jsonResponse != null)
                        {
                            List<Resource> resources = JsonConvert.DeserializeObject<Dictionary<string, List<Resource>>>(jsonResponse)![
                                "value"
                            ];
                            foreach (var resource in resources)
                            {
                                string[] sections = resource.Id.Split('/');
                                resource.ResourceGroup = $"/subscriptions/{settings.Subscription}/resourceGroups/{sections[4]}";
                                resourcesFound.Add(resource);
                            }
                        }
                    }
                }
                else
                {
                    // Split the tag into a key and value
                    List<string> tag = settings.Tag.Split(':').ToList();

                    AnsiConsole.Markup($"[green]=> Searching for resources where tag [white]{tag[0]}[/] equals [white]{tag[1]}[/][/]\n");
                    HttpResponseMessage response = await _client.GetAsync(
                        $"subscriptions/{settings.Subscription}/resources?$filter=tagName eq '{tag[0]}' and tagValue eq '{tag[1]}'&api-version=2021-04-01"
                    );
                    string jsonResponse = await response.Content.ReadAsStringAsync();

                    if (jsonResponse != null)
                    {
                        List<Resource> resources = JsonConvert.DeserializeObject<Dictionary<string, List<Resource>>>(jsonResponse)![
                            "value"
                        ];
                        foreach (var resource in resources)
                        {
                            string[] sections = resource.Id.Split('/');
                            resource.ResourceGroup = $"/subscriptions/{settings.Subscription}/resourceGroups/{sections[4]}";
                            resourcesFound.Add(resource);
                        }
                    }
                }

                // Do we need to filter the resource types?
                if (!string.IsNullOrEmpty(settings.ResourceTypes))
                {
                    List<string> allowedTypes = settings.ResourceTypes.Split(':').ToList();
                    AnsiConsole.Markup($"[green]=> Restricting resource types to:[/]\n");
                    foreach (string type in allowedTypes)
                    {
                        AnsiConsole.Markup($"\t- [white]{type}[/]\n");
                    }
                    resourcesFound = resourcesFound.Where(r => allowedTypes.Contains(r.Type)).ToList();
                }
            }

            // Do we need to filter exclusions?
            if (!string.IsNullOrEmpty(settings.Exclude))
            {
                List<string> exclusions = settings.Exclude.Split(':').ToList();
                List<Resource> filteredResources = resourcesFound.Where(r => !exclusions.Contains(r.Name)).ToList();
                foreach (var resource in resourcesFound.Except(filteredResources))
                {
                    AnsiConsole.Markup($"[green]=> Excluding [white]{resource.Name}[/][/]\n");
                }
                resourcesFound = filteredResources;
            }

            // Now we have our actual list of resources to axe, let's get the latest API version for each resource type
            foreach (var resource in resourcesFound)
            {
                string[] sections = resource.Id.Split('/');
                string resourceGroup = sections[4];
                string provider;
                string resourceType;

                if (!settings.ResourceGroups)
                {
                    provider = sections[6];
                    resourceType = sections[7];
                    resource.OutputMessage =
                        $"[white]{resource.Type} {resource.Name}[/] [green]in resource group[/] [white]{resourceGroup}[/]";
                }
                else
                {
                    provider = "Microsoft.Resources";
                    resourceType = "resourceGroups";
                    resource.OutputMessage = $"[green]group[/] [white]{resource.Name}[/]";
                }

                string? apiVersion = await GetLatestApiVersion(settings, provider, resourceType);

                if (apiVersion == null)
                {
                    AnsiConsole.Markup($"[green]=> Unable to get latest API version for {resource.OutputMessage} so will exclude[/]\n");
                }

                resource.ApiVersion = apiVersion;
            }

            // Remove any resources that we couldn't get an API version for
            resourcesFound = resourcesFound.Except(resourcesFound.Where(r => string.IsNullOrEmpty(r.ApiVersion)).ToList()).ToList();

            await DetermineLocks(settings, resourcesFound);

            await DetermineRoles(settings, resourcesFound);

            // Return whatever is left
            return resourcesFound;
        }

        private async Task<List<EffectiveRole>> DetermineSubscriptionRoles(AxeSettings settings)
        {
            List<EffectiveRole> subscriptionRoles = new();
            string roleId =
                $"subscriptions/{settings.Subscription}/providers/Microsoft.Authorization/roleAssignments?$filter=principalId eq '{settings.UserId}'&api-version=2022-04-01";
            HttpResponseMessage response = await _client.GetAsync(roleId);
            if (response.IsSuccessStatusCode)
            {
                string jsonResponse = await response.Content.ReadAsStringAsync();
                if (jsonResponse != null)
                {
                    List<dynamic> roles = JsonConvert.DeserializeObject<Dictionary<string, List<dynamic>>>(jsonResponse)!["value"];
                    foreach (var role in roles)
                    {
                        RoleDefinition roleDefinition = await GetRoleDefinition(role.properties.roleDefinitionId.ToString());

                        if (role.properties.scope != $"/subscriptions/{settings.Subscription}")
                        {
                            continue;
                        }

                        EffectiveRole effectiveRole =
                            new()
                            {
                                RoleDefinitionId = roleDefinition.Name,
                                Scope = role.properties.scope,
                                ScopeType = "subscription",
                                Name = roleDefinition.Properties.RoleName,
                                Type = roleDefinition.Properties.Type
                            };

                        if (effectiveRole.Name == "Owner")
                        {
                            effectiveRole.Priority = 0;
                        }
                        else if (effectiveRole.Name == "Contributor")
                        {
                            effectiveRole.Priority = 1;
                        }
                        else
                        {
                            effectiveRole.Priority = 2;
                        }

                        bool hasFullPermission = roleDefinition.Properties.Permissions.Where(r => r.Actions.Contains("*")).Any();
                        bool hasFullAuthPermission = roleDefinition.Properties.Permissions
                            .Where(r => r.Actions.Contains("Microsoft.Authorization/*"))
                            .Any();
                        bool allAuthPermissionBlocked = roleDefinition.Properties.Permissions
                            .Where(r => r.NotActions.Contains("Microsoft.Authorization/*"))
                            .Any();
                        bool deleteAuthPermissionBlocked = roleDefinition.Properties.Permissions
                            .Where(r => r.NotActions.Contains("Microsoft.Authorization/*/Delete"))
                            .Any();
                        bool writeAuthPermissionBlocked = roleDefinition.Properties.Permissions
                            .Where(r => r.NotActions.Contains("Microsoft.Authorization/*/Write"))
                            .Any();

                        if (
                            (hasFullPermission || hasFullAuthPermission)
                            && (!allAuthPermissionBlocked && !deleteAuthPermissionBlocked && !writeAuthPermissionBlocked)
                        )
                        {
                            effectiveRole.CanManageLocks = true;
                        }

                        subscriptionRoles.Add(effectiveRole);
                    }
                }
            }

            return subscriptionRoles;
        }

        private async Task DetermineRoles(AxeSettings settings, List<Resource> resources)
        {
            AnsiConsole.Markup($"[green]=> Checking resources for role assignments[/]\n");

            foreach (Resource resource in resources)
            {
                string roleId =
                    $"{resource.Id}/providers/Microsoft.Authorization/roleAssignments?$filter=principalId eq '{settings.UserId}'&api-version=2022-04-01";
                HttpResponseMessage response = await _client.GetAsync(roleId);
                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    if (jsonResponse != null)
                    {
                        List<dynamic> roles = JsonConvert.DeserializeObject<Dictionary<string, List<dynamic>>>(jsonResponse)!["value"];
                        foreach (var role in roles)
                        {
                            RoleDefinition roleDefinition = await GetRoleDefinition(role.properties.roleDefinitionId.ToString());

                            if (role.properties.scope == $"/subscriptions/{settings.Subscription}")
                            {
                                continue;
                            }

                            string[] scopeSections = role.properties.scope.ToString().Split('/');

                            EffectiveRole effectiveRole =
                                new()
                                {
                                    RoleDefinitionId = roleDefinition.Name,
                                    Scope = role.properties.scope,
                                    ScopeType = scopeSections.Length > 5 ? "resource" : "resource group",
                                    Name = roleDefinition.Properties.RoleName,
                                    Type = roleDefinition.Properties.Type
                                };

                            if (effectiveRole.Name == "Owner")
                            {
                                effectiveRole.Priority = 0;
                            }
                            else if (effectiveRole.Name == "Contributor")
                            {
                                effectiveRole.Priority = 1;
                            }
                            else
                            {
                                effectiveRole.Priority = 2;
                            }

                            bool hasFullPermission = roleDefinition.Properties.Permissions.Where(r => r.Actions.Contains("*")).Any();
                            bool hasFullAuthPermission = roleDefinition.Properties.Permissions
                                .Where(r => r.Actions.Contains("Microsoft.Authorization/*"))
                                .Any();
                            bool allAuthPermissionBlocked = roleDefinition.Properties.Permissions
                                .Where(r => r.NotActions.Contains("Microsoft.Authorization/*"))
                                .Any();
                            bool deleteAuthPermissionBlocked = roleDefinition.Properties.Permissions
                                .Where(r => r.NotActions.Contains("Microsoft.Authorization/*/Delete"))
                                .Any();
                            bool writeAuthPermissionBlocked = roleDefinition.Properties.Permissions
                                .Where(r => r.NotActions.Contains("Microsoft.Authorization/*/Write"))
                                .Any();

                            if (
                                (hasFullPermission || hasFullAuthPermission)
                                && (!allAuthPermissionBlocked && !deleteAuthPermissionBlocked && !writeAuthPermissionBlocked)
                            )
                            {
                                effectiveRole.CanManageLocks = true;
                            }

                            resource.Roles.Add(effectiveRole);
                        }
                    }
                }
            }
        }

        private async Task<RoleDefinition> GetRoleDefinition(string roleDefinitionId)
        {
            string[] sections = roleDefinitionId.Split('/');
            string roleId = sections[^1];
            string roleDefinition = $"providers/Microsoft.Authorization/roleDefinitions/{roleId}?api-version=2022-04-01";
            HttpResponseMessage response = await _client.GetAsync(roleDefinition);
            if (response.IsSuccessStatusCode)
            {
                string jsonResponse = await response.Content.ReadAsStringAsync();
                if (jsonResponse != null)
                {
                    return JsonConvert.DeserializeObject<RoleDefinition>(jsonResponse)!;
                }
            }
            return new RoleDefinition();
        }

        private async Task DetermineLocks(AxeSettings settings, List<Resource> resources)
        {
            AnsiConsole.Markup($"[green]=> Checking resources for locks[/]\n");

            List<ResourceLock> resourceLocks = new();

            if (settings.Force == true)
            {
                AnsiConsole.Markup($"[green]=> Detected --force. Resource locks will be removed and reapplied where possible[/]\n");
            }

            string locks = $"/subscriptions/{settings.Subscription}/providers/Microsoft.Authorization/locks?api-version=2016-09-01";

            var response = await _client.GetAsync(locks);
            if (response.IsSuccessStatusCode)
            {
                string responseContent = await response.Content.ReadAsStringAsync();
                if (responseContent != null)
                {
                    resourceLocks.AddRange(
                        JsonConvert.DeserializeObject<Dictionary<string, List<ResourceLock>>>(responseContent)!["value"]
                    );

                    foreach (var resource in resources)
                    {
                        string[] sections = resource.Id.Split('/');
                        foreach (var resourceLock in resourceLocks)
                        {
                            string lockId = resourceLock.Id.ToLower();
                            string resourceGroupId =
                                $"/subscriptions/{settings.Subscription}/resourceGroups/{sections[4]}/providers/{resourceLock.Type}/{resourceLock.Name}".ToLower();
                            string subscriptionId =
                                $"/subscriptions/{settings.Subscription}/providers/{resourceLock.Type}/{resourceLock.Name}".ToLower();

                            if (lockId.StartsWith(resource.Id.ToLower()))
                            {
                                resourceLock.Scope =
                                    resource.Type.ToLower() == "microsoft.resources/resourcegroups" ? "resource group" : "resource";
                                resource.ResourceLocks.Add(resourceLock);
                                resource.IsLocked = true;
                            }
                            else if (lockId == resourceGroupId)
                            {
                                resourceLock.Scope = "resource group";
                                resource.ResourceLocks.Add(resourceLock);
                                resource.IsLocked = true;
                            }
                            else if (lockId == subscriptionId)
                            {
                                resourceLock.Scope = "subscription";
                                resource.ResourceLocks.Add(resourceLock);
                                resource.IsLocked = true;
                            }
                        }
                        if (settings.Force == false && resource.IsLocked == true)
                        {
                            AnsiConsole.Markup(
                                $"[green]=> Found [red]LOCKED[/] resource {resource.OutputMessage} which cannot be deleted[/] - [white]SKIPPING[/]\n"
                            );
                        }
                    }
                }
            }
        }

        private static bool ShouldSkipIfLocked(AxeSettings settings, Resource resource)
        {
            // Find out what kind of powers we have
            bool hasSubscriptionLockPowers = settings.SubscriptionRole == "Owner";
            bool hasResourceLockPowers = resource.Roles.Where(r => r.CanManageLocks == true).Any();

            // If we don't have subscription lock powers and we don't have resource lock powers then we're not good
            if (hasSubscriptionLockPowers == false && hasResourceLockPowers == false)
            {
                return true;
            }

            // If we have subscription lock powers, we can remove any lock so we're good
            if (hasSubscriptionLockPowers == true)
            {
                return false;
            }

            // Find out if we have subscription level locks
            bool hasSubscriptionLocks = resource.ResourceLocks.Where(r => r.Scope == "subscription").Any();

            // We don't have subscription lock powers so if the locks are at the subscription level then we're not good
            if (hasSubscriptionLocks == true)
            {
                return true;
            }

            // We do have resource lock powers and we're dealing with resource groups so we're good
            if (settings.ResourceGroups == true)
            {
                return false;
            }

            // Find out what kind of locks we have at the group and resource level
            bool hasGroupLocks = resource.ResourceLocks.Where(r => r.Scope == "resource group").Any();
            bool hasResourceLocks = resource.ResourceLocks.Where(r => r.Scope == "resource").Any();

            // We have resource lock powers and the resource is locked at the resource level so we're good
            if (hasGroupLocks == false)
            {
                return false;
            }

            // Find out if the role scope is for the resource group
            bool hasOwnerOnGroup = resource.Roles.Where(r => r.ScopeType == "resource group" && r.Name == "Owner").Any();

            // We have resource lock powers and the resource is locked at the group level
            if (hasGroupLocks == true && hasOwnerOnGroup == true)
            {
                return false;
            }

            // Has owner on resource but lock is on group lands here so we're not good
            return true;
        }

        public static IAsyncPolicy<HttpResponseMessage> GetRetryAfterPolicy()
        {
            return Policy
                .HandleResult<HttpResponseMessage>(msg => msg.Headers.TryGetValues("RetryAfter", out var _))
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: (_, response, _) =>
                        response.Result.Headers.TryGetValues("RetryAfter", out var seconds)
                            ? TimeSpan.FromSeconds(int.Parse(seconds.First()))
                            : TimeSpan.FromSeconds(5),
                    onRetryAsync: (msg, time, retries, context) => Task.CompletedTask
                );
        }
    }
}
