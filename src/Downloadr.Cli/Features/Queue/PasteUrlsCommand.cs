namespace Downloadr.Cli.Features.Queue;

using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;
using Spectre.Console.Cli;

public sealed class PasteUrlsCommand : Command<PasteUrlsCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--destination|-d")]
        public string? Destination { get; init; }
    }

    private readonly IQueueService queueService;
    private readonly IAnsiConsole console;
    private readonly Infrastructure.Configuration.DownloadrOptions options;

    public PasteUrlsCommand(IQueueService queueService, IAnsiConsole console, Infrastructure.Configuration.DownloadrOptions options)
    {
        this.queueService = queueService;
        this.console = console;
        this.options = options;
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
        var urls = ParseUrls(lines);
        if (urls.Count == 0)
        {
            console.MarkupLine("[yellow]No valid URLs provided.[/]");
            return 1;
        }

        var destination = settings.Destination ?? options.DownloadDirectory;
        queueService.AddRange(urls, destination);
        console.MarkupLine($"[green]{urls.Count}[/] item(s) added to queue → [blue]{destination}[/]");
        return 0;
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

    internal static List<Uri> ParseUrls(IEnumerable<string> lines)
    {
        return lines
            .SelectMany(l => l.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => Uri.TryCreate(s, UriKind.Absolute, out var uri) ? uri : null)
            .Where(uri => uri != null)
            .Cast<Uri>()
            .ToList();
    }
}


