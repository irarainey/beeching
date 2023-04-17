using Azure.Core;
using Azure.Identity;
using Polly;
using Spectre.Console;
using System.Net.Http.Headers;

namespace Beeching.Commands
{
    public class AzureAxe : IAzureAxe
    {
        private readonly HttpClient _client;
        private bool _tokenRetrieved;

        public AzureAxe(IHttpClientFactory httpClientFactory)
        {
            _client = httpClientFactory.CreateClient("AzApi");
        }

        public static IAsyncPolicy<HttpResponseMessage> GetRetryAfterPolicy()
        {
            return Policy.HandleResult<HttpResponseMessage>
                    (msg => msg.Headers.TryGetValues("RetryAfter", out var _))
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: (_, response, _) => response.Result.Headers.TryGetValues("RetryAfter", out var seconds) ? TimeSpan.FromSeconds(int.Parse(seconds.First())) : TimeSpan.FromSeconds(5),
                    onRetryAsync: (msg, time, retries, context) => Task.CompletedTask
                );
        }

        private async Task RetrieveToken(bool includeDebugOutput)
        {
            if (_tokenRetrieved)
                return;

            // Get the token by using the DefaultAzureCredential
            var tokenCredential = new ChainedTokenCredential(
                new AzureCliCredential(),
                new DefaultAzureCredential());

            if (includeDebugOutput)
                AnsiConsole.WriteLine($"Using token credential: {tokenCredential.GetType().Name} to fetch a token.");

            var token = await tokenCredential.GetTokenAsync(new TokenRequestContext(new[]
                { $"https://management.azure.com/.default" }));

            if (includeDebugOutput)
                AnsiConsole.WriteLine($"Token retrieved and expires at: {token.ExpiresOn}");

            // Set as the bearer token for the HTTP client
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            _tokenRetrieved = true;
        }

        private async Task<HttpResponseMessage> ExecuteCallToAzureApi(bool includeDebugOutput, object payload, Uri uri)
        {
            await RetrieveToken(includeDebugOutput);

            var response = await _client.DeleteAsync(uri);

            if (includeDebugOutput)
            {
                AnsiConsole.WriteLine($"Response status code is {response.StatusCode}");
                if (!response.IsSuccessStatusCode)
                {
                    AnsiConsole.WriteLine($"Response content: {await response.Content.ReadAsStringAsync()}");
                }
            }

            response.EnsureSuccessStatusCode();
            return response;
        }

        public async Task AxeResourceGroup(bool includeDebugOutput, Guid subscriptionId, string name)
        {
            var uri = new Uri(
                $"/subscriptions/{subscriptionId}/resourcegroups/{name}?api-version=2021-04-01",
                UriKind.Relative);

            var payload = "";
            var response = await ExecuteCallToAzureApi(includeDebugOutput, payload, uri);

            var content = await response.Content.ReadAsStringAsync();

            return;
        }
    }
}