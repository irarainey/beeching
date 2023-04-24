using Beeching.Commands.Interfaces;
using Beeching.Helpers;
using Beeching.Models;
using Newtonsoft.Json;
using Polly;
using Spectre.Console;
using System.Net.Http.Headers;
using System.Resources;
using System.Text;

namespace Beeching.Commands
{
    internal sealed class AzureAxe : IAzureAxe
    {
        private readonly HttpClient _client;

        public AzureAxe(IHttpClientFactory httpClientFactory)
        {
            _client = httpClientFactory.CreateClient("AzApi");
        }

        public async Task<int> AxeResources(AxeSettings settings)
        {
            // Get the access token and add it to the request header for the http client
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                await AuthHelper.GetAccessToken(settings.Debug)
            );

            // Get the list of resources to axe based on the supplied options
            List<Resource> resourcesToAxe = await GetAxeResourceList(settings);

            // If we are in what-if mode then just output the details of the resources to axe
            if (settings.WhatIf)
            {
                AnsiConsole.Markup($"[cyan]- +++ RUNNING WHAT-IF +++[/]\n");
            }

            int unlockedAxeCount = resourcesToAxe.Where(r => r.IsLocked == false).Count();
            if ((unlockedAxeCount == 0 && settings.Force == false) || resourcesToAxe.Count == 0)
            {
                AnsiConsole.Markup($"[cyan]- No resources found to axe[/]\n\n");
            }
            else
            {
                foreach (var resource in resourcesToAxe)
                {
                    string locked = resource.IsLocked == true ? "LOCKED " : "";
                    string group = settings.ResourceGroups == true? " and [red]ALL[/] resources within it" : "";
                    AnsiConsole.Markup($"[green]- [red]WILL AXE {locked}[/]resource [white]{resource.OutputMessage}[/]{group}[/]\n");
                }
            }

            if (settings.WhatIf)
            {
                AnsiConsole.Markup($"[cyan]- +++ WHAT-IF COMPLETE +++[/]\n");
                return 0;
            }

            if ((unlockedAxeCount == 0 && settings.Force == false) || resourcesToAxe.Count == 0)
            {
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
                    AnsiConsole.Markup($"[green]- Resource axing abandoned[/]\n\n");
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
                    $"[red]- Probably a dependency issue. Pausing for {settings.RetryPause} seconds and will retry. Attempt {retryCount} of {settings.MaxRetries}[/]\n\n"
                );
                await Task.Delay(settings.RetryPause * 1000);
                resourcesToAxe = axeStatus.AxeList;
                retryCount++;
            }

