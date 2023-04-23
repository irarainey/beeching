using Beeching.Commands.Interfaces;
using Beeching.Helpers;
using Spectre.Console;
using Spectre.Console.Cli;

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
            if (settings.Force)
            {
                return ValidationResult.Error("Force is not yet implemented.");
            }

            if (!string.IsNullOrEmpty(settings.Name) && !string.IsNullOrEmpty(settings.Tag))
            {
                return ValidationResult.Error("Only one of Name or Tag can be specified for resources to be axed.");
            }

            if (string.IsNullOrEmpty(settings.Name) && string.IsNullOrEmpty(settings.Tag))
            {
                return ValidationResult.Error("A Name or Tag must be specified for resources to be axed.");
            }

            if (!string.IsNullOrEmpty(settings.ResourceTypes) && settings.ResourceGroups)
            {
                return ValidationResult.Error("Resource groups cannot be specified with resource types.");
            }

            if (!string.IsNullOrEmpty(settings.ResourceTypes) && (!settings.ResourceTypes.Contains('/') || !settings.ResourceTypes.Contains ('.')))
            {
                return ValidationResult.Error("Resource type specified is not in a valid format.");
            }

            if (settings.MaxRetries < 1 || settings.MaxRetries > 100)
            {
                return ValidationResult.Error("Max retries must be set between 1 and 100.");
            }

            if (settings.RetryPause < 5 || settings.RetryPause > 60)
            {
                return ValidationResult.Error("Retry pause must be set between 5 and 60 seconds.");
            }

            return ValidationResult.Success();
        }

        public override async Task<int> ExecuteAsync(CommandContext context, AxeSettings settings)
        {
            settings.Subscription = GetSubscriptionId(settings);

            if (settings.Subscription == Guid.Empty)
            {
                return -1;
            }

            (string, string) userInformation = AzCliHelper.GetSignedInUser();
            AnsiConsole.Markup($"[green]- Running as user [white]{userInformation.Item2}[/] // [white]{userInformation.Item1}[/][/]\n");
            AnsiConsole.Markup($"[green]- Using subscription id [white]{settings.Subscription}[/][/]\n");

            return await _azureAxe.AxeResources(settings);
        }

        private static Guid GetSubscriptionId(AxeSettings settings)
        {
            Guid subscriptionId = settings.Subscription;

            if (subscriptionId == Guid.Empty)
            {
                try
                {
                    if (settings.Debug)
                    {
                        AnsiConsole.Markup(
                            "[green]- No subscription ID specified. Trying to retrieve the default subscription ID from Azure CLI[/]"
                        );
                    }

                    subscriptionId = Guid.Parse(AzCliHelper.GetDefaultAzureSubscriptionId());

                    if (settings.Debug)
                    {
                        AnsiConsole.Markup($"[green]- Default subscription ID retrieved from az cli: {subscriptionId}[/]");
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
    }
}
