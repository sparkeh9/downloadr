namespace Downloadr.Cli.Infrastructure.Configuration;

public sealed class DownloadrOptions
{
    public string DownloadDirectory { get; init; } = "downloads";
    public int MaxConcurrentDownloads { get; init; } = 3;
    public int RequestTimeoutSeconds { get; init; } = 100;
}


