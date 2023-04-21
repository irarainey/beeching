using Beeching.Commands.Interfaces;
using Beeching.Helpers;
using Beeching.Models;
using Newtonsoft.Json;
using Polly;
using Spectre.Console;
using System.Net.Http.Headers;

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

            // Get the list of resources to axe based on the name filter
            List<Resource> resourcesToAxe = await GetAxeResourceList(settings);

            // If no resources are found then drop out
            if (resourcesToAxe.Count == 0)
            {
                AnsiConsole.Markup($"[cyan]- No resources found to axe[/]\n\n");
                return 0;
            }

            // If we are in what-if mode then just output the details of the resources to axe
            if (settings.WhatIf)
            {
                AnsiConsole.Markup($"[cyan]- +++ RUNNING WHAT-IF +++[/]\n");
            }

            // List of URIs to axe with the formatted output message
            List<(Uri, string)> axeUriList = new();

            // Iterate through the list of resources to axe
            foreach (var resource in resourcesToAxe)
            {
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
                    outputMessage =
                        $"[white]'{resource.Type}' '{resource.Name}'[/] [green]in resource group[/] [white]'{resourceGroup}'[/]";
                }
                else
                {
                    provider = "Microsoft.Resources";
                    resourceType = "resourceGroups";
                    outputMessage = $"[white]'{resource.Type}' '{resource.Name}'[/]";
                }

                AnsiConsole.Markup($"[red]- WILL AXE[/] {outputMessage}\n");

                if (!settings.WhatIf)
                {
                    // Get the latest API version for the resource type
                    string? apiVersion = await GetLatestApiVersion(settings, provider, resourceType);

                    // If we can't get the latest API version then skip over this resource
                    if (apiVersion == null)
                    {
                        AnsiConsole.Markup($"[red]- Unable to get latest API version for {outputMessage}[/] so will exclude\n");
                        continue;
                    }

                    // Build the URI for the delete request
                    axeUriList.Add((new Uri($"{resource.Id}?api-version={apiVersion}", UriKind.Relative), outputMessage));
                }
            }

            // If we are in what-if mode then drop out here
            if (settings.WhatIf)
            {
                AnsiConsole.Markup($"[cyan]- +++ WHAT-IF COMPLETE +++[/]\n");
                return 0;
            }

            // If the axe list is empty then drop out
            if (axeUriList.Count == 0)
            {
                AnsiConsole.Markup($"[cyan]- No resources found to axe[/]\n\n");
                return 0;
            }

            // If we want to skip confirmation then be my guest
            if (!settings.SkipConfirmation)
            {
                var confirm = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("\nAre you sure you want to axe these resources? [red](This cannot be undone)[/]")
                        .AddChoices(new[] { "Yes", "No" })
                );

                if (confirm == "No")
                {
                    AnsiConsole.Markup($"[green]- Resource axing abandoned[/]\n\n");
                    return 0;
                }
            }

            int retryCount = 1;
            while (retryCount < (settings.MaxRetries + 1))
            {
                // Iterate through the list of resources to axe and make the delete requests
                List<(Uri, string)> failedAxeList = await SwingTheAxe(settings, axeUriList);

                if (failedAxeList.Count == 0)
                {
                    break;
                }

                AnsiConsole.Markup(
                    $"[red]- Probably a dependency issue. Pausing for {settings.RetryPause} seconds and will retry. Attempt {retryCount} of {settings.MaxRetries}[/]\n\n"
                );
                await Task.Delay(settings.RetryPause * 1000);
                axeUriList = failedAxeList;
                retryCount++;
            }

            if (retryCount < (settings.MaxRetries + 1))
            {
                AnsiConsole.Markup($"[green]- All resources axed successfully[/]\n\n");
            }
            else
            {
                AnsiConsole.Markup($"[red]- Axe failed after {settings.MaxRetries} attempts. Try running the command again with --debug flag for more information[/]\n\n");
            }

            return 0;
        }

        private async Task<List<(Uri, string)>> SwingTheAxe(AxeSettings settings, List<(Uri, string)> axeUriList)
        {
            List<(Uri, string)> failedAxeList = new();
            foreach (var resource in axeUriList)
            {
                // Output the details of the delete request
                AnsiConsole.Markup($"[green]- Axing [white]'{resource.Item2}'[/][/]\n");

                // Make the delete request
                var response = await _client.DeleteAsync(resource.Item1);

                if (settings.Debug)
                {
                    AnsiConsole.Markup($"[green]- Response status code is {response.StatusCode}[/]");
                    AnsiConsole.Markup($"[green]- Response content: {await response.Content.ReadAsStringAsync()}[/]");
                }

                if (!response.IsSuccessStatusCode)
                {
                    AnsiConsole.Markup($"[red]- Axe failed: {response.StatusCode}[/]\n");
                    failedAxeList.Add(resource);
                }
                else
                {
                    AnsiConsole.Markup($"[green]- Resource axed successfully[/]\n");
                }
            }

            return failedAxeList;
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
            HttpResponseMessage resourceResponse;
            HttpResponseMessage groupsResponse;

            if (useNameFilter)
            {
                AnsiConsole.Markup($"[green]- Searching for resources where name contains [white]'{settings.Name}'[/][/]\n");

                // Get the list of resource groups
                groupsResponse = await _client.GetAsync($"subscriptions/{settings.Subscription}/resourcegroups?api-version=2021-04-01");

                // Get the list of resources based on the name filter
                resourceResponse = await _client.GetAsync(
                    $"subscriptions/{settings.Subscription}/resources?$filter=substringof('{settings.Name}',name)&api-version=2021-04-01"
                );
            }
            else
            {
                // Split the tag into a key and value
                List<string> tag = settings.Tag.Split(':').ToList();

                AnsiConsole.Markup($"[green]- Searching for resources where tag [white]'{tag[0]}'[/] equals [white]'{tag[1]}'[/][/]\n");

                // Get the list of resource groups based on the tag filter
                groupsResponse = await _client.GetAsync(
                    $"subscriptions/{settings.Subscription}/resourcegroups?$filter=tagName eq '{tag[0]}' and tagValue eq '{tag[1]}'&api-version=2021-04-01"
                );

                // Get the list of resources based on the tag filter
                resourceResponse = await _client.GetAsync(
                    $"subscriptions/{settings.Subscription}/resources?$filter=tagName eq '{tag[0]}' and tagValue eq '{tag[1]}'&api-version=2021-04-01"
                );
            }

            // Read and iterate through the list of resources and groups
            string groupJson = await groupsResponse.Content.ReadAsStringAsync();
            string resourceJson = await resourceResponse.Content.ReadAsStringAsync();

            // Create a list of resources found to axed
            List<Resource> resourcesFound = new();

            // If we have a response for groups then add them to the list
            if (groupJson != null)
            {
                resourcesFound.AddRange(JsonConvert.DeserializeObject<Dictionary<string, List<Resource>>>(groupJson)!["value"]);

                // If we are using a name filter then filter the list to only groups the name filter matches
                if (useNameFilter)
                {
                    resourcesFound = resourcesFound.Where(x => x.Name.Contains(settings.Name)).ToList();
                }
            }

            // If we have a response for resources then add them to the list
            if (resourceJson != null)
            {
                resourcesFound.AddRange(JsonConvert.DeserializeObject<Dictionary<string, List<Resource>>>(resourceJson)!["value"]);
            }

            // Do we need to filter the resource types?
            if (!string.IsNullOrEmpty(settings.ResourceTypes))
            {
                List<string> allowedTypes = settings.ResourceTypes.Split(':').ToList();
                AnsiConsole.Markup($"[green]- Restricting resource types to [white]'{settings.ResourceTypes}'[/] only[/]\n");
                resourcesFound = resourcesFound.Where(r => allowedTypes.Contains(r.Type)).ToList();
            }

            // Do we need to filter exclusions?
            if (!string.IsNullOrEmpty(settings.Exclude))
            {
                List<string> exclusions = settings.Exclude.Split(':').ToList();
                List<Resource> filteredResources = resourcesFound.Where(r => !exclusions.Contains(r.Name)).ToList();
                foreach (var resource in resourcesFound.Except(filteredResources))
                {
                    AnsiConsole.Markup($"[green]- Excluding [white]'{resource.Name}'[/][/]\n");
                }
                resourcesFound = filteredResources;
            }

            // Return whatever is left
            return resourcesFound;
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
