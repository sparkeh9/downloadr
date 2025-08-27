namespace Downloadr.Cli.Features.Ui;

using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using Download;
using Queue;
using Infrastructure.Configuration;
using Domain;
using Spectre.Console;
using Spectre.Console.Rendering;

public sealed class TopUiRunner
{
    private readonly IQueueService queueService;
    private readonly IAnsiConsole console;
    private readonly IDownloadEngine engine;
    private readonly DownloadrOptions options;
    private RateUnitMode rateMode = RateUnitMode.Bytes;
    private SortColumn sortColumn = SortColumn.Name;
    private bool sortAscending = false;
    private bool sortFocus = true;
    private static readonly SortColumn[] sortCycle = new[] { SortColumn.Name, SortColumn.Status, SortColumn.Progress, SortColumn.Speed, SortColumn.Eta };

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

        var requestAdd = false;
        var topIndex = 0;
        var maxFps = Math.Max(1, options.UiMaxFps);
        var minFrameMs = (int)Math.Floor(1000.0 / maxFps);
        while (!cts.IsCancellationRequested)
        {
            console.Live(RenderWithChrome()).Start(ctx =>
            {
                while (!cts.IsCancellationRequested && !requestAdd)
                {
                    var frameStart = DateTime.UtcNow;
                    HandleKeyInput(ref selectedIndex, cts, () => requestAdd = true);
                    ctx.UpdateTarget(RenderWithChrome());
                    ctx.Refresh();
                    var elapsed = (int)(DateTime.UtcNow - frameStart).TotalMilliseconds;
                    var remaining = minFrameMs - elapsed;
                    if (remaining > 0)
                    {
                        Thread.Sleep(remaining);
                    }
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
            var totalRows = console.Profile.Height;
            var pageSize = Math.Max(1, totalRows - 8 - 6);
            if (selectedIndex < topIndex)
                topIndex = selectedIndex;
            else if (selectedIndex >= topIndex + pageSize)
                topIndex = selectedIndex - pageSize + 1;

            var table = RenderTable(selectedIndex, topIndex, pageSize);
            var sortInfo = BuildSortInfo(topIndex, pageSize);
            var controls = BuildControlsPanel();
            return new Rows(table, sortInfo, controls);
        }
    }

    private IRenderable RenderTable(int selectedIndex = -1, int topIndex = 0, int pageSize = int.MaxValue)
    {
        var items = queueService.List();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn(GetColumnHeader("Name", SortColumn.Name)).NoWrap())
            .AddColumn(new TableColumn(GetColumnHeader("Status", SortColumn.Status)).NoWrap())
            .AddColumn(new TableColumn(GetColumnHeader("Progress", SortColumn.Progress)).NoWrap())
            .AddColumn(new TableColumn(GetColumnHeader("Speed", SortColumn.Speed)).NoWrap())
            .AddColumn(new TableColumn(GetColumnHeader("ETA", SortColumn.Eta)).NoWrap());

        var borderColour = IsDarkBackground() ? Color.Grey23 : Color.Grey50;
        table.BorderStyle = new Style(borderColour);

        // Compute total throughput for footer under Speed
        var totalBps = items
            .Where(i => i.Status == DownloadStatus.Running)
            .Select(i => i.AverageBytesPerSecond ?? 0)
            .Where(v => v > 0)
            .Sum();
        var totalThroughput = totalBps > 0 ? FormatRate(totalBps) : string.Empty;
        if (!string.IsNullOrEmpty(totalThroughput))
        {
            table.Columns[3].Footer = new Markup($"[grey53]{Markup.Escape(totalThroughput)}[/]");
        }

        var ordered = ApplySort(items).ToArray();
        var visible = ordered.Skip(topIndex).Take(pageSize).ToArray();
        for (var i = 0; i < visible.Length; i++)
        {
            var item = visible[i];
            var rowColour = GetRowColour(item.Status);
            var progress = item.TotalBytes.HasValue && item.TotalBytes > 0
                ? $"{(item.DownloadedBytes * 100.0 / item.TotalBytes.Value):0.0}%"
                : "-";
            var speed = item.Status == DownloadStatus.Completed
                ? string.Empty
                : FormatRate(item.AverageBytesPerSecond);
            var eta = item.Status == DownloadStatus.Completed
                ? string.Empty
                : (item.EstimatedTimeRemaining.HasValue
                    ? item.EstimatedTimeRemaining.Value.ToString()
                    : "-");

            var displayName = GetDisplayName(item);
            var truncated = TruncateWithEllipsis(displayName, GetNameMaxWidth());
            var safeName = Markup.Escape(truncated);
            var absoluteIndex = topIndex + i;
            var styledName = string.IsNullOrEmpty(rowColour) ? safeName : $"[{rowColour}]{safeName}[/]";
            var nameCell = absoluteIndex == selectedIndex ? $"[bold yellow]>[/] {styledName}" : styledName;

            var statusCell = item.Status.ToString();
            if (!string.IsNullOrEmpty(rowColour)) statusCell = $"[{rowColour}]{Markup.Escape(statusCell)}[/]";
            var progressCell = string.IsNullOrEmpty(rowColour) ? progress : $"[{rowColour}]{progress}[/]";
            var speedCell = string.IsNullOrEmpty(speed) ? string.Empty : (string.IsNullOrEmpty(rowColour) ? speed : $"[{rowColour}]{speed}[/]");
            var etaCell = string.IsNullOrEmpty(eta) ? string.Empty : (string.IsNullOrEmpty(rowColour) ? eta : $"[{rowColour}]{eta}[/]");

            table.AddRow(
                nameCell,
                statusCell,
                progressCell,
                speedCell,
                etaCell
            );
        }

        if (items.Count == 0)
        {
            table.AddRow("(empty queue)", "", "", "", "");
        }

        var termWidth = console.Profile.Width;
        if (termWidth <= Downloadr.Cli.Constants.UiLayout.MaxTableWidth)
        {
            table.Expand();
        }
        return table;
    }

    private IRenderable BuildSortInfo(int topIndex, int pageSize)
    {
        var items = queueService.List();
        var sortName = sortColumn switch
        {
            SortColumn.Name => "Name",
            SortColumn.Progress => "Progress",
            SortColumn.Speed => "Speed",
            SortColumn.Eta => "ETA",
            _ => "Status"
        };
        var dirText = sortAscending ? "ASC" : "DESC";
        var start = items.Count == 0 ? 0 : Math.Min(items.Count, topIndex + 1);
        var end = items.Count == 0 ? 0 : Math.Min(items.Count, topIndex + pageSize);
        var text = new Markup($"[grey53]Sort: {sortName} {dirText}  |  Concurrency: {engine.GetDesiredConcurrency()}  |  Items {start}–{end}/{items.Count}[/]");
        return text;
    }

    private IRenderable BuildControlsPanel()
    {
        var isDark = IsDarkBackground();
        var keyColour = isDark ? Downloadr.Cli.Constants.UiColours.KeyDark : Downloadr.Cli.Constants.UiColours.KeyLight;
        var textColour = isDark ? Downloadr.Cli.Constants.UiColours.TextDark : Downloadr.Cli.Constants.UiColours.TextLight;

        var markup = new Markup($@"[bold]Controls[/]
[{textColour}]Selection:[/] [bold {keyColour}]Tab[/] next, [bold {keyColour}]Shift+Tab[/] previous  |  [{textColour}]Add:[/] [bold {keyColour}]A[/]
[{textColour}]Item:[/] [bold {keyColour}]Space[/] pause/resume, [bold {keyColour}]C[/] cancel/undo cancel
[{textColour}]Global:[/] [bold {keyColour}]S[/] pause all, [bold {keyColour}]H[/] resume all, [bold {keyColour}]K[/] stop all
[{textColour}]Cleanup:[/] [bold {keyColour}]X[/] remove finished, [bold {keyColour}]Z[/] stop & remove all
[{textColour}]Sorting:[/] [bold {keyColour}]Left[/]/[bold {keyColour}]Right[/] column  |  [bold {keyColour}]Up[/] asc, [bold {keyColour}]Down[/] desc
[{textColour}]Concurrency:[/] [bold {keyColour}]+[/] increase, [bold {keyColour}]-[/] decrease   |   [{textColour}]Current:[/] {engine.GetDesiredConcurrency()}   |   [bold {keyColour}]Q[/] quit");
        return new Panel(markup)
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Grey50)
        };
    }

