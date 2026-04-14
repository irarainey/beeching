using Beeching.Commands;
using Spectre.Console;
using System.Diagnostics;
using System.Text.Json;

namespace Beeching.Helpers
{
    internal static class AzCliHelper
    {
        private static readonly Lazy<string> _resolvedAzCliPath = new(ResolveAzCliPath);

        private static string ResolveAzCliPath()
        {
            using Process process = CreateProcess(
                Environment.OSVersion.Platform == PlatformID.Win32NT ? "where" : "which",
                Constants.AzCliExecutable
            );

            process.Start();
            string processOutput = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            string resolved = process.ExitCode == 0
                ? processOutput.Split(Environment.NewLine)[0].Trim()
                : Constants.AzCliExecutable;

            return resolved
                + (
                    Environment.OSVersion.Platform == PlatformID.Win32NT && !resolved.EndsWith(".cmd")
                        ? ".cmd"
                        : string.Empty
                );
        }

        public static string DetermineAzCliPath() => _resolvedAzCliPath.Value;

        public static Guid GetSubscriptionId(AxeSettings settings)
        {
            Guid subscriptionId = settings.Subscription;

            if (subscriptionId == Guid.Empty)
            {
                try
                {
                    if (settings.Debug)
                    {
                        AnsiConsole.Markup(
                            "[green]=> No subscription ID specified. Trying to retrieve the default subscription ID from Azure CLI[/]\n"
                        );
                    }

                    subscriptionId = Guid.Parse(GetCurrentAzureSubscription());

                    if (settings.Debug)
                    {
                        AnsiConsole.Markup($"[green]=> Default subscription ID retrieved from az cli: {subscriptionId}[/]\n");
                    }
                }
                catch
                {
                    AnsiConsole.Markup(
                        "[red]=> Missing subscription ID. Please specify a subscription ID or login to Azure CLI.[/]\n"
                    );
                }
            }

            return subscriptionId;
        }

        public static string GetCurrentAzureSubscription()
        {
            string azCliExecutable = DetermineAzCliPath();
            using Process process = CreateProcess(azCliExecutable, "account show");

            process.Start();
            var stderrTask = process.StandardError.ReadToEndAsync();
            string processOutput = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                using var jsonOutput = JsonDocument.Parse(processOutput);
                JsonElement root = jsonOutput.RootElement;
                if (root.TryGetProperty("id", out JsonElement idElement))
                {
                    return idElement.GetString() ?? string.Empty;
                }
                else
                {
                    throw new Exception("Unable to find the 'id' property in the JSON output.");
                }
            }
            else
            {
                string error = stderrTask.GetAwaiter().GetResult();
                throw new Exception($"Error executing '{azCliExecutable} account show': {error}");
            }
        }

        public static string GetSubscriptionName(string subscriptionId)
        {
            string output = CallAzCliRest($"/subscriptions/{subscriptionId}?api-version=2020-01-01");

            using var jsonOutput = JsonDocument.Parse(output);
            JsonElement root = jsonOutput.RootElement;
            if (root.TryGetProperty("displayName", out JsonElement displayNameElement))
            {
                return displayNameElement.GetString() ?? string.Empty;
            }
            else
            {
                return "[Error Determining Name]";
            }
        }

        private static string CallAzCliRest(string uri)
        {
            string azCliExecutable = DetermineAzCliPath();

            using Process process = CreateProcess(azCliExecutable, $"rest --uri {Constants.ArmBaseUrl}{uri}");
            process.Start();
            var stderrTask = process.StandardError.ReadToEndAsync();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return process.ExitCode == 0 ? output : stderrTask.GetAwaiter().GetResult();
        }

        public static (string UserId, string DisplayName) GetSignedInUser()
        {
            string azCliExecutable = DetermineAzCliPath();
            using Process process = CreateProcess(azCliExecutable, "ad signed-in-user show");

            process.Start();
            var stderrTask = process.StandardError.ReadToEndAsync();
            string processOutput = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                using var jsonOutput = JsonDocument.Parse(processOutput);
                JsonElement root = jsonOutput.RootElement;

                string userId = root.TryGetProperty("id", out JsonElement idElement)
                    ? idElement.GetString() ?? string.Empty
                    : string.Empty;

                string displayName = root.TryGetProperty("displayName", out JsonElement displayNameElement)
                    ? displayNameElement.GetString() ?? string.Empty
                    : string.Empty;

                return (userId, displayName);
            }
            else
            {
                string error = stderrTask.GetAwaiter().GetResult();
                throw new Exception($"Error executing '{Constants.AzCliExecutable} ad signed-in-user show': {error}");
            }
        }

        private static Process CreateProcess(string filename, string arguments)
        {
            return new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = filename,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
        }
    }
}
