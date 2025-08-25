namespace Downloadr.Cli.Features.Queue;

using System;
using System.Collections.Generic;
using Spectre.Console;
using Spectre.Console.Cli;
using Downloadr.Cli.Features.Ui;
using Downloadr.Cli.Infrastructure.Configuration;

public sealed class QueueCommand : Command<QueueCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--destination|-d")]
        public string? Destination { get; init; }

        [CommandOption("--parallel|-p")]
        public int? Parallelism { get; init; }

        public int RefreshMs { get; init; } = 500;
    }

    private readonly IQueueService queueService;
    private readonly IAnsiConsole console;
    private readonly DownloadrOptions options;
    private readonly TopUiRunner topUiRunner;

    public QueueCommand(IQueueService queueService, IAnsiConsole console, DownloadrOptions options, TopUiRunner topUiRunner)
    {
        this.queueService = queueService;
        this.console = console;
        this.options = options;
        this.topUiRunner = topUiRunner;
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var panel = new Panel(new Markup("Paste URLs (CR or CRLF separated), then press Ctrl+Z and Enter on Windows to end input:"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.CadetBlue)
        };
        console.Write(panel);

        var lines = ReadMultilineFromConsole();
        var urls = PasteUrlsCommand.ParseUrls(lines);

        if (urls.Count > 0)
        {
            var destination = settings.Destination ?? options.DownloadDirectory;
            queueService.AddRange(urls, destination);
            console.MarkupLine($"[green]{urls.Count}[/] item(s) added to queue → [blue]{destination}[/]");
        }
        else
        {
            console.MarkupLine("[yellow]No valid URLs provided.[/]");
        }

        return topUiRunner.Run(settings.RefreshMs, settings.Parallelism);
    }

    private static List<string> ReadMultilineFromConsole()
    {
        var list = new List<string>();
        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            list.Add(line);
        }
        return list;
    }
}