    private static bool IsDarkBackground()
    {
        try
        {
            return Console.BackgroundColor is ConsoleColor.Black
                   or ConsoleColor.DarkBlue
                   or ConsoleColor.DarkGreen
                   or ConsoleColor.DarkCyan
                   or ConsoleColor.DarkRed
                   or ConsoleColor.DarkMagenta
                   or ConsoleColor.DarkGray;
        }
        catch
        {
            return true;
        }
    }

    private string GetColumnHeader(string label, SortColumn column)
    {
        if (sortColumn != column)
        {
            return label;
        }
        var arrow = sortAscending ? "^" : "v";
        var colour = sortFocus ? "chartreuse3" : "deepskyblue1";
        return $"[{colour}]{Markup.Escape(label)} {arrow}[/]";
    }

    private static string GetDisplayName(DownloadItem item)
    {
        var name = Path.GetFileName(item.DestinationPath);
            return string.IsNullOrWhiteSpace(name) ? Extensions.UriExtensions.ToSafeFileName(item.Url) : name;
    }

    private void HandleKeyInput(ref int selectedIndex, CancellationTokenSource cts, Action requestAdd)
    {
        if (!console.Input.IsKeyAvailable() && !Console.KeyAvailable) return;
        var key = TryReadKey();
        if (key == null) return;
        var items = ApplySort(queueService.List()).ToArray();
        // Allow sort/concurrency controls even when list is empty
        if (items.Length == 0)
        {
            switch (key.Value.Key)
            {
                case ConsoleKey.LeftArrow:
                    {
                        var idx = Array.IndexOf(sortCycle, sortColumn);
                        idx = (idx - 1 + sortCycle.Length) % sortCycle.Length;
                        sortColumn = sortCycle[idx];
                        sortAscending = false;
                        break;
                    }
                case ConsoleKey.RightArrow:
                    {
                        var idx = Array.IndexOf(sortCycle, sortColumn);
                        idx = (idx + 1) % sortCycle.Length;
                        sortColumn = sortCycle[idx];
                        sortAscending = false;
                        break;
                    }
                case ConsoleKey.UpArrow:
                    sortAscending = true; break;
                case ConsoleKey.DownArrow:
                    sortAscending = false; break;
                case ConsoleKey.OemPlus:
                case ConsoleKey.Add:
                    engine.SetDesiredConcurrency(engine.GetDesiredConcurrency() + 1); break;
                case ConsoleKey.OemMinus:
                case ConsoleKey.Subtract:
                    engine.SetDesiredConcurrency(engine.GetDesiredConcurrency() - 1); break;
                case ConsoleKey.A:
                    requestAdd(); break;
                case ConsoleKey.Q:
                    cts.Cancel(); break;
            }
            return;
        }

        selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, items.Length - 1));
        switch (key.Value.Key)
        {
            case ConsoleKey.UpArrow:
            case ConsoleKey.DownArrow:
                // toggle sort direction
                sortAscending = !sortAscending;
                break;
            case ConsoleKey.LeftArrow:
                // change sort column
                {
                    var idx = Array.IndexOf(sortCycle, sortColumn);
                    idx = (idx - 1 + sortCycle.Length) % sortCycle.Length;
                    sortColumn = sortCycle[idx];
                }
                break;
            case ConsoleKey.RightArrow:
                // change sort column
                {
                    var idx = Array.IndexOf(sortCycle, sortColumn);
                    idx = (idx + 1) % sortCycle.Length;
                    sortColumn = sortCycle[idx];
                }
                break;
            case ConsoleKey.Tab:
                // selection navigation (wrap). Detect Shift via modifiers
                if ((key.Value.Modifiers & ConsoleModifiers.Shift) == ConsoleModifiers.Shift)
                {
                    selectedIndex = selectedIndex <= 0 ? items.Length - 1 : selectedIndex - 1;
                }
                else
                {
                    selectedIndex = selectedIndex >= items.Length - 1 ? 0 : selectedIndex + 1;
                }
                break;
            case ConsoleKey.Spacebar:
                if (items[selectedIndex].Status == DownloadStatus.Paused)
                    engine.Resume(items[selectedIndex].Id);
                else if (items[selectedIndex].Status is DownloadStatus.Queued or DownloadStatus.Running)
                    engine.Pause(items[selectedIndex].Id);
                break;
            case ConsoleKey.C:
                if (items[selectedIndex].Status == DownloadStatus.Cancelled)
                {
                    engine.Resume(items[selectedIndex].Id);
                }
                else
                {
                    engine.Cancel(items[selectedIndex].Id);
                }
                break;
            case ConsoleKey.OemPlus:
            case ConsoleKey.Add:
                engine.SetDesiredConcurrency(engine.GetDesiredConcurrency() + 1);
                break;
            case ConsoleKey.OemMinus:
            case ConsoleKey.Subtract:
                engine.SetDesiredConcurrency(engine.GetDesiredConcurrency() - 1);
                break;
            case ConsoleKey.S:
                engine.PauseAll();
                break;
            case ConsoleKey.H:
                engine.ResumeAll();
                break;
            case ConsoleKey.K:
                engine.CancelAll();
                break;
            case ConsoleKey.X:
                queueService.ClearCompleted();
                break;
            case ConsoleKey.Z:
                engine.CancelAll();
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

    private ConsoleKeyInfo? TryReadKey()
    {
        try
        {
            var k = console.Input.ReadKey(intercept: true);
            if (k != null) return k.Value;
        }
        catch
        {
            // fall through to System.Console below
        }
        try
        {
            if (Console.KeyAvailable)
            {
                return Console.ReadKey(intercept: true);
            }
        }
        catch
        {
            return null;
        }
        return null;
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

        var lines = new List<string>();
        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            lines.Add(line);
        }

        var urls = PasteUrlsCommand.ParseUrls(lines);
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

    private enum SortColumn
    {
        Status,
        Name,
        Progress,
        Speed,
        Eta
    }

    private IOrderedEnumerable<DownloadItem> ApplySort(IReadOnlyCollection<DownloadItem> items)
    {
        var asc = sortAscending;
        IOrderedEnumerable<DownloadItem> ordered = sortColumn switch
        {
            SortColumn.Name => items.OrderBy(i => GetDisplayName(i), StringComparer.OrdinalIgnoreCase),
            SortColumn.Progress => items.OrderBy(i => i.TotalBytes.HasValue && i.TotalBytes > 0 ? (double)i.DownloadedBytes / i.TotalBytes.Value : 0.0),
            SortColumn.Speed => items.OrderBy(i => i.Status == DownloadStatus.Completed ? double.NegativeInfinity : (i.AverageBytesPerSecond ?? 0)),
            SortColumn.Eta => items.OrderBy(i => i.Status == DownloadStatus.Completed ? TimeSpan.MaxValue : (i.EstimatedTimeRemaining ?? TimeSpan.MaxValue)),
            _ => items.OrderBy(i => i.Status).ThenBy(i => i.StartedAtUtc)
        };
        if (!asc)
        {
            ordered = sortColumn switch
            {
                SortColumn.Name => items.OrderByDescending(i => GetDisplayName(i), StringComparer.OrdinalIgnoreCase),
                SortColumn.Progress => items.OrderByDescending(i => i.TotalBytes.HasValue && i.TotalBytes > 0 ? (double)i.DownloadedBytes / i.TotalBytes.Value : 0.0),
                SortColumn.Speed => items.OrderByDescending(i => i.Status == DownloadStatus.Completed ? double.NegativeInfinity : (i.AverageBytesPerSecond ?? 0)),
                SortColumn.Eta => items.OrderByDescending(i => i.Status == DownloadStatus.Completed ? TimeSpan.MaxValue : (i.EstimatedTimeRemaining ?? TimeSpan.MaxValue)),
                _ => items.OrderByDescending(i => i.Status).ThenByDescending(i => i.StartedAtUtc)
            };
        }
        return ordered;
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

    private int GetNameMaxWidth()
    {
        var widthLimit = Math.Min(console.Profile.Width, Downloadr.Cli.Constants.UiLayout.MaxTableWidth);
        var approxOtherColumns = Downloadr.Cli.Constants.UiLayout.ApproxOtherColumnsWidth; // borders + Status/Progress/Speed/ETA + spacing
        var max = widthLimit - approxOtherColumns;
        if (max < Downloadr.Cli.Constants.UiLayout.NameColumnMin) max = Downloadr.Cli.Constants.UiLayout.NameColumnMin;
        if (max > Downloadr.Cli.Constants.UiLayout.NameColumnMax) max = Downloadr.Cli.Constants.UiLayout.NameColumnMax;
        return max;
    }

    private static string TruncateWithEllipsis(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max) return value;
        if (max <= 1) return "…";
        return value.Substring(0, max - 1) + "…";
    }

    private string GetRowColour(DownloadStatus status)
    {
        var dark = IsDarkBackground();
        return status switch
        {
            DownloadStatus.Cancelled => dark ? Downloadr.Cli.Constants.UiColours.Dark.Cancelled : Downloadr.Cli.Constants.UiColours.Light.Cancelled,
            DownloadStatus.Running => dark ? Downloadr.Cli.Constants.UiColours.Dark.Running : Downloadr.Cli.Constants.UiColours.Light.Running,
            DownloadStatus.Queued => dark ? Downloadr.Cli.Constants.UiColours.Dark.Queued : Downloadr.Cli.Constants.UiColours.Light.Queued,
            DownloadStatus.Paused => dark ? Downloadr.Cli.Constants.UiColours.Dark.Paused : Downloadr.Cli.Constants.UiColours.Light.Paused,
            DownloadStatus.Completed => dark ? Downloadr.Cli.Constants.UiColours.Dark.Completed : Downloadr.Cli.Constants.UiColours.Light.Completed,
            DownloadStatus.Failed => dark ? Downloadr.Cli.Constants.UiColours.Dark.Failed : Downloadr.Cli.Constants.UiColours.Light.Failed,
            _ => string.Empty
        };
    }
}


