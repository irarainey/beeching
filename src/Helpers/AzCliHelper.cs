using Beeching.Commands;
using Spectre.Console;
using System.Diagnostics;
using System.Text.Json;

namespace Beeching.Helpers
{
    internal static class AzCliHelper
    {
        private static string _azCliExecutable = Constants.AzCliExecutable;

        public static string DetermineAzCliPath()
        {
            using Process process = CreateProcess(
                Environment.OSVersion.Platform == PlatformID.Win32NT ? "where" : "which",
                _azCliExecutable
            );

            process.Start();
            string processOutput = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                _azCliExecutable = processOutput.Split(Environment.NewLine)[0].Trim();
            }

            return _azCliExecutable
                + (
                    Environment.OSVersion.Platform == PlatformID.Win32NT && _azCliExecutable.EndsWith(".cmd") == false
                        ? ".cmd"
                        : string.Empty
                );
        }

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
                            "[green]=> No subscription ID specified. Trying to retrieve the default subscription ID from Azure CLI[/]"
                        );
                    }

                    subscriptionId = Guid.Parse(GetCurrentAzureSubscription());

                    if (settings.Debug)
                    {
                        AnsiConsole.Markup($"[green]=> Default subscription ID retrieved from az cli: {subscriptionId}[/]");
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.WriteException(
                        new ArgumentException("Missing subscription ID. Please specify a subscription ID or login to Azure CLI.", ex)
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
            string processOuput = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                using var jsonOutput = JsonDocument.Parse(processOuput);
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
                string error = process.StandardError.ReadToEnd();
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

        public static string CallAzCliRest(string uri)
        {
            string azCliExecutable = DetermineAzCliPath();

            using Process process = CreateProcess(azCliExecutable, $"rest --uri {Constants.ArmBaseUrl}{uri}");
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return process.ExitCode == 0 ? output : error;
        }

        public static (string, string) GetSignedInUser()
        {
            (string, string) result = (string.Empty, string.Empty);
            string azCliExecutable = DetermineAzCliPath();
            using Process process = CreateProcess(azCliExecutable, "ad signed-in-user show");

            process.Start();
            string processOuput = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                using var jsonOutput = JsonDocument.Parse(processOuput);
                JsonElement root = jsonOutput.RootElement;
                if (root.TryGetProperty("id", out JsonElement idElement))
                {
                    result.Item1 = idElement.GetString() ?? string.Empty;
                }
                else
                {
                    result.Item1 = string.Empty;
                }

                if (root.TryGetProperty("displayName", out JsonElement displayNameElement))
                {
                    result.Item2 = displayNameElement.GetString() ?? string.Empty;
                }
                else
                {
                    result.Item2 = string.Empty;
                }

                return result;
            }
            else
            {
                string error = process.StandardError.ReadToEnd();
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
