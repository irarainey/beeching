using Spectre.Console.Cli;
using System.ComponentModel;

namespace Beeching.Commands
{
    public sealed class AxeSettings : CommandSettings
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

        [CommandOption("-f|--force")]
        [Description("Force the axe to delete the resources if locked.")]
        [DefaultValue(false)]
        public bool Force { get; set; }

        [CommandOption("-y|--yes")]
        [Description("Skip the confirmation prompt.")]
        [DefaultValue(false)]
        public bool SkipConfirmation { get; set; }

        [CommandOption("-q|--quiet")]
        [Description("Do not show any output.")]
        [DefaultValue(false)]
        public bool SupressOutput { get; set; }

        [CommandOption("-w|--what-if")]
        [Description("Show which resources would face the axe.")]
        [DefaultValue(false)]
        public bool WhatIf { get; set; }

        [CommandOption("-d|--debug")]
        [Description("Increase logging verbosity to show all debug logs.")]
        [DefaultValue(false)]
        public bool Debug { get; set; }

        [CommandOption("-v|--version")]
        [Description("Reports the application version.")]
        [DefaultValue(false)]
        public bool Version { get; set; }
    }
}
