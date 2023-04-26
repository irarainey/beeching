using Beeching;
using Beeching.Commands;
using Beeching.Commands.Interfaces;
using Beeching.Helpers;
using Beeching.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

var registrations = new ServiceCollection();

registrations
    .AddHttpClient(
        "ArmApi",
        client =>
        {
            client.BaseAddress = new Uri(Constants.ArmBaseUrl);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        }
    )
    .AddPolicyHandler(Axe.GetRetryAfterPolicy());

registrations.AddTransient<IAxe, Axe>();

var registrar = new TypeRegistrar(registrations);

var app = new CommandApp<AxeCommand>(registrar);

app.Configure(config =>
{
    config.SetApplicationName(Constants.Beeching);
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

AnsiConsole.Markup($"[green]{Constants.Header}[/]\n");
AnsiConsole.Markup($"[green]=> Version: {VersionHelper.GetVersion()}[/]\n");

string? latestVersion = await VersionHelper.GetLatestVersionAsync ();

if (latestVersion != null)
{
    if (VersionHelper.IsUpdateAvailable (installedVersion, latestVersion))
    {
        AnsiConsole.Markup ($"[cyan]=> An update is available {latestVersion}. Update using: dotnet tool update -g {Constants.Beeching}[/]\n");
    }
}

return await app.RunAsync(args);
