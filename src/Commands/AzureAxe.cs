using Azure.Core;
using Azure.Identity;
using Beeching.Commands.Interfaces;
using Beeching.Models;
using Newtonsoft.Json;
using Polly;
using Spectre.Console;
using System;
using System.Net.Http.Headers;

namespace Beeching.Commands
{
    public class AzureAxe : IAzureAxe
    {
        private readonly HttpClient _client;

        public AzureAxe(IHttpClientFactory httpClientFactory)
        {
            _client = httpClientFactory.CreateClient("AzApi");
        }

        public async Task<bool> AxeResources(AxeSettings settings)
        {
            // Get the access token and add it to the request header for the http client
            await GetAccessToken(settings.Debug);

            // Get the list of resources to axe based on the name filter
            List<Resource> axeResources = await GetAxeResourceList(settings);

            // If there are no resources to axe then drop out
            if (axeResources.Count == 0)
            {
                if (!settings.SupressOutput)
                {
                    AnsiConsole.Markup($"[green]No resources found to axe[/]\n");
                }
                return true;
            }

            List<(Uri, string)> resourcesToAxe = new();

            if (settings.WhatIf)
            {
                AnsiConsole.Markup($"[green]** Running What-If **[/]\n");
            }

            // Iterate through the list of resources to axe
            foreach (var resource in axeResources)
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
                        $"{resource.Type} {resource.Name} in resource group {resourceGroup}";
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
                    AnsiConsole.Markup($"[green]Would axe {outputMessage}[/]\n");
                    continue;
                }

                // Get the latest API version for the resource type
                string? apiVersion = await GetLatestApiVersion(settings, provider, resourceType);

                // If we can't get the latest API version then skip over this resource
                if (apiVersion == null)
                {
                    if (!settings.SupressOutput)
                    {
                        AnsiConsole.Markup(
                            $"[red]Unable to get latest API version for {outputMessage}[/]\n"
                        );
                    }
                    continue;
                }

                // Build the URI for the delete request
                resourcesToAxe.Add(
                    (
                        new Uri($"{resource.Id}?api-version={apiVersion}", UriKind.Relative),
                        outputMessage
                    )
                );
                AnsiConsole.Markup($"[green]Found: {outputMessage}[/]\n");
            }

            if (!settings.SkipConfirmation)
            {
                var confirm = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title(
                            "Are you sure you want to axe these resources? [red](This cannot be undone)[/]"
                        )
                        .AddChoices(new[] { "Yes", "No" })
                );

