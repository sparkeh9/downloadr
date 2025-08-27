namespace Downloadr.Cli.Infrastructure.Bootstrapping;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Spectre.Console;
using Spectre.Console.Cli;
using Features.Persistence;
using Features.Queue;
using Downloadr.Cli.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Features.Download;
using Features.Ui;

public class Bootstrapper
{
    public static CommandApp Bootstrap()
    {
        var configuration = Configuration.BuildConfig();
        Log.Logger = Observability.CreateLogger(configuration);

        var services = new ServiceCollection()
                      .AddSingleton( AnsiConsole.Create( new AnsiConsoleSettings() ) )
                      .AddSingleton( Log.Logger )
                      .AddSingleton<IConfiguration>( configuration )
                      .Configure<DownloadrOptions>( configuration.GetSection( nameof(DownloadrOptions) ) )
                      .AddSingleton( sp => sp.GetRequiredService<IOptions<DownloadrOptions>>().Value )
                      // Features
                      .AddHttpClient()
                      .AddSingleton<IDownloadEngine, DownloadEngine>()
                      .AddSingleton<TopUiRunner>()
                      .AddSingleton<IDownloadRepository, FileDownloadRepository>()
                      .AddSingleton<IQueueService, QueueService>();
        
        var app = new CommandApp(new SpectreConsole.TypeRegistrar(services));
        app.Configure(config =>
        {
            config.SetApplicationName("downloadr")
                  .SetApplicationVersion(System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0-dev")
                  .ConfigureCommands()
                  .SetHelpProvider(new CliHelpProvider(config.Settings))
                  .ValidateExamples()
                  .SetExceptionHandler((ex, resolver) =>
                  {
                      Log.Error(ex, "An error occurred: {Message}", ex.Message);
                      AnsiConsole.MarkupLine("[red]Error:[/] {0}", ex.Message);
                      return -1;
                  });
        });

        return app;
    }
}