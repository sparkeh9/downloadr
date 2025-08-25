## Downloadr

Resumable, terminal-first file download manager for Windows and Linux, built with .NET and Spectre.Console.

### Highlights
- **Paste to queue**: Paste CR/CRLF-separated URLs to enqueue.
- **Top-like live UI**: Per-item status, ETA, and adaptive speed units (toggle bytes/bits).
- **Pause / resume / cancel**: For an item or globally. Clear completed/failed/cancelled.
- **Resilient resumption**: Uses `.part` files and HTTP ranges; safe to kill and restart.
- **Configurable**: Download directory and concurrency via `appsettings.json` or CLI flags.

### Getting started
#### Prerequisites
- .NET SDK 9.0+ (for building from source), or use published binaries in `dist/`.

#### Build and run from source
```powershell
dotnet restore .\downloadr.sln
dotnet build .\downloadr.sln -c Debug
dotnet run --project .\src\Downloadr.Cli\Downloadr.Cli.csproj
```

#### Usage
- Start live view (continues/resumes downloads):
```powershell
downloadr
downloadr --parallel 6   # override concurrency
```
- Queue URLs then start live view:
```powershell
downloadr queue -d D:\Downloads
```
  - In the prompt, paste CR/CRLF-separated URLs; press Ctrl+Z then Enter to finish.

#### Live UI keys
- Up/Down: select item
- A: add URLs (modal; pasting supported)
- P / R / C: pause / resume / cancel selected
- G / H: pause all / resume all
- X: clear completed, failed, cancelled (also deletes `.part` files)
- U: toggle bytes/bits speed units
- Q: quit

### Configuration
`appsettings.json` is copied to output and publish artefacts.
```json
{
  "DownloadrOptions": {
    "DownloadDirectory": "downloads",
    "MaxConcurrentDownloads": 3,
    "RequestTimeoutSeconds": 100
  }
}
```
- Override destination with `-d` on `queue` or when adding in the UI.
- Override concurrency with `--parallel|-p` on the command line.

### Persistence and logs
- Queue and progress are stored under `data/` (git-ignored). Logs under `logs/`.

### Publish binaries
Single-file, self-contained builds for Windows and Linux:
```powershell
pwsh .\scripts\publish.ps1 -Configuration Release -VersionTag v0.1.0
```
Outputs are placed in `dist/` and zipped (`downloadr-win-x64.zip`, `downloadr-linux-x64.tar.gz`).
