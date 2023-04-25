using Azure.Core;
using Azure.Identity;
using Spectre.Console;

namespace Beeching.Helpers
{
    internal static class AuthHelper
    {
        internal static async Task<string> GetAccessToken(bool debug)
        {
            var tokenCredential = new ChainedTokenCredential(new AzureCliCredential(), new DefaultAzureCredential());

            if (debug)
            {
                AnsiConsole.WriteLine($"=> Using token credential: {tokenCredential.GetType().Name} to fetch a token.");
            }

            var token = await tokenCredential.GetTokenAsync(new TokenRequestContext(new[] { $"https://management.azure.com/.default" }));

            if (debug)
            {
                AnsiConsole.WriteLine($"=> Token retrieved and expires at: {token.ExpiresOn}");
            }

            return token.Token;
        }
    }
}
