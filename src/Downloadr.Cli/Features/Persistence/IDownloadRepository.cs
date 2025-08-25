namespace Downloadr.Cli.Features.Persistence;

using System;
using System.Collections.Generic;
using Downloadr.Cli.Features.Domain;

public interface IDownloadRepository
{
    void Initialise();
    IEnumerable<DownloadItem> GetAll();
    DownloadItem? Get(Guid id);
    void Upsert(DownloadItem item);
    void Delete(Guid id);
    void DeleteMany(IEnumerable<Guid> ids);
}


