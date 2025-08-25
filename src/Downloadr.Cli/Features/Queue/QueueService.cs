namespace Downloadr.Cli.Features.Queue;

using System;
using System.Collections.Generic;
using System.Linq;
using Downloadr.Cli.Features.Domain;
using Downloadr.Cli.Features.Persistence;

public interface IQueueService
{
    void AddRange(IEnumerable<Uri> urls, string destinationDirectory);
    IReadOnlyCollection<DownloadItem> List();
    void ClearCompleted();
    void Delete(Guid id);
}

public sealed class QueueService : IQueueService
{
    private readonly IDownloadRepository repository;

    public QueueService(IDownloadRepository repository)
    {
        this.repository = repository;
        repository.Initialise();
    }

    public void AddRange(IEnumerable<Uri> urls, string destinationDirectory)
    {
        if (!System.IO.Directory.Exists(destinationDirectory))
        {
            System.IO.Directory.CreateDirectory(destinationDirectory);
        }
        foreach (var url in urls)
        {
            var fileName = url.Segments.LastOrDefault()?.Trim('/') ?? url.Host;
            var item = new DownloadItem
            {
                Url = url,
                DestinationPath = System.IO.Path.Combine(destinationDirectory, fileName),
                Status = DownloadStatus.Queued
            };
            repository.Upsert(item);
        }
    }

    public IReadOnlyCollection<DownloadItem> List()
    {
        return repository.GetAll().ToArray();
    }

    public void ClearCompleted()
    {
        var itemsToClear = repository.GetAll()
            .Where(i => i.Status == DownloadStatus.Completed || i.Status == DownloadStatus.Failed || i.Status == DownloadStatus.Cancelled)
            .ToArray();

        foreach (var item in itemsToClear)
        {
            var partPath = item.DestinationPath + ".part";
            if (System.IO.File.Exists(partPath))
            {
                try { System.IO.File.Delete(partPath); } catch { /* ignore */ }
            }
        }

        repository.DeleteMany(itemsToClear.Select(i => i.Id));
    }

    public void Delete(Guid id)
    {
        var item = repository.Get(id);
        if (item != null)
        {
            var partPath = item.DestinationPath + ".part";
            if (System.IO.File.Exists(partPath))
            {
                try { System.IO.File.Delete(partPath); } catch { /* ignore */ }
            }
        }
        repository.Delete(id);
    }
}


