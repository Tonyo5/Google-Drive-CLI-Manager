using System.CommandLine;
using AuthService;
using GoogleDriveCli.Command;
using Services;
using Spectre.Console;

AnsiConsole.MarkupLine("[cyan1]Google Drive CLI[/]\n");
AnsiConsole.MarkupLine("[grey]Google Drive Manager [/]\n");

var credentialsOption = new Option<string>(
    aliases: ["--credentials", "-c"],
    description: "Path to your client_secret.json (default: ./client_secret.json)",
    getDefaultValue: () => "client_secret.json");

var rootCommand = new RootCommand("Google Drive CLI Manager – authenticate, sync, search, and upload.")
{
    credentialsOption
};

var syncCommand = new Command("sync", "Download all Drive files to the local Downloads directory in parallel.");

syncCommand.SetHandler(async (string credPath) =>
{
    var drive = await BuildDriveServiceAsync(credPath);
    var handler = new SyncCommand(new ApiService(drive), new LocalFileService());
    Environment.Exit(await handler.ExecuteAsync());
}, credentialsOption);

var queryArgument = new Argument<string>("query", "Search term (matched against file/folder names).");
var searchCommand = new Command("search", "Search for files or folders in Google Drive.") { queryArgument };

searchCommand.SetHandler(async (string credPath, string query) =>
{
    var drive = await BuildDriveServiceAsync(credPath);
    var handler = new SearchCommand(new ApiService(drive), new LocalFileService());
    Environment.Exit(await handler.ExecuteAsync(query));
}, credentialsOption, queryArgument);

var localPathArg = new Argument<string>("local_path", "Path to the local file to upload.");
var drivePathArg = new Argument<string>("drive_path", "Destination folder path in Drive (e.g. 'projects/reports'). Use '/' for root.");
var uploadCommand = new Command("upload", "Upload a local file to a specific Google Drive folder path.")
{
    localPathArg,
    drivePathArg
};

uploadCommand.SetHandler(async (string credPath, string localPath, string drivePath) =>
{
    var drive = await BuildDriveServiceAsync(credPath);
    var handler = new UploadCommand(new ApiService(drive));
    Environment.Exit(await handler.ExecuteAsync(localPath, drivePath));
}, credentialsOption, localPathArg, drivePathArg);

rootCommand.AddCommand(syncCommand);
rootCommand.AddCommand(searchCommand);
rootCommand.AddCommand(uploadCommand);

return await rootCommand.InvokeAsync(args);

static async Task<Google.Apis.Drive.v3.DriveService> BuildDriveServiceAsync(string credPath)
{
    try
    {
        AnsiConsole.MarkupLine("[grey]Authenticating with Google...[/]");
        var service = await GoogleAuthService.AuthenticateAsync(credPath);
        AnsiConsole.MarkupLine("[green]✔ Authenticated.[/]\n");
        return service;
    }
    catch (FileNotFoundException ex)
    {
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
        Environment.Exit(1);
        return null!;
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Authentication error: {Markup.Escape(ex.Message)}[/]");
        Environment.Exit(1);
        return null!;
    }
}