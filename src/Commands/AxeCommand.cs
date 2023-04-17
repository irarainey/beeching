using Beeching.Commands.Interfaces;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Diagnostics;
using System.Text.Json;

namespace Beeching.Commands
{
    internal sealed class AxeCommand : AsyncCommand<AxeSettings>
    {
        private readonly IAzureAxe _azureAxe;

        public AxeCommand(IAzureAxe azureAxe)
        {
            _azureAxe = azureAxe;
        }

        public override ValidationResult Validate(CommandContext context, AxeSettings settings)
        {
            if (string.IsNullOrEmpty(settings.Id))
            {
                return ValidationResult.Error("An Id must be specified.");
            }

            return ValidationResult.Success();
        }

        public override async Task<int> ExecuteAsync(CommandContext context, AxeSettings settings)
        {
            if (settings.Debug)
            {
                AnsiConsole.WriteLine($"Version: {typeof(AxeCommand).Assembly.GetName().Version}");
            }

            var subscriptionId = settings.Subscription;

            if (subscriptionId == Guid.Empty)
            {
                try
                {
                    if (settings.Debug)
                    {
                        AnsiConsole.WriteLine(
                            "No subscription ID specified. Trying to retrieve the default subscription ID from Azure CLI."
                        );
                    }

                    subscriptionId = Guid.Parse(GetDefaultAzureSubscriptionId());

                    if (settings.Debug)
                    {
                        AnsiConsole.WriteLine(
                            $"Default subscription ID retrieved from az cli: {subscriptionId}"
                        );
                    }

                    settings.Subscription = subscriptionId;
                }
                catch (Exception e)
                {
                    AnsiConsole.WriteException(
                        new ArgumentException(
                            "Missing subscription ID. Please specify a subscription ID or login to Azure CLI.",
                            e
                        )
                    );
                    return -1;
                }
            }

            await _azureAxe.AxeResource(settings);

            return 0;
        }

        private static string GetDefaultAzureSubscriptionId()
        {
            string azCliExecutable = DetermineAzCliPath();

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = azCliExecutable,
                    Arguments = "account show",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            string azOutput = process.StandardOutput.ReadToEnd();

            process.WaitForExit();

            int exitCode = process.ExitCode;
            process.Close();

            if (exitCode != 0)
            {
                string error = process.StandardError.ReadToEnd();
                throw new Exception($"Error executing 'az account show': {error}");
            }
            else
            {
                using var jsonDocument = JsonDocument.Parse(azOutput);
                JsonElement root = jsonDocument.RootElement;
                if (root.TryGetProperty("id", out JsonElement idElement))
                {
                    string subscriptionId = idElement.GetString();
                    return subscriptionId;
                }
                else
                {
                    throw new Exception("Unable to find the 'id' property in the JSON output.");
                }
            }
        }

        private static string DetermineAzCliPath()
        {
            string azExecutable = "az";

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName =
                        Environment.OSVersion.Platform == PlatformID.Win32NT ? "where" : "which",
                    Arguments = "az",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode == 0)
            {
                azExecutable = output.Split(Environment.NewLine)[0].Trim();
            }

            process.Close();

            return azExecutable
                + (
                    Environment.OSVersion.Platform == PlatformID.Win32NT
                    && azExecutable.EndsWith(".cmd") == false
                        ? ".cmd"
                        : ""
                );
        }
    }
}
