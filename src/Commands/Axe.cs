using Beeching.Commands.Interfaces;
using Beeching.Helpers;
using Beeching.Models;
using Newtonsoft.Json;
using Polly;
using Spectre.Console;
using System.Net.Http.Headers;
using System.Net.Sockets;
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
                settings.PrivilegedUserSubscriptionRole = primaryRole;
                AnsiConsole.Markup($"[green]=> Role [white]{settings.PrivilegedUserSubscriptionRole}[/] found on subscription[/]\n");
            }
            else
            {
                settings.PrivilegedUserSubscriptionRole = "None";
                AnsiConsole.Markup($"[green]=> No subscription roles found[/]\n");
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
                    string primaryRole = string.Empty;
                    if (resource.Roles.Count > 0)
                    {
                        primaryRole = resource.Roles.OrderBy(r => r.Priority).First().Name;
                    }

                    // Determine if we can manage locks on the resource
                    bool canManageLocks =
                        subscriptionRoles.Where(r => r.CanManageLocks == true).Any()
                        || resource.Roles.Where(r => r.CanManageLocks == true).Any();

                    if (!string.IsNullOrEmpty(primaryRole))
                    {
                        AnsiConsole.Markup(
                            $"[green]=> Role [white]{primaryRole}[/] found on resource [white]{resource.OutputMessage}[/][/]\n"
                        );
                    }
                    else
                    {
                        AnsiConsole.Markup($"[green]=> No roles found on resource [white]{resource.OutputMessage}[/][/]\n");
                    }

                    resource.Skip = resource.IsLocked == true && canManageLocks == false ? true : false;
                    string skipMessage =
                        resource.Skip == true ? " so will not be able to remove any locks - [white]SKIPPING[/]" : string.Empty;
                    string lockedState = resource.IsLocked == true ? "[red]LOCKED[/] " : string.Empty;

                    if (resource.Skip == false)
                    {
                        string locked = resource.IsLocked == true ? "LOCKED " : string.Empty;
                        string group = settings.ResourceGroups == true ? " and [red]ALL[/] resources within it" : string.Empty;
                        AnsiConsole.Markup($"[green]=> [red]WILL AXE {locked}[/]resource [white]{resource.OutputMessage}[/]{group}[/]\n");
                    } else
                    {
                        AnsiConsole.Markup($"[green]=> Found [red]LOCKED[/] resource [white]{resource.OutputMessage}[/] but you do not have permission to remove locks - [white]SKIPPING[/][/]\n");
                    }
                }
            }

            if (settings.WhatIf)
            {
                AnsiConsole.Markup($"[cyan]=> +++ WHAT-IF COMPLETE +++[/]\n");
                return 0;
            }

            if (
                (unlockedAxeCount == 0 && settings.Force == false)
                || resourcesToAxe.Count == 0
                || resourcesToAxe.Where(r => r.Skip == false).Count() == 0
            )
            {
                if (showedNoResources == false)
                {
                    AnsiConsole.Markup($"[cyan]=> No resources to axe[/]\n\n");
                }
                return 0;
            }

            // If we want to skip confirmation then be my guest
            if (!settings.SkipConfirmation)
            {
                string title = $"\nAre you sure you want to axe these {resourcesToAxe.Count} resources? [red](This cannot be undone)[/]";
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
                    $"[green]=>[/] [red]Probably a dependency issue. Pausing for {settings.RetryPause} seconds and will retry. Attempt {retryCount} of {settings.MaxRetries}[/]\n\n"
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
                            $"[green]=>[/] [red]Axe failed because the resource is locked. Remove the lock and try again[/]\n"
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
                                ScopeType = "Subscription",
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

                            EffectiveRole effectiveRole =
                                new()
                                {
                                    RoleDefinitionId = roleDefinition.Name,
                                    Scope = role.properties.scope,
                                    ScopeType = "Resource",
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
                                $"[green]=> Found resource {resource.OutputMessage} but it's locked and cannot be deleted[/]\n"
                            );
                        }
                    }
                }
            }
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