                if (confirm == "No")
                {
                    AnsiConsole.Markup($"[red]Resource axing abandoned[/]\n");
                    return true;
                }
            }

            foreach (var resource in resourcesToAxe)
            {
                // Output the details of the delete request
                if (!settings.SupressOutput)
                {
                    AnsiConsole.Markup($"[green]Axing {resource.Item2}[/]\n");
                }

                // Make the delete request
                var response = await _client.DeleteAsync(resource.Item1);

                // If we are in debug mode then output the response
                if (settings.Debug)
                {
                    AnsiConsole.WriteLine($"Response status code is {response.StatusCode}");
                    if (!response.IsSuccessStatusCode)
                    {
                        AnsiConsole.WriteLine(
                            $"Response content: {await response.Content.ReadAsStringAsync()}"
                        );
                    }
                }
            }

            if (!settings.SupressOutput)
            {
                AnsiConsole.Markup($"[green]Resources axed[/]\n");
            }

            return true;
        }

        private async Task<string?> GetLatestApiVersion(
            AxeSettings settings,
            string provider,
            string type
        )
        {
            var apiVersion = await _client.GetAsync(
                $"subscriptions/{settings.Subscription}/providers/{provider}/resourceTypes?api-version=2021-04-01"
            );

            var api = await apiVersion.Content.ReadAsStringAsync();
            var allApiVersions = JsonConvert.DeserializeObject<ApiVersions>(api);

            if (allApiVersions == null)
            {
                return null;
            }

            var apiTypeVersion = allApiVersions.Value.Where(x => x.ResourceType == type).First();

            return apiTypeVersion.DefaultApiVersion ?? apiTypeVersion.ApiVersions.First();
        }

        private async Task<List<Resource>> GetAxeResourceList(AxeSettings settings)
        {
            var resources = new List<Resource>();

            if (!string.IsNullOrEmpty(settings.Name))
            {
                if (!settings.SupressOutput)
                {
                    AnsiConsole.Markup(
                        $"[green]Searching for resources where name contains '{settings.Name}'[/]\n"
                    );
                }

                // Get the list of resources based on the name filter
                var resourceResponse = await _client.GetAsync(
                    $"subscriptions/{settings.Subscription}/resources?$filter=substringof('{settings.Name}',name)&api-version=2021-04-01"
                );

                var resourceJson = await resourceResponse.Content.ReadAsStringAsync();
                var foundResources = JsonConvert.DeserializeObject<Resources>(resourceJson);

                if (foundResources != null)
                {
                    resources.AddRange(foundResources.Value);
                }

                // Get the list of resource groups
                var groupsResponse = await _client.GetAsync(
                    $"subscriptions/{settings.Subscription}/resourcegroups?api-version=2021-04-01"
                );

                var groupJson = await groupsResponse.Content.ReadAsStringAsync();
                var foundGroups = JsonConvert.DeserializeObject<Resources>(groupJson);

                if (foundGroups != null)
                {
                    var groups = foundGroups.Value
                        .Where(x => x.Name.Contains(settings.Name))
                        .ToList();
                    if (groups != null)
                    {
                        resources.AddRange(groups);
                    }
                }
            }
            else
            {
                var tag = settings.Tag.Split('|');

                if (!settings.SupressOutput)
                {
                    AnsiConsole.Markup(
                        $"[green]Searching for resources where tag '{tag[0]}' equals '{tag[1]}'[/]\n"
                    );
                }

                // Get the list of resources based on the name filter
                var resourceResponse = await _client.GetAsync(
                    $"subscriptions/{settings.Subscription}/resources?$filter=tagName eq '{tag[0]}' and tagValue eq '{tag[1]}'&api-version=2021-04-01"
                );

                var resourceJson = await resourceResponse.Content.ReadAsStringAsync();
                var foundResources = JsonConvert.DeserializeObject<Resources>(resourceJson);

                if (foundResources != null)
                {
                    resources.AddRange(foundResources.Value);
                }

                // Get the list of resource groups
                var groupsResponse = await _client.GetAsync(
                    $"subscriptions/{settings.Subscription}/resourcegroups?$filter=tagName eq '{tag[0]}' and tagValue eq '{tag[1]}'&api-version=2021-04-01"
                );

                var groupJson = await groupsResponse.Content.ReadAsStringAsync();
                var foundGroups = JsonConvert.DeserializeObject<Resources>(groupJson);

                if (foundGroups != null)
                {
                    var groups = foundGroups.Value
                        .Where(x => x.Name.Contains(settings.Name))
                        .ToList();
                    if (groups != null)
                    {
                        resources.AddRange(groups);
                    }
                }
            }

            return resources;
        }

        private async Task GetAccessToken(bool debug)
        {
            var tokenCredential = new ChainedTokenCredential(
                new AzureCliCredential(),
                new DefaultAzureCredential()
            );

            if (debug)
            {
                AnsiConsole.WriteLine(
                    $"Using token credential: {tokenCredential.GetType().Name} to fetch a token."
                );
            }

            var token = await tokenCredential.GetTokenAsync(
                new TokenRequestContext(new[] { $"https://management.azure.com/.default" })
            );

            if (debug)
            {
                AnsiConsole.WriteLine($"Token retrieved and expires at: {token.ExpiresOn}");
            }

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                token.Token
            );
        }

        public static IAsyncPolicy<HttpResponseMessage> GetRetryAfterPolicy()
        {
            return Policy
                .HandleResult<HttpResponseMessage>(
                    msg => msg.Headers.TryGetValues("RetryAfter", out var _)
                )
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
