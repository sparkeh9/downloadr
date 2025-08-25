namespace Downloadr.Cli.Features.Ui;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Spectre.Console;
using Spectre.Console.Cli;
using Downloadr.Cli.Features.Queue;
using Downloadr.Cli.Features.Download;
using Spectre.Console.Rendering;

public sealed class TopCommand : Command<TopCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        public int RefreshMs { get; init; } = 500;
        [CommandOption("--parallel|-p")]
        public int? Parallelism { get; init; }
    }

    private readonly TopUiRunner runner;

    public TopCommand(TopUiRunner runner)
    {
        this.runner = runner;
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        return runner.Run(settings.RefreshMs, settings.Parallelism);
    }
}


