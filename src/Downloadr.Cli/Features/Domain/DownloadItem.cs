namespace Downloadr.Cli.Features.Domain;

using System;

public sealed class DownloadItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Uri Url { get; init; } = new("about:blank");
    public string DestinationPath { get; init; } = string.Empty;

    public long? TotalBytes { get; set; }
    public long DownloadedBytes { get; set; }

    public DownloadStatus Status { get; set; } = DownloadStatus.Queued;

    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public double? AverageBytesPerSecond { get; set; }

    public string? ETag { get; set; }
    public DateTimeOffset? LastModifiedUtc { get; set; }

    public TimeSpan? EstimatedTimeRemaining
    {
        get
        {
            if (AverageBytesPerSecond is null or <= 0) return null;
            if (TotalBytes is null) return null;
            var remaining = TotalBytes.Value - DownloadedBytes;
            if (remaining <= 0) return TimeSpan.Zero;
            return TimeSpan.FromSeconds(remaining / AverageBytesPerSecond.Value);
        }
    }

    public string ShortId => Id.ToString()[..8];
}


