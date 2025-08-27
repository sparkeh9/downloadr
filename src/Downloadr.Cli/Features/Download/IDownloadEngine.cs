namespace Downloadr.Cli.Features.Download;

using System.Threading;
using System.Threading.Tasks;

public interface IDownloadEngine
{
    Task RunAsync(CancellationToken cancellationToken, int? maxConcurrencyOverride = null);
    void Pause(Guid itemId);
    void Resume(Guid itemId);
    void Cancel(Guid itemId);
    void PauseAll();
    void ResumeAll();
    void CancelAll();
    int GetDesiredConcurrency();
    void SetDesiredConcurrency(int value);
}