            if (retryCount < (settings.MaxRetries + 1) && axeStatus.Status == true)
            {
                AnsiConsole.Markup($"[green]- All resources axed successfully[/]\n\n");
            }
            else if (retryCount < (settings.MaxRetries + 1) && axeStatus.Status == false)
            {
                AnsiConsole.Markup($"[green]- Axe failed on some locked resources[/]\n\n");
            }
            else
            {
                AnsiConsole.Markup(
                    $"[red]- Axe failed after {settings.MaxRetries} attempts. Try running the command again with --debug flag for more information[/]\n\n"
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
                            $"[green]- Removing {resourceLock.Scope} lock [white]{resourceLock.Name}[/] for [white]{resource.OutputMessage}[/][/]\n"
                        );
                        var lockResponse = await _client.DeleteAsync(
                            new Uri($"{resourceLock.Id}?api-version=2016-09-01", UriKind.Relative)
                        );
                        if (!lockResponse.IsSuccessStatusCode)
                        {
                            AnsiConsole.Markup($"[red]- Failed to remove lock for {resource.OutputMessage}[/] - SKIPPING\n");
                            skipAxe = true;
                        }
                    }
                }

                // If we can't remove the lock then skip the axe
                if (skipAxe)
                {
                    continue;
                }

                string group = settings.ResourceGroups == true ? " and [red]ALL[/] resources within it" : "";

                // Output the details of the delete request
                AnsiConsole.Markup($"[green]- [red]AXING[/] [white]{resource.OutputMessage}[/]{group}[/]\n");

                // Make the delete request
                var response = await _client.DeleteAsync(new Uri($"{resource.Id}?api-version={resource.ApiVersion}", UriKind.Relative));

                if (settings.Debug)
                {
                    AnsiConsole.Markup($"[green]- Response status code is {response.StatusCode}[/]");
                    AnsiConsole.Markup($"[green]- Response content: {await response.Content.ReadAsStringAsync()}[/]");
                }

                if (!response.IsSuccessStatusCode)
                {
                    string responseContent = await response.Content.ReadAsStringAsync();
                    if (responseContent.Contains("Please remove the lock and try again"))
                    {
                        AnsiConsole.Markup($"[red]- Axe failed because the resource is locked. Remove the lock and try again[/]\n");
                        axeStatus.Status = false;
                        continue;
                    }
                    else
                    {
                        AnsiConsole.Markup($"[red]- Axe failed: {response.StatusCode}[/]\n");
                        axeStatus.AxeList.Add(resource);
                        axeStatus.Status = false;
                    }
                }
                else
                {
                    AnsiConsole.Markup($"[green]- Resource axed successfully[/]\n");

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
                                    $"[green]- Adding {resourceLock.Scope} lock [white]{resourceLock.Name}[/] for [white]{resource.OutputMessage}[/][/]\n"
                                );

                                var createLockResponse = await _client.PutAsync(
                                    new Uri($"{resourceLock.Id}?api-version=2016-09-01", UriKind.Relative),
                                    new StringContent(JsonConvert.SerializeObject(resourceLock), Encoding.UTF8, "application/json")
                                );

                                if (!createLockResponse.IsSuccessStatusCode)
                                {
                                    AnsiConsole.Markup($"[red]- Failed to re-add lock for {resource.OutputMessage}[/]\n");
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
                        AnsiConsole.Markup($"[green]- Searching for resource groups where name contains [white]{name}[/][/]\n");
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
                        $"[green]- Searching for resource groups where tag [white]{tag[0]}[/] equals [white]{tag[1]}[/][/]\n"
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
                        AnsiConsole.Markup($"[green]- Searching for resources where name contains [white]{name}[/][/]\n");
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

                    AnsiConsole.Markup($"[green]- Searching for resources where tag [white]{tag[0]}[/] equals [white]{tag[1]}[/][/]\n");
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
                    AnsiConsole.Markup($"[green]- Restricting resource types to:[/]\n");
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
                    AnsiConsole.Markup($"[green]- Excluding [white]{resource.Name}[/][/]\n");
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
                    resource.OutputMessage =
                        $"[green]group[/] [white]{resource.Name}[/]";
                }

                string? apiVersion = await GetLatestApiVersion(settings, provider, resourceType);

                if (apiVersion == null)
                {
                    AnsiConsole.Markup($"[red]- Unable to get latest API version for {resource.OutputMessage}[/] so will exclude\n");
                }

                resource.ApiVersion = apiVersion;
            }

            // Remove any resources that we couldn't get an API version for
            resourcesFound = resourcesFound.Except(resourcesFound.Where(r => string.IsNullOrEmpty(r.ApiVersion)).ToList()).ToList();

            await CheckForLocks(settings, resourcesFound);

            // Return whatever is left
            return resourcesFound;
        }

        private async Task CheckForLocks(AxeSettings settings, List<Resource> resources)
        {
            AnsiConsole.Markup($"[green]- Checking found resources for locks[/]\n");

            List<ResourceLock> resourceLocks = new();

            if (settings.Force == true)
            {
                AnsiConsole.Markup($"[green]- Force option provided - resource locks will be removed for axing[/]\n");
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
                        foreach (var resourceLock in resourceLocks)
                        {
                            if (resourceLock.Id.ToLower() == $"{resource.Id}/providers/{resourceLock.Type}/{resourceLock.Name}".ToLower())
                            {
                                resourceLock.Scope = "resource";
                                resource.ResourceLocks.Add(resourceLock);
                                resource.IsLocked = true;
                            }
                            else if (
                                resourceLock.Id.ToLower()
                                == $"{resource.ResourceGroup}/providers/{resourceLock.Type}/{resourceLock.Name}".ToLower()
                            )
                            {
                                resourceLock.Scope = "resource group";
                                resource.ResourceLocks.Add(resourceLock);
                                resource.IsLocked = true;
                            }
                            else if (
                                resourceLock.Id.ToLower()
                                == $"/subscriptions/{settings.Subscription}/providers/{resourceLock.Type}/{resourceLock.Name}".ToLower()
                            )
                            {
                                resourceLock.Scope = "subscription";
                                resource.ResourceLocks.Add(resourceLock);
                                resource.IsLocked = true;
                            }
                        }
                        if (settings.Force == false && resource.IsLocked == true)
                        {
                            AnsiConsole.Markup(
                                $"[green]- Found resource {resource.OutputMessage} but it's locked and cannot be deleted[/]\n"
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
