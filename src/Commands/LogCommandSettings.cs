using Spectre.Console.Cli;
using System.ComponentModel;

namespace Beeching.Commands
{
    public class LogCommandSettings : CommandSettings
    {
        [CommandOption("--debug")]
        [Description("Increase logging verbosity to show all debug logs.")]
        [DefaultValue(false)]
        public bool Debug { get; set; }

    }
}