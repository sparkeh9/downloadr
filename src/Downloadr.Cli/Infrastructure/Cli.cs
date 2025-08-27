namespace Downloadr.Cli.Infrastructure;

using Spectre.Console.Cli;
using Features.Queue;
using Features.Ui;

public static class Cli
{
    public static IConfigurator ConfigureCommands( this IConfigurator configurator )
    {
        configurator.SetApplicationName("downloadr");
        configurator.AddExample(["top"]);

        // default command shows live view and continues downloads
        configurator.AddCommand<TopCommand>("top")
            .WithDescription("Interactive, top-like live view of downloads");

        // queue command: ingest then start live UI
        configurator.AddCommand<QueueCommand>("queue")
            .WithDescription("Paste URLs and immediately begin downloads with live view");

        return configurator;
    }
}