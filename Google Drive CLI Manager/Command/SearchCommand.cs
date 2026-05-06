using Services;
using Spectre.Console;

namespace GoogleDriveCli.Command
{
    public class SearchCommand
    {
        private readonly IApiService _driveApi;
        private readonly LocalFileService _localFiles;

        public SearchCommand(IApiService driveApi, LocalFileService localFiles)
        {
            _driveApi = driveApi;
            _localFiles = localFiles;
        }

        public async Task<int> ExecuteAsync(string query, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                AnsiConsole.MarkupLine("[red]Search query cannot be empty.[/]");
                return 1;
            }

            AnsiConsole.MarkupLine($"[bold cyan]Searching Drive for:[/] [yellow]{Markup.Escape(query)}[/]\n");

            try
            {
                var results = await _driveApi.SearchFilesAsync(query, ct);

                if (results.Count == 0)
                {
                    AnsiConsole.MarkupLine("[yellow]No files found matching your query.[/]");
                    return 0;
                }

                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .Title($"[bold white]Search Results ({results.Count} found)[/]")
                    .AddColumn("[bold]Name[/]")
                    .AddColumn("[bold]Type[/]")
                    .AddColumn("[bold]Size[/]")
                    .AddColumn("[bold]Modified[/]")
                    .AddColumn("[bold]Status[/]");

                foreach (var file in results)
                {
                    var isDownloaded = _localFiles.IsDownloaded(file.Id);
                    var statusMarkup = isDownloaded
                        ? "[green]✔ Downloaded[/]"
                        : "[red]✘ Not Downloaded[/]";

                    var sizeText = file.Size.HasValue
                        ? FormatBytes(file.Size.Value)
                        : "[grey]N/A[/]";

                    var mimeShort = SimplifyMime(file.MimeType ?? "unknown");

                    table.AddRow(
                        Markup.Escape(file.Name),
                        mimeShort,
                        sizeText,
                        Markup.Escape(file.ModifiedTime ?? "-"),
                        statusMarkup
                    );
                }

                AnsiConsole.Write(table);

                var downloaded = results.Count(r => _localFiles.IsDownloaded(r.Id));
                var notDownloaded = results.Count - downloaded;

                AnsiConsole.MarkupLine($"\n[green]{downloaded} downloaded[/]  [red]{notDownloaded} not downloaded[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Search failed: {Markup.Escape(ex.Message)}[/]");
                return 1;
            }

            return 0;
        }

        private static string SimplifyMime(string mimeType) => mimeType switch
        {
            "application/vnd.google-apps.document" => "[blue]Google Doc[/]",
            "application/vnd.google-apps.spreadsheet" => "[green]Google Sheet[/]",
            "application/vnd.google-apps.presentation" => "[yellow]Google Slides[/]",
            "application/vnd.google-apps.folder" => "[cyan]Folder[/]",
            "application/pdf" => "[red]PDF[/]",
            "image/png" or "image/jpeg" or "image/gif" => "[magenta]Image[/]",
            _ when mimeType.StartsWith("text/") => "[grey]Text[/]",
            _ => Markup.Escape(mimeType)
        };

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}
