using Beeching;
using Beeching.Commands;
using Beeching.Commands.Interfaces;
using Beeching.Helpers;
using Beeching.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
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
    .AddResilienceHandler("retry-after", builder =>
    {
        builder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            ShouldHandle = static args => ValueTask.FromResult(
                args.Outcome.Result?.Headers.TryGetValues("RetryAfter", out _) == true),
            DelayGenerator = static args =>
            {
                if (args.Outcome.Result?.Headers.TryGetValues("RetryAfter", out var values) == true
                    && int.TryParse(values.First(), out var seconds))
                {
                    return new ValueTask<TimeSpan?>(TimeSpan.FromSeconds(seconds));
                }
                return new ValueTask<TimeSpan?>(TimeSpan.FromSeconds(5));
            }
        });
    });

registrations.AddTransient<IAxe, Axe>();

var registrar = new TypeRegistrar(registrations);

var app = new CommandApp<AxeCommand>(registrar);

app.Configure(config =>
{
    config.SetApplicationName(Constants.Beeching);
    config.AddCommand<AxeCommand>("axe").WithDescription("The mighty axe that culls the resources.");
});

string installedVersion = VersionHelper.GetVersion();

if (args.Contains("--version") == true || args.Contains("-v") == true)
{
    AnsiConsole.WriteLine(installedVersion);
    return 0;
}

AnsiConsole.Markup($"[green]{Constants.Header}[/]\n");
AnsiConsole.Markup($"[green]=> Version: {VersionHelper.GetVersion()}[/]\n");

if (args.Contains("--ignore-update") == false && args.Contains("-i") == false)
{
    string? latestVersion = await VersionHelper.GetLatestVersionAsync();

    if (latestVersion != null)
    {
        if (VersionHelper.IsUpdateAvailable(installedVersion, latestVersion))
        {
            AnsiConsole.Markup(
                $"[cyan]=> An update is available {latestVersion}. Update using: dotnet tool update -g {Constants.Beeching}[/]\n"
            );
        }
    }
}

return await app.RunAsync(args);
