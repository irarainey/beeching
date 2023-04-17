using Spectre.Console.Cli;
using System.ComponentModel;

namespace Beeching.Commands
{
    public sealed class AxeSettings : CommandSettings
    {
        [CommandOption("-w|--what-if")]
        [Description("Show which resources would face the axe.")]
        public bool WhatIf { get; set; } = false;

        [CommandOption("-d|--debug")]
        [Description("Increase logging verbosity to show all debug logs.")]
        [DefaultValue(false)]
        public bool Debug { get; set; } = false;

        [CommandOption("-s|--subscription")]
        [Description("The subscription id to use.")]
        public Guid Subscription { get; set; }

        [CommandOption("-n|--name")]
        [Description("The name of the resources to axe.")]
        public string? Name { get; set; }

        [CommandOption("-i|--id")]
        [Description("An individual resource id to axe.")]
        public string? Id { get; set; }

        [CommandOption("-t|--tag")]
        [Description("The tag value of the resources to axe.")]
        public string? Tag { get; set; }

        [CommandOption("-e|--exclude")]
        [Description("The name of the resources to exclude from the axe.")]
        public string? Exclude { get; set; }
    }
}
