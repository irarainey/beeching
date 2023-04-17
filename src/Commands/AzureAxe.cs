using Azure.Core;
using Azure.Identity;
using Beeching.Commands.Interfaces;
using Beeching.Models;
using Newtonsoft.Json;
using Polly;
using Spectre.Console;
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
            Resources? axeResources = await GetAxeResourceList(settings);

            // If there are no resources to axe then drop out
            if (axeResources == null || axeResources.Value.Count == 0)
            {
                AnsiConsole.WriteLine($"No resources found to axe");
                return true;
            }

            // Iterate through the list of resources to axe
            foreach (var resource in axeResources.Value)
            {
                // Split the resource ID into sections so we can get various parts of it
                string[] sections = resource.Id.Split('/');
                string resourceGroup = sections[4];
                string provider = sections[6];
                string resourceType = sections[7];

                // If we are in what-if mode then just output the details of the resource to axe
                if (settings.WhatIf)
                {
                    AnsiConsole.WriteLine($"Would axe {resource.Type} {resource.Name} in resource group {resourceGroup}");
                    continue;
                }

                // Get the latest API version for the resource type
                string? apiVersion = await GetLatestApiVersion(settings, provider, resourceType);

                // If we can't get the latest API version then skip over this resource
                if (apiVersion == null)
                {
                    AnsiConsole.WriteLine($"Unable to get latest API version for {resource.Type} {resource.Name} in resource group {resourceGroup}");
                    continue;
                }

                // Build the URI for the delete request
                var uri = new Uri($"{resource.Id}?api-version={apiVersion}", UriKind.Relative);

                // Output the details of the delete request
                AnsiConsole.WriteLine($"Axing {resource.Type} {resource.Name} in resource group {resourceGroup}");

                // Make the delete request
                var response = await _client.DeleteAsync(uri);

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

                //response.EnsureSuccessStatusCode();
            }

            return true;
        }

        private async Task<string?> GetLatestApiVersion(AxeSettings settings, string provider, string type)
        {
            var apiVersion = await _client.GetAsync(
                                   $"subscriptions/{settings.Subscription}/providers/{provider}/resourceTypes?api-version=2021-04-01");

            var api = await apiVersion.Content.ReadAsStringAsync();
            var allApiVersions = JsonConvert.DeserializeObject<ApiVersions>(api);

            if (allApiVersions == null)
            {
                return null;
            }

            var apiTypeVersion = allApiVersions.Value.Where(x => x.ResourceType == type).First();

            return apiTypeVersion.DefaultApiVersion ?? apiTypeVersion.ApiVersions.First();
        }

        private async Task<Resources?> GetAxeResourceList(AxeSettings settings)
        {
            var response = await _client.GetAsync(
                               $"subscriptions/{settings.Subscription}/resources?$filter=substringof('{settings.Name}',name)&api-version=2021-04-01"
                                          );

            var json = await response.Content.ReadAsStringAsync();

            return JsonConvert.DeserializeObject<Resources>(json);
        }

        private async Task GetAccessToken(bool includeDebugOutput)
        {
            var tokenCredential = new ChainedTokenCredential(
                new AzureCliCredential(),
                new DefaultAzureCredential()
            );

            if (includeDebugOutput)
            {
                AnsiConsole.WriteLine(
                    $"Using token credential: {tokenCredential.GetType().Name} to fetch a token."
                );
            }

            var token = await tokenCredential.GetTokenAsync(
                new TokenRequestContext(new[] { $"https://management.azure.com/.default" })
            );

            if (includeDebugOutput)
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
