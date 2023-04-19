using Beeching.Commands.Interfaces;
using Beeching.Helpers;
using Beeching.Models;
using Newtonsoft.Json;
using Polly;
using Spectre.Console;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Beeching.Commands
{
    internal sealed class AzureAxe : IAzureAxe
    {
        private readonly HttpClient _client;

        public AzureAxe(IHttpClientFactory httpClientFactory)
        {
            _client = httpClientFactory.CreateClient("AzApi");
        }

        public async Task<bool> AxeResources(AxeSettings settings)
        {
            // Get the access token and add it to the request header for the http client
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                await AuthHelper.GetAccessToken(settings.Debug)
            );

            if (!settings.SupressOutput)
            {
                AnsiConsole.Markup($"[green]- Using subscription id {settings.Subscription}[/]\n");
            }

            // Get the list of resources to axe based on the name filter
            List<Resource> axeResources = await GetAxeResourceList(settings);

            // If there are no resources to axe then drop out
            if (axeResources.Count == 0)
            {
                if (!settings.SupressOutput)
                {
                    AnsiConsole.Markup($"[cyan]- No resources found to axe[/]\n\n");
                }
                return true;
            }

            List<(Uri, string)> resourcesToAxe = new();

            if (settings.WhatIf)
            {
                AnsiConsole.Markup($"[green]- ** Running What-If **[/]\n");
            }

            // Iterate through the list of resources to axe
            foreach (var resource in axeResources)
            {
                List<string> exclusions = settings.Exclude.Split(':').ToList();
                if (exclusions.Contains(resource.Name))
                {
                    AnsiConsole.Markup($"[green]- Excluding {resource.Name}[/]\n");
                    continue;
                }

                // Split the resource ID into sections so we can get various parts of it
                string[] sections = resource.Id.Split('/');
                string resourceGroup = sections[4];
                string provider;
                string resourceType;
                string outputMessage;

                if (resource.Type != "Microsoft.Resources/resourceGroups")
                {
                    provider = sections[6];
                    resourceType = sections[7];
                    outputMessage = $"{resource.Type} {resource.Name} in resource group {resourceGroup}";
                }
                else
                {
                    provider = "Microsoft.Resources";
                    resourceType = "resourceGroups";
                    outputMessage = $"{resource.Type} {resource.Name}";
                }

                // If we are in what-if mode then just output the details of the resource to axe
                if (settings.WhatIf)
                {
                    AnsiConsole.Markup($"[green]- Would axe {outputMessage}[/]\n");
                    continue;
                }

                // Get the latest API version for the resource type
                string? apiVersion = await GetLatestApiVersion(settings, provider, resourceType);

                // If we can't get the latest API version then skip over this resource
                if (apiVersion == null)
                {
                    if (!settings.SupressOutput)
                    {
                        AnsiConsole.Markup($"[red]- Unable to get latest API version for {outputMessage}[/]\n");
                    }
                    continue;
                }

                // Build the URI for the delete request
                resourcesToAxe.Add((new Uri($"{resource.Id}?api-version={apiVersion}", UriKind.Relative), outputMessage));
                AnsiConsole.Markup($"[green]- Found: {outputMessage}[/]\n");
            }

            // If we are in what-if mode then drop out
            if (settings.WhatIf)
            {
                return true;
            }

            if (resourcesToAxe.Count == 0)
            {
                if (!settings.SupressOutput)
                {
                    AnsiConsole.Markup($"[cyan]- No resources found to axe[/]\n\n");
                }
                return true;
            }

            if (!settings.SkipConfirmation)
            {
                var confirm = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("\nAre you sure you want to axe these resources? [red](This cannot be undone)[/]")
                        .AddChoices(new[] { "Yes", "No" })
                );

                if (confirm == "No")
                {
                    AnsiConsole.Markup($"[red]- Resource axing abandoned[/]\n\n");
                    return true;
                }
            }

            bool allSuccessful = true;
            foreach (var resource in resourcesToAxe)
            {
                // Output the details of the delete request
                if (!settings.SupressOutput)
                {
                    AnsiConsole.Markup($"[green]- Axing {resource.Item2}[/]\n");
                }

                // Make the delete request
                var response = await _client.DeleteAsync(resource.Item1);

                if (settings.Debug)
                {
                    AnsiConsole.WriteLine($"- Response status code is {response.StatusCode}");
                    AnsiConsole.WriteLine($"Response content: {await response.Content.ReadAsStringAsync()}");
                }

                if (!settings.SupressOutput)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        AnsiConsole.Markup($"[red]- Axe failed: {response.StatusCode}[/]\n");
                        allSuccessful = false;
                    }
                    else
                    {
                        AnsiConsole.Markup($"[green]- Resource axed successfully[/]\n");
                    }
                }
            }

            if (!settings.SupressOutput)
            {
                if (allSuccessful)
                {
                    AnsiConsole.Markup($"[green]- All resources axed successfully[/]\n\n");
                }
                else
                {
                    AnsiConsole.Markup(
                        $"[red]- Some resources refused to be axed. This could be a dependency issue. Try running the same command again[/]\n\n"
                    );
                }
            }

            return true;
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
            List<Resource> resources = new();
            List<string> allowedTypes = settings.ResourceTypes.Split(':').ToList();

            if (!string.IsNullOrEmpty(settings.Name))
            {
                if (!settings.SupressOutput)
                {
                    AnsiConsole.Markup($"[green]- Searching for resources where name contains '{settings.Name}'[/]\n");
                }

                // Get the list of resources based on the name filter
                HttpResponseMessage resourceResponse = await _client.GetAsync(
                    $"subscriptions/{settings.Subscription}/resources?$filter=substringof('{settings.Name}',name)&api-version=2021-04-01"
                );

                string resourceJson = await resourceResponse.Content.ReadAsStringAsync();
                List<Resource> foundResources = new();
                if (resourceJson != null)
                {
                    foundResources = JsonConvert.DeserializeObject<Dictionary<string, List<Resource>>>(resourceJson)!["value"];
                }

                if (foundResources != null)
                {
                    foreach (var resource in foundResources)
                    {
                        if (!string.IsNullOrEmpty(settings.ResourceTypes))
                        {
                            if (allowedTypes.Contains(resource.Type))
                            {
                                resources.Add(resource);
                            }
                        }
                        else
                        {
                            resources.Add(resource);
                        }
                    }
                }

                // Get the list of resource groups
                HttpResponseMessage groupsResponse = await _client.GetAsync(
                    $"subscriptions/{settings.Subscription}/resourcegroups?api-version=2021-04-01"
                );

                string groupJson = await groupsResponse.Content.ReadAsStringAsync();
                List<Resource> foundGroups = new();
                if (groupJson != null)
                {
                    foundGroups = JsonConvert.DeserializeObject<Dictionary<string, List<Resource>>>(groupJson)!["value"];
                }

                if (foundGroups != null)
                {
                    var groups = foundGroups.Where(x => x.Name.Contains(settings.Name)).ToList();
                    if (groups != null)
                    {
                        foreach (var resource in groups)
                        {
                            if (!string.IsNullOrEmpty(settings.ResourceTypes))
                            {
                                if (allowedTypes.Contains(resource.Type))
                                {
                                    resources.Add(resource);
                                }
                            }
                            else
                            {
                                resources.Add(resource);
                            }
                        }
                    }
                }
            }
            else
            {
                List<string> tag = settings.Tag.Split(':').ToList();

                if (!settings.SupressOutput)
                {
                    AnsiConsole.Markup($"[green]- Searching for resources where tag '{tag[0]}' equals '{tag[1]}'[/]\n");
                }

                // Get the list of resources based on the name filter
                HttpResponseMessage resourceResponse = await _client.GetAsync(
                    $"subscriptions/{settings.Subscription}/resources?$filter=tagName eq '{tag[0]}' and tagValue eq '{tag[1]}'&api-version=2021-04-01"
                );

                string resourceJson = await resourceResponse.Content.ReadAsStringAsync();
                List<Resource> foundResources = new();
                if (resourceJson != null)
                {
                    foundResources = JsonConvert.DeserializeObject<Dictionary<string, List<Resource>>>(resourceJson)!["value"];
                }

                if (foundResources != null)
                {
                    foreach (var resource in foundResources)
                    {
                        if (!string.IsNullOrEmpty(settings.ResourceTypes))
                        {
                            if (allowedTypes.Contains(resource.Type))
                            {
                                resources.Add(resource);
                            }
                        }
                        else
                        {
                            resources.Add(resource);
                        }
                    }
                }

                // Get the list of resource groups
                HttpResponseMessage groupsResponse = await _client.GetAsync(
                    $"subscriptions/{settings.Subscription}/resourcegroups?$filter=tagName eq '{tag[0]}' and tagValue eq '{tag[1]}'&api-version=2021-04-01"
                );

                string groupJson = await groupsResponse.Content.ReadAsStringAsync();
                List<Resource> foundGroups = new();
                if (groupJson != null)
                {
                    foundGroups = JsonConvert.DeserializeObject<Dictionary<string, List<Resource>>>(groupJson)!["value"];
                }

                if (foundGroups != null)
                {
                    var groups = foundGroups.Where(x => x.Name.Contains(settings.Name)).ToList();
                    if (groups != null)
                    {
                        foreach (var resource in groups)
                        {
                            if (!string.IsNullOrEmpty(settings.ResourceTypes))
                            {
                                if (allowedTypes.Contains(resource.Type))
                                {
                                    resources.Add(resource);
                                }
                            }
                            else
                            {
                                resources.Add(resource);
                            }
                        }
                    }
                }
            }

            return resources;
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
