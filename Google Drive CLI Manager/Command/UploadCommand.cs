using Services;
using Spectre.Console;

namespace GoogleDriveCli.Command
{
    public class UploadCommand
    {
        private readonly ApiService _driveApi;

        public UploadCommand(ApiService driveApi)
        {
            _driveApi = driveApi;
        }

        public async Task<int> ExecuteAsync(string localPath, string drivePath, CancellationToken c = default)
        {
            if (string.IsNullOrWhiteSpace(localPath))
            {
                AnsiConsole.MarkupLine("[red]Local path cannot be empty.[/]");
                return 1;
            }

            var fullLocalPath = Path.GetFullPath(localPath);

            if (!File.Exists(fullLocalPath))
            {
                AnsiConsole.MarkupLine($"[red]File not found:[/] {Markup.Escape(fullLocalPath)}");
                return 1;
            }

            if (string.IsNullOrWhiteSpace(drivePath))
            {
                AnsiConsole.MarkupLine("[red]Drive path cannot be empty. Use '/' for root or a path like 'projects/reports'.[/]");
                return 1;
            }

            var fileName = Path.GetFileName(fullLocalPath);
            var fileSize = new FileInfo(fullLocalPath).Length;

            AnsiConsole.MarkupLine($"[bold cyan]Uploading:[/] [white]{Markup.Escape(fileName)}[/] ({FormatBytes(fileSize)})");
            AnsiConsole.MarkupLine($"[bold cyan]Destination:[/] [white]{Markup.Escape(drivePath)}[/]\n");

            try
            {
                string folderId;

                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("Resolving Drive folder path...", async ctx =>
                    {
                        var normalised = drivePath.Trim('/').Equals("root", StringComparison.OrdinalIgnoreCase)
                                         || drivePath.Trim() == "/"
                            ? string.Empty
                            : drivePath.Trim('/');

                        folderId = string.IsNullOrEmpty(normalised)
                            ? "root"
                            : await _driveApi.EnsureFolderPathAsync(normalised, c);

                        ctx.Status($"Uploading {Markup.Escape(fileName)}...");

                        var uploadedId = await _driveApi.UploadFileAsync(fullLocalPath, folderId, c);

                        AnsiConsole.MarkupLine($"[green]✔ Upload complete![/]  Drive file ID: [grey]{Markup.Escape(uploadedId)}[/]");
                    });
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"\n[red]Upload failed: {Markup.Escape(ex.Message)}[/]");
                return 1;
            }

            return 0;
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
