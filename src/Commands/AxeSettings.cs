using Spectre.Console.Cli;
using System.ComponentModel;

namespace Beeching.Commands
{
    public sealed class AxeSettings : CommandSettings
    {
        [CommandOption("-d|--debug")]
        [Description("Increase logging verbosity to show all debug logs.")]
        [DefaultValue(false)]
        public bool Debug { get; set; }

        [CommandOption("-s|--subscription")]
        [Description("The subscription id to use.")]
        public Guid Subscription { get; set; }

        [CommandOption("-i|--id")]
        [Description("The resource id to axe.")]
        public string Id { get; set; } = "";

        [CommandOption("-t|--tag")]
        [Description("The tag value of the resources to axe.")]
        public string Tag { get; set; } = "";
    }
}
