using Beeching.Commands;
using Beeching.Commands.Interfaces;
using Beeching.Helpers;
using Beeching.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

string header =
    "\n _                    _     _             \r\n| |                  | |   (_)            \r\n| |__   ___  ___  ___| |__  _ _ __   __ _ \r\n| '_ \\ / _ \\/ _ \\/ __| '_ \\| | '_ \\ / _` |\r\n| |_) |  __/  __/ (__| | | | | | | | (_| |\r\n|_.__/ \\___|\\___|\\___|_| |_|_|_| |_|\\__, |\r\n                                     __/ |\r\n                                    |___/\n ";

var registrations = new ServiceCollection();

registrations
    .AddHttpClient(
        "AzApi",
        client =>
        {
            client.BaseAddress = new Uri("https://management.azure.com/");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        }
    )
    .AddPolicyHandler(AzureAxe.GetRetryAfterPolicy());

registrations.AddTransient<IAzureAxe, AzureAxe>();

var registrar = new TypeRegistrar(registrations);

var app = new CommandApp<AxeCommand>(registrar);

app.Configure(config =>
{
    config.SetApplicationName("beeching");
    config.AddCommand<AxeCommand>("axe").WithDescription("The mighty axe that culls the resources.");

#if DEBUG
    config.PropagateExceptions();
#endif
});

string installedVersion = VersionHelper.GetVersion();

if (args.Contains("--version") || args.Contains("-v"))
{
    AnsiConsole.WriteLine(installedVersion);
    return 0;
}

AnsiConsole.Markup($"[green]{header}[/]\n");
AnsiConsole.Markup($"[green]- Version: {VersionHelper.GetVersion()}[/]\n");

string? latestVersion = await VersionHelper.GetLatestVersionAsync ();

if (latestVersion != null)
{
    if (VersionHelper.IsUpdateAvailable (installedVersion, latestVersion))
    {
        AnsiConsole.Markup ($"[cyan]- An update is available {latestVersion}. Update using: dotnet tool update -g beeching[/]\n");
    }
}

return await app.RunAsync(args);
