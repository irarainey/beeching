using Spectre.Console.Cli;
using System.ComponentModel;

namespace Beeching.Commands
{
    public sealed class AxeSettings : LogCommandSettings
    {
        [CommandOption("-s|--subscription")]
        [Description("The subscription id to use.")]
        public Guid Subscription { get; set; }

        [CommandOption("--name|-n")]
        [Description("The name of the resource group.")]
        public string Name { get; set; } = "";
    }
}
