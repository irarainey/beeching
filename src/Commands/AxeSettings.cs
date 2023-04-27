using Spectre.Console.Cli;
using System.ComponentModel;

namespace Beeching.Commands
{
    internal class AxeSettings : CommandSettings
    {
        [CommandOption("-s|--subscription")]
        [Description("The subscription id to use.")]
        public Guid Subscription { get; set; }

        [CommandOption("-n|--name")]
        [Description("The name (or partial name) of the resources to axe.")]
        [DefaultValue("")]
        public string Name { get; set; }

        [CommandOption("-t|--tag")]
        [Description("The tag value of the resources to axe.")]
        [DefaultValue("")]
        public string Tag { get; set; }

        [CommandOption("-r|--resource-types")]
        [Description("Restict the types of the resources to axe.")]
        [DefaultValue("")]
        public string ResourceTypes { get; set; }

        [CommandOption("-e|--exclude")]
        [Description("The names of resources to exclude from the axe.")]
        [DefaultValue("")]
        public string Exclude { get; set; }

        [CommandOption("-g|--resource-groups")]
        [Description("Axe resource groups and contents rather than individual resource types.")]
        [DefaultValue(false)]
        public bool ResourceGroups { get; set; }

        [CommandOption("-f|--force")]
        [Description("Force the axe to delete the resources if locked.")]
        [DefaultValue(false)]
        public bool Force { get; set; }

        [CommandOption("-y|--yes")]
        [Description("Skip the confirmation prompt.")]
        [DefaultValue(false)]
        public bool SkipConfirmation { get; set; }

        [CommandOption("-w|--what-if")]
        [Description("Show which resources would face the axe.")]
        [DefaultValue(false)]
        public bool WhatIf { get; set; }

        [CommandOption("-m|--max-retry")]
        [Description("Sets the maximum amount to retry attempts when axe fails.")]
        [DefaultValue(6)]
        public int MaxRetries { get; set; }

        [CommandOption("-p|--retry-pause")]
        [Description("Sets the pause in seconds for the retry attempts.")]
        [DefaultValue(10)]
        public int RetryPause { get; set; }

        [CommandOption("-d|--debug")]
        [Description("Increase logging verbosity to show all debug logs.")]
        [DefaultValue(false)]
        public bool Debug { get; set; }

        [CommandOption("-v|--version")]
        [Description("Reports the application version.")]
        [DefaultValue(false)]
        public bool Version { get; set; }

        public string UserId { get; set; }

        public string SubscriptionRole { get; set; }

        public bool IsSubscriptionRolePrivileged { get; set; }
    }
}
