namespace Downloadr.Cli.Features.Persistence;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Downloadr.Cli.Features.Domain;

public sealed class FileDownloadRepository : IDownloadRepository
{
    private readonly string rootPath;
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true
    };

    public FileDownloadRepository()
    {
        rootPath = Path.Combine(Directory.GetCurrentDirectory(), "data", "items");
    }

    public void Initialise()
    {
        Directory.CreateDirectory(rootPath);
    }

    public IEnumerable<DownloadItem> GetAll()
    {
        if (!Directory.Exists(rootPath)) yield break;

        foreach (var file in Directory.EnumerateFiles(rootPath, "*.json", SearchOption.TopDirectoryOnly))
        {
            DownloadItem? item = null;
            try
            {
                var json = File.ReadAllText(file);
                item = JsonSerializer.Deserialize<DownloadItem>(json, jsonOptions);
            }
            catch
            {
                // ignore corrupted entries for now
            }
            if (item != null) yield return item;
        }
    }

    public DownloadItem? Get(Guid id)
    {
        var path = GetItemPath(id);
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<DownloadItem>(json, jsonOptions);
    }

    public void Upsert(DownloadItem item)
    {
        Directory.CreateDirectory(rootPath);
        var path = GetItemPath(item.Id);
        var json = JsonSerializer.Serialize(item, jsonOptions);
        File.WriteAllText(path, json);
    }

    private string GetItemPath(Guid id) => Path.Combine(rootPath, $"{id}.json");

    public void Delete(Guid id)
    {
        var path = GetItemPath(id);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void DeleteMany(IEnumerable<Guid> ids)
    {
        foreach (var id in ids)
        {
            Delete(id);
        }
    }
}


