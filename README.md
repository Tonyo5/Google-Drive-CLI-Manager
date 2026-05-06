# GDrive CLI Manager

A command-line interface for Google Drive built with **.NET 8**. It supports OAuth 2.0 authentication, parallel file synchronisation,
Drive search with download-status indicators, and file uploads.

---

## Table of Contents

- [Prerequisites](#prerequisites)
- [Getting Your Google Credentials](#getting-your-google-credentials)
- [Build & Run](#build--run)
- [Commands](#commands)
  - [sync](#sync)
  - [search](#search)
  - [upload](#upload)
- [Architectural Notes](#architectural-notes)
  - [Parallel Downloads & Thread-Safe Statistics](#parallel-downloads--thread-safe-statistics)
  - [State Management (Manifest File)](#state-management-manifest-file)
  - [Project Structure](#project-structure)

---

## Prerequisites

| Requirement | Version |
|---|---|
| .NET SDK | 8.0 or later |
| Google Cloud project with Drive API enabled | Free tier |

Install .NET 8: https://dotnet.microsoft.com/en-us/download/dotnet/8.0

---

## Getting Your Google Credentials

1. Go to [Google Cloud Console](https://console.cloud.google.com/).
2. Create a new project (or select an existing one).
3. Navigate to **APIs & Services → Library** and enable **Google Drive API**.
4. Go to **APIs & Services → Credentials → Create Credentials → OAuth client ID**.
5. Choose **Desktop app** as the application type, give it a name, and click **Create**.
6. Click **Download JSON** and save the file as `client_secret.json`.

> **Where to place the file:**  
> Copy `client_secret.json` into the **root of the repository** (next to `GDriveCLI.csproj`).  
> The tool looks for `./client_secret.json` by default. You can override this with the `--credentials` flag.

---

## Build & Run

```bash
# Clone the repo
git clone https://github.com/YOUR_USERNAME/gdrive-cli.git
cd gdrive-cli

# Restore dependencies
dotnet restore

# Build
dotnet build -c Release

# Run directly (place client_secret.json in this directory first)
dotnet run -- --help

# Or publish a self-contained binary
dotnet publish -c Release -r win-x64 --self-contained   # Windows
dotnet publish -c Release -r linux-x64 --self-contained # Linux
dotnet publish -c Release -r osx-x64 --self-contained   # macOS
```

The first time any command runs, a browser window opens for the Google OAuth consent screen. After you approve,
the token is saved to `.gdrive-tokens/` and subsequent calls skip the browser flow.

---

## Commands

### sync

Downloads **all files** from your Google Drive to a local `Downloads/` directory using parallel workers.

```bash
dotnet run -- sync
# With a custom credentials path:
dotnet run -- sync --credentials /path/to/client_secret.json
```

**What it does:**
- Fetches the complete list of files from Drive (all pages).
- Skips files already present on disk (checked via the local manifest).
- Downloads remaining files in parallel (max 4 concurrent downloads).
- Exports Google Workspace files (Docs, Sheets, Slides) as PDF automatically.
- Displays a real-time progress bar during download.
- Prints a final summary table: successful, skipped, failed, total size, elapsed time.

Example output:
```
╔══════════════════════════════════════╗
║         GDrive Sync Starting         ║
╚══════════════════════════════════════╝

Fetching file list from Google Drive...
Found 47 files in Drive.

 Downloading files ████████████████████ 100% ⠋

╭───────────────────────────┬─────────────╮
│ Metric                    │ Value       │
├───────────────────────────┼─────────────┤
│ Successful downloads      │ 44          │
│ Skipped (already local)   │ 3           │
│ Failed downloads          │ 0           │
│ Total processed           │ 47          │
│ Total data downloaded     │ 128.4 MB    │
│ Time elapsed              │ 00:23.441   │
╰───────────────────────────┴─────────────╯
```

---

### search

Searches Google Drive by file/folder name and shows whether each result has been downloaded locally.

```bash
dotnet run -- search "quarterly report"
dotnet run -- search "budget"
```

Results display a `✔ Downloaded` or `✘ Not Downloaded` status for each file.

---

### upload

Uploads a local file to a specific folder path in Google Drive.

```bash
# Upload to Drive root
dotnet run -- upload ./report.pdf /

# Upload to a nested folder (created automatically if it doesn't exist)
dotnet run -- upload ./slides.pptx "projects/Q4/presentations"
```

If the destination folder path does not exist in Drive, the tool **creates the entire folder hierarchy** automatically before uploading.

---

## Architectural Notes

### Parallel Downloads & Thread-Safe Statistics

The `sync` command downloads files using `Task.WhenAll` combined with a `SemaphoreSlim` to cap concurrency at **4 simultaneous downloads**.
This design:

- Avoids thread-pool exhaustion (unbounded parallelism via `Parallel.ForEach` would create one task per file, which becomes problematic
for large Drives).
- Keeps the network load predictable and respects Google's API rate limits.
- Uses `async/await` throughout so threads are not blocked during I/O waits.

```
allFiles ──► [ Task ] ──┐
             [ Task ] ──┤─► SemaphoreSlim(4) ──► Google Drive API
             [ Task ] ──┤
             [ Task ] ──┘
```

**Thread-safe statistics** are maintained in `SyncStatistics` using `Interlocked.Increment` and `Interlocked.Add`
— lock-free atomic operations that avoid race conditions without the overhead of a mutex. Each download task calls
`RecordSuccess(bytes)` or `RecordFailure()` independently, and the final totals are always correct regardless of completion order.

### State Management (Manifest File)

Rather than performing a filesystem `File.Exists` scan for every search result (O(n) disk hits), the tool maintains a **JSON manifest**
at `.gdrive-manifest.json`.

| Field | Purpose |
|---|---|
| `DriveId` | Used as the dictionary key for O(1) lookup |
| `LocalPath` | Verified with `File.Exists` at read time |
| `DownloadedAt` | Audit trail (when the file was last synced) |

The manifest is loaded into memory at startup and flushed to disk once after `sync` completes — batching all I/O into a single write.
Writes to the in-memory dictionary during parallel sync are protected by a `lock` object.

### Project Structure

```
GDriveCLI/
├── Program.cs                      # CLI parsing (System.CommandLine) + entry point
├── GDriveCLI.csproj
├── client_secret.json              # ← Place your credentials here
│
└── src/
    ├── Auth/
    │   └── GoogleAuthService.cs    # OAuth 2.0 flow + token persistence
    │
    ├── Commands/
    │   ├── SyncCommand.cs          # Parallel download logic + stats display
    │   ├── SearchCommand.cs        # Drive search + local status display
    │   └── UploadCommand.cs        # Local → Drive upload with folder creation
    │
    ├── Services/
    │   ├── DriveApiService.cs      # All Google Drive API calls + retry logic
    │   └── LocalFileService.cs     # Filesystem operations + manifest management
    │
    └── Models/
        └── Models.cs               # DriveFileInfo, SyncStatistics, ManifestEntry
```

**Separation of concerns:**
- `Program.cs` only wires CLI arguments to command handlers — no business logic.
- `DriveApiService` encapsulates all HTTP/API concerns including paging and exponential back-off retry.
- `LocalFileService` owns all filesystem and manifest concerns.
- `SyncStatistics` is a pure thread-safe counter; it has no knowledge of Drive or files.

---

## Error Handling

| Scenario | Behaviour |
|---|---|
| `client_secret.json` missing | Clear error message with path hint |
| Rate limiting (HTTP 429) | Exponential back-off, up to 4 retries |
| Server errors (5xx) | Same retry policy |
| Individual download failure | Logged; partial file deleted; sync continues |
| Upload target path missing | Folder hierarchy created automatically |
| Network interruption during sync | Failed files counted; rest of sync continues |
