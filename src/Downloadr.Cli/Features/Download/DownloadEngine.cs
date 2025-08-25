namespace Downloadr.Cli.Features.Download;

using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Downloadr.Cli.Features.Domain;
using Downloadr.Cli.Features.Persistence;
using Downloadr.Cli.Features.Queue;
using Downloadr.Cli.Infrastructure.Configuration;

public sealed class DownloadEngine : IDownloadEngine
{
    private readonly HttpClient httpClient;
    private readonly IDownloadRepository repository;
    private readonly IQueueService queueService;
    private readonly DownloadrOptions options;
    private readonly ConcurrentDictionary<Guid, byte> inProgress = new();
    private readonly ConcurrentDictionary<Guid, byte> paused = new();
    private readonly ConcurrentDictionary<Guid, byte> cancelled = new();

    public DownloadEngine(HttpClient httpClient,
                          IDownloadRepository repository,
                          IQueueService queueService,
                          DownloadrOptions options)
    {
        this.httpClient = httpClient;
        this.repository = repository;
        this.queueService = queueService;
        this.options = options;
        httpClient.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
    }

    public async Task RunAsync(CancellationToken cancellationToken, int? maxConcurrencyOverride = null)
    {
        // normalise any items left in Running to Paused so they are picked up again
        foreach (var item in queueService.List().Where(i => i.Status == DownloadStatus.Running))
        {
            item.Status = DownloadStatus.Paused;
            repository.Upsert(item);
        }

        var channel = Channel.CreateUnbounded<DownloadItem>();

        // Producer
        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                foreach (var item in queueService.List().Where(i => i.Status is DownloadStatus.Queued or DownloadStatus.Paused))
                {
                    if (inProgress.ContainsKey(item.Id))
                        continue;
                    await channel.Writer.WriteAsync(item, cancellationToken);
                }
                await Task.Delay(1000, cancellationToken);
            }
            channel.Writer.Complete();
        }, cancellationToken);

        // Consumers
        var degree = Math.Max(1, maxConcurrencyOverride ?? options.MaxConcurrentDownloads);
        var workers = Enumerable.Range(0, degree)
                                .Select(_ => WorkerAsync(channel.Reader, cancellationToken))
                                .ToArray();

        await Task.WhenAll(workers);
    }

    private async Task WorkerAsync(ChannelReader<DownloadItem> reader, CancellationToken cancellationToken)
    {
        await foreach (var item in reader.ReadAllAsync(cancellationToken))
        {
            if (!inProgress.TryAdd(item.Id, 0))
                continue;
            try
            {
                await DownloadAsync(item, cancellationToken);
            }
            catch
            {
                item.Status = DownloadStatus.Failed;
                repository.Upsert(item);
            }
            finally
            {
                inProgress.TryRemove(item.Id, out _);
            }
        }
    }

    private async Task DownloadAsync(DownloadItem item, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(item.DestinationPath) ?? options.DownloadDirectory);

        var partPath = item.DestinationPath + ".part";
        long existing = 0;
        if (File.Exists(partPath))
        {
            existing = new FileInfo(partPath).Length;
        }
        else if (File.Exists(item.DestinationPath))
        {
            // already completed
            var finalInfo = new FileInfo(item.DestinationPath);
            item.DownloadedBytes = finalInfo.Length;
            item.TotalBytes ??= finalInfo.Length;
            item.Status = DownloadStatus.Completed;
            item.CompletedAtUtc = DateTimeOffset.UtcNow;
            repository.Upsert(item);
            return;
        }

        var request = new HttpRequestMessage(HttpMethod.Get, item.Url);
        if (existing > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existing, null);
            if (!string.IsNullOrWhiteSpace(item.ETag))
            {
                if (EntityTagHeaderValue.TryParse(item.ETag, out var etag))
                {
                    request.Headers.IfRange = new RangeConditionHeaderValue(etag);
                }
            }
            else if (item.LastModifiedUtc.HasValue)
            {
                request.Headers.IfRange = new RangeConditionHeaderValue(item.LastModifiedUtc.Value);
            }
        }

        item.StartedAtUtc ??= DateTimeOffset.UtcNow;
        item.Status = DownloadStatus.Running;
        item.DownloadedBytes = existing;
        repository.Upsert(item);

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            var totalHeader = response.Content.Headers.ContentRange?.Length;
            if (totalHeader.HasValue && existing >= totalHeader.Value)
            {
                // rename part to final
                if (File.Exists(partPath))
                {
                    File.Move(partPath, item.DestinationPath, overwrite: true);
                }
                item.TotalBytes = totalHeader.Value;
                item.DownloadedBytes = totalHeader.Value;
                item.Status = DownloadStatus.Completed;
                item.CompletedAtUtc = DateTimeOffset.UtcNow;
                repository.Upsert(item);
                return;
            }
            if (File.Exists(partPath)) File.Delete(partPath);
            existing = 0;
            request = new HttpRequestMessage(HttpMethod.Get, item.Url);
            response.Dispose();
            // restart from scratch
            using var fresh = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            fresh.EnsureSuccessStatusCode();
            await WriteResponseAsync(item, fresh, partPath, existing, cancellationToken);
            return;
        }

        if (response.StatusCode == HttpStatusCode.OK && existing > 0)
        {
            if (File.Exists(partPath)) File.Delete(partPath);
            existing = 0;
        }

        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength + existing;
        if (total.HasValue)
        {
            item.TotalBytes = total.Value;
        }
        item.ETag = response.Headers.ETag?.Tag;
        item.LastModifiedUtc = response.Content.Headers.LastModified;
        repository.Upsert(item);

        await WriteResponseAsync(item, response, partPath, existing, cancellationToken);

        // move to final on completion if not paused/cancelled
        if (cancelled.ContainsKey(item.Id))
        {
            if (File.Exists(partPath)) File.Delete(partPath);
            item.Status = DownloadStatus.Cancelled;
            repository.Upsert(item);
            return;
        }
        if (paused.ContainsKey(item.Id))
        {
            item.Status = DownloadStatus.Paused;
            repository.Upsert(item);
            return;
        }

        if (File.Exists(partPath))
        {
            File.Move(partPath, item.DestinationPath, overwrite: true);
        }
        item.Status = DownloadStatus.Completed;
        item.CompletedAtUtc = DateTimeOffset.UtcNow;
        repository.Upsert(item);
    }

    private async Task WriteResponseAsync(DownloadItem item, HttpResponseMessage response, string partPath, long existing, CancellationToken cancellationToken)
    {
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(partPath, FileMode.Append, FileAccess.Write, FileShare.Read);

        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            var lastUpdate = DateTimeOffset.UtcNow;
            var bytesSinceLast = 0L;
            int read;
            while ((read = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                if (cancelled.ContainsKey(item.Id))
                {
                    return;
                }
                if (paused.ContainsKey(item.Id))
                {
                    return;
                }
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                item.DownloadedBytes += read;
                bytesSinceLast += read;

                var now = DateTimeOffset.UtcNow;
                var elapsed = (now - lastUpdate).TotalSeconds;
                if (elapsed >= 0.5)
                {
                    item.AverageBytesPerSecond = item.AverageBytesPerSecond.HasValue
                        ? (item.AverageBytesPerSecond.Value * 0.7) + ((bytesSinceLast / elapsed) * 0.3)
                        : (bytesSinceLast / elapsed);
                    repository.Upsert(item);
                    lastUpdate = now;
                    bytesSinceLast = 0;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public void Pause(Guid itemId)
    {
        paused[itemId] = 0;
        var item = repository.Get(itemId);
        if (item != null)
        {
            item.Status = DownloadStatus.Paused;
            repository.Upsert(item);
        }
    }

    public void Resume(Guid itemId)
    {
        paused.TryRemove(itemId, out _);
        cancelled.TryRemove(itemId, out _);
        var item = repository.Get(itemId);
        if (item != null)
        {
            item.Status = DownloadStatus.Queued;
            repository.Upsert(item);
        }
    }

    public void Cancel(Guid itemId)
    {
        cancelled[itemId] = 0;
    }

    public void PauseAll()
    {
        foreach (var item in repository.GetAll())
        {
            paused[item.Id] = 0;
            item.Status = DownloadStatus.Paused;
            repository.Upsert(item);
        }
    }

    public void ResumeAll()
    {
        paused.Clear();
        foreach (var item in repository.GetAll())
        {
            if (item.Status is DownloadStatus.Failed or DownloadStatus.Paused)
            {
                item.Status = DownloadStatus.Queued;
                repository.Upsert(item);
            }
        }
    }
}
