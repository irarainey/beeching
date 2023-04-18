using Beeching.Commands;
using Beeching.Commands.Interfaces;
using Beeching.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

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
    config.AddCommand<AxeCommand>("axe")
        .WithDescription("The mighty axe that culls the resources.");

#if DEBUG
    config.PropagateExceptions();
#endif
});

return await app.RunAsync(args);
