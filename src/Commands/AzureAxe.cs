using Azure.Core;
using Azure.Identity;
using Beeching.Commands.Interfaces;
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

        private async Task RetrieveToken(bool includeDebugOutput)
        {
            if (_tokenRetrieved)
            {
                return;
            }

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

            _tokenRetrieved = true;
        }

        private async Task<HttpResponseMessage> ExecuteCallToAzureApi(AxeSettings settings, Uri uri)
        {
            await RetrieveToken(settings.Debug);

            var response = await _client.DeleteAsync(uri);

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

            response.EnsureSuccessStatusCode();
            return response;
        }

        public async Task<HttpResponseMessage> AxeResource(AxeSettings settings)
        {
            string resourceId = settings.Id;

            var uri = new Uri($"{resourceId}?api-version=2021-04-01", UriKind.Relative);

            var response = await ExecuteCallToAzureApi(settings, uri);

            return response;
        }
    }
}
