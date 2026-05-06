using Services;
using Model;
using Spectre.Console;
using System.Collections.Concurrent;

namespace GoogleDriveCli.Command
{
    public class SyncCommand
    {
        private readonly IApiService _driveApi;
        private readonly LocalFileService _localFiles;

        private const int MaxDegreeOfParallelism = 4;

        public SyncCommand(IApiService driveApi, LocalFileService localFiles)
        {
            _driveApi = driveApi;
            _localFiles = localFiles;
        }

        public async Task<int> ExecuteAsync(CancellationToken c = default)
        {
            AnsiConsole.MarkupLine("[bold cyan]╔══════════════════════════════════════╗[/]");
            AnsiConsole.MarkupLine("[bold cyan]║         GDrive Sync Starting         ║[/]");
            AnsiConsole.MarkupLine("[bold cyan]╚══════════════════════════════════════╝[/]");
            AnsiConsole.MarkupLine($"[grey]Downloads directory: {_localFiles.DownloadsDirectory}[/]");

            AnsiConsole.MarkupLine("\n[yellow]Fetching file list from Google Drive...[/]");
            List<DriveFileInfo> allFiles;

            try
            {
                allFiles = await _driveApi.ListAllFilesAsync(c);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed to list files: {ex.Message}[/]");
                return 1;
            }

            AnsiConsole.MarkupLine($"[green]Found {allFiles.Count} files in Drive.[/]\n");

            if (allFiles.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No files to download.[/]");
                return 0;
            }

            var stats = new SyncStatistic();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var errors = new ConcurrentBag<string>();

            var semaphore = new SemaphoreSlim(MaxDegreeOfParallelism, MaxDegreeOfParallelism);
            int completed = 0;

            var downloadTasks = allFiles.Select(file => DownloadOneFileAsync(
                file, stats, errors, semaphore, allFiles.Count, c,
                () =>
                {
                    var current = Interlocked.Increment(ref completed);
                    Console.WriteLine($"[{current}/{allFiles.Count}] {file.Name}");
                })).ToList();

            await Task.WhenAll(downloadTasks);

            stopwatch.Stop();

            _localFiles.SaveManifest();

            PrintSummary(stats, stopwatch.Elapsed, errors);

            return stats.FailedCount > 0 ? 1 : 0;
        }

        private async Task DownloadOneFileAsync(
            DriveFileInfo file,
            SyncStatistic stats,
            ConcurrentBag<string> errors,
            SemaphoreSlim semaphore,
            int total,
            CancellationToken c,
            Action onProgress)
        {
            await semaphore.WaitAsync(c);
            try
            {
                if (_localFiles.IsDownloaded(file.Id))
                {
                    stats.RecordSkipped();
                    onProgress();
                    return;
                }

                var localPath = _localFiles.GetLocalPath(file.Name);

                await using var fileStream = new FileStream(
                    localPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    bufferSize: 81920, useAsync: true);

                await _driveApi.DownloadFileAsync(file.Id, file.MimeType ?? string.Empty, fileStream, c);

                var fileSize = new FileInfo(localPath).Length;
                _localFiles.RecordDownload(file.Id, localPath);
                stats.RecordSuccess(fileSize);
            }
            catch (OperationCanceledException)
            {
                stats.RecordFailure();
                errors.Add($"{file.Name}: cancelled");
            }
            catch (Exception ex)
            {
                stats.RecordFailure();
                errors.Add($"{file.Name}: {ex.Message}");

                var localPath = _localFiles.GetLocalPath(file.Name);
                if (File.Exists(localPath))
                {
                    try { File.Delete(localPath); } catch { }
                }
            }
            finally
            {
                semaphore.Release();
                onProgress();
            }
        }

        private static void PrintSummary(SyncStatistic stats, TimeSpan elapsed, ConcurrentBag<string> errors)
        {
            AnsiConsole.WriteLine();
            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title("[bold white]Sync Summary[/]")
                .AddColumn("[bold]Metric[/]")
                .AddColumn("[bold]Value[/]");

            table.AddRow("[green]Successful downloads[/]", $"[green]{stats.SuccessCount}[/]");
            table.AddRow("[yellow]Skipped (already local)[/]", $"[yellow]{stats.SkippedCount}[/]");
            table.AddRow("[red]Failed downloads[/]", $"[red]{stats.FailedCount}[/]");
            table.AddRow("Total processed", stats.TotalProcessed.ToString());
            table.AddRow("Total data downloaded", FormatBytes(stats.TotalBytesSynced));
            table.AddRow("Time elapsed", elapsed.ToString(@"mm\:ss\.fff"));

            AnsiConsole.Write(table);

            if (!errors.IsEmpty)
            {
                AnsiConsole.MarkupLine("\n[red]Errors:[/]");
                foreach (var error in errors)
                    AnsiConsole.MarkupLine($"  [red]•[/] {Markup.Escape(error)}");
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}