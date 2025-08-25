namespace Downloadr.Cli.Features.Ui;

using System;
using System.Linq;
using System.Threading;
using Downloadr.Cli.Features.Download;
using Downloadr.Cli.Features.Queue;
using Downloadr.Cli.Infrastructure.Configuration;
using Spectre.Console;
using Spectre.Console.Rendering;

public sealed class TopUiRunner
{
    private readonly IQueueService queueService;
    private readonly IAnsiConsole console;
    private readonly IDownloadEngine engine;
    private readonly DownloadrOptions options;
    private RateUnitMode rateMode = RateUnitMode.Bytes;

    public TopUiRunner(IQueueService queueService, IAnsiConsole console, IDownloadEngine engine, DownloadrOptions options)
    {
        this.queueService = queueService;
        this.console = console;
        this.engine = engine;
        this.options = options;
    }

    public int Run(int refreshMs, int? parallelism)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var engineTask = engine.RunAsync(cts.Token, parallelism);

        var selectedIndex = 0;
        var help = new Panel(new Markup("[bold]Keys:[/] Up/Down select  |  A add  |  P pause  |  R resume  |  C cancel  |  G pause all  |  H resume all  |  X clear completed  |  U units  |  Q quit"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Grey50)
        };

        var requestAdd = false;
        while (!cts.IsCancellationRequested)
        {
            console.Live(RenderWithChrome()).Start(ctx =>
            {
                while (!cts.IsCancellationRequested && !requestAdd)
                {
                    HandleKeyInput(ref selectedIndex, cts, () => requestAdd = true);
                    ctx.UpdateTarget(RenderWithChrome());
                    ctx.Refresh();
                    Thread.Sleep(refreshMs);
                }
            });

            if (requestAdd)
            {
                // temporarily leave live view to accept input
                QueueNewDownloadsInteractive();
                requestAdd = false;
                continue;
            }
            break;
        }

        cts.Cancel();
        try { engineTask.Wait(1000); } catch { }
        return 0;

        IRenderable RenderWithChrome()
        {
            var layout = new Layout("Root").SplitRows(
                new Layout("Help") { Size = 3 },
                new Layout("Table")
            );
            layout["Help"].Update(help);
            layout["Table"].Update(RenderTable(selectedIndex));
            return layout;
        }
    }

    private Table RenderTable(int selectedIndex = -1)
    {
        var items = queueService.List();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Id")
            .AddColumn("Status")
            .AddColumn("Progress")
            .AddColumn("Speed")
            .AddColumn("ETA")
            .AddColumn("File");

        var ordered = items.OrderBy(i => i.Status).ThenBy(i => i.StartedAtUtc).ToArray();
        for (var i = 0; i < ordered.Length; i++)
        {
            var item = ordered[i];
            var progress = item.TotalBytes.HasValue && item.TotalBytes > 0
                ? $"{(item.DownloadedBytes * 100.0 / item.TotalBytes.Value):0.0}%"
                : "-";
            var speed = FormatRate(item.AverageBytesPerSecond);
            var eta = item.EstimatedTimeRemaining.HasValue
                ? item.EstimatedTimeRemaining.Value.ToString()
                : "-";

            var idCell = item.ShortId;
            if (i == selectedIndex) idCell = $"[bold yellow]>[/] {idCell}";

            table.AddRow(
                idCell,
                item.Status.ToString(),
                progress,
                speed,
                eta,
                System.IO.Path.GetFileName(item.DestinationPath)
            );
        }

        if (items.Count == 0)
        {
            table.AddRow("-", "-", "-", "-", "-", "(empty queue)");
        }

        return table;
    }

    private void HandleKeyInput(ref int selectedIndex, CancellationTokenSource cts, System.Action requestAdd)
    {
        if (!console.Input.IsKeyAvailable()) return;
        var key = console.Input.ReadKey(intercept: true);
        if (key == null) return;
        var items = queueService.List().OrderBy(i => i.Status).ThenBy(i => i.StartedAtUtc).ToArray();
        if (items.Length == 0)
        {
            switch (key.Value.Key)
            {
                case ConsoleKey.A:
                    requestAdd();
                    break;
                case ConsoleKey.Q:
                    cts.Cancel();
                    break;
            }
            return;
        }

        selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, items.Length - 1));
        switch (key.Value.Key)
        {
            case ConsoleKey.UpArrow:
                selectedIndex = Math.Max(0, selectedIndex - 1);
                break;
            case ConsoleKey.DownArrow:
                selectedIndex = Math.Min(items.Length - 1, selectedIndex + 1);
                break;
            case ConsoleKey.P:
                engine.Pause(items[selectedIndex].Id);
                break;
            case ConsoleKey.R:
                engine.Resume(items[selectedIndex].Id);
                break;
            case ConsoleKey.C:
                engine.Cancel(items[selectedIndex].Id);
                break;
            case ConsoleKey.G:
                engine.PauseAll();
                break;
            case ConsoleKey.H:
                engine.ResumeAll();
                break;
            case ConsoleKey.X:
                queueService.ClearCompleted();
                break;
            case ConsoleKey.A:
                requestAdd();
                break;
            case ConsoleKey.U:
                rateMode = rateMode == RateUnitMode.Bytes ? RateUnitMode.Bits : RateUnitMode.Bytes;
                break;
            case ConsoleKey.Q:
                cts.Cancel();
                break;
        }
    }

    private void QueueNewDownloadsInteractive()
    {
        // destination prompt (single-line)
        var destination = AnsiConsole.Prompt(
            new TextPrompt<string>("Destination directory:")
                .DefaultValue(options.DownloadDirectory)
                .AllowEmpty());

        // modal-style panel for multiline paste
        var modal = new Panel(new Markup("Paste URLs (CR or CRLF separated) below, then press [bold]Ctrl+Z[/] and [bold]Enter[/] to finish:"))
        {
            Header = new PanelHeader(" Add URLs ", Justify.Center),
            Border = BoxBorder.Double,
            BorderStyle = new Style(Color.CadetBlue)
        };
        console.Write(modal);

        var lines = new System.Collections.Generic.List<string>();
        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            lines.Add(line);
        }

        var urls = Downloadr.Cli.Features.Queue.PasteUrlsCommand.ParseUrls(lines);
        if (urls.Count == 0)
        {
            console.MarkupLine("[yellow]No valid URLs provided.[/]");
            return;
        }

        queueService.AddRange(urls, destination);
        console.MarkupLine($"[green]{urls.Count}[/] item(s) added to queue → [blue]{destination}[/]");
    }

    private enum RateUnitMode
    {
        Bytes,
        Bits
    }

    private string FormatRate(double? bytesPerSecond)
    {
        if (!bytesPerSecond.HasValue || bytesPerSecond <= 0) return "-";
        var value = bytesPerSecond.Value;
        string[] units;
        if (rateMode == RateUnitMode.Bits)
        {
            value *= 8.0; // bytes → bits
            units = new[] { "b/s", "kb/s", "Mb/s", "Gb/s", "Tb/s" };
        }
        else
        {
            units = new[] { "B/s", "KB/s", "MB/s", "GB/s", "TB/s" };
        }

        var unitIndex = 0;
        while (value >= 1000.0 && unitIndex < units.Length - 1)
        {
            value /= 1000.0;
            unitIndex++;
        }

        return $"{value:0.0} {units[unitIndex]}";
    }
}


