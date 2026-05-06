using Google.Apis.Drive.v3;
using Model;

namespace Services
{
    public class ApiService : IApiService
    {
        private readonly DriveService _service;

        public const string FolderMimeType = "application/vnd.google-apps.folder";

        public ApiService(DriveService service)
        {
            _service = service;
        }
        public async Task<List<DriveFileInfo>> ListAllFilesAsync(CancellationToken c = default)
        {
            var results = new List<DriveFileInfo>();
            string? pageToken = null;

            do
            {
                var request = _service.Files.List();
                request.Q = "mimeType != 'application/vnd.google-apps.folder' and trashed = false";
                request.Fields = "nextPageToken, files(id, name, mimeType, size, modifiedTime)";
                request.PageSize = 100;
                request.PageToken = pageToken;

                var response = await ExecuteWithRetryAsync(() => request.ExecuteAsync(c));
                if (response.Files != null)
                {
                    results.AddRange(response.Files.Select(ToFileInfo));
                }

                pageToken = response.NextPageToken;
            } while (pageToken != null);

            return results;
        }

        public async Task<List<DriveFileInfo>> SearchFilesAsync(string query, CancellationToken c = default)
        {
            var results = new List<DriveFileInfo>();
            string? pageToken = null;

            var escapedQuery = query.Replace("\\", "\\\\").Replace("'", "\\'");

            do
            {
                var request = _service.Files.List();
                request.Q = $"name contains '{escapedQuery}' and trashed = false";
                request.Fields = "nextPageToken, files(id, name, mimeType, size, modifiedTime)";
                request.PageSize = 100;
                request.PageToken = pageToken;

                var response = await ExecuteWithRetryAsync(() => request.ExecuteAsync(c));

                if (response.Files != null)
                {
                    results.AddRange(response.Files.Select(ToFileInfo));
                }

                pageToken = response.NextPageToken;
            } while (pageToken != null);

            return results;
        }

        public async Task DownloadFileAsync(string fileId, string mimeType, Stream destination, CancellationToken c = default)
        {
            if (IsGoogleWorkspaceFile(mimeType))
            {
                var exportRequest = _service.Files.Export(fileId, "application/pdf");
                await exportRequest.DownloadAsync(destination, c);
            }
            else
            {
                var getRequest = _service.Files.Get(fileId);
                getRequest.Fields = "id";
                await getRequest.DownloadAsync(destination, c);
            }
        }

        public async Task<string> EnsureFolderPathAsync(string drivePath, CancellationToken c = default)
        {
            var parts = drivePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            string parentId = "root";

            foreach (var part in parts)
            {
                parentId = await GetOrCreateFolderAsync(part, parentId, c);
            }

            return parentId;
        }

        public async Task<string> UploadFileAsync(string localPath, string parentFolderId, CancellationToken c = default)
        {
            var fileName = Path.GetFileName(localPath);
            var mimeType = GetMimeType(fileName);

            var fileMetadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = fileName,
                Parents = new List<string> { parentFolderId }
            };

            await using var stream = File.OpenRead(localPath);

            var request = _service.Files.Create(fileMetadata, stream, mimeType);
            request.Fields = "id, name";

            var result = await ExecuteWithRetryAsync(async () =>
            {
                var progress = await request.UploadAsync(c);
                if (progress.Status != Google.Apis.Upload.UploadStatus.Completed)
                    throw new Exception($"Upload did not complete. Status: {progress.Status}. Error: {progress.Exception?.Message}");
                return request.ResponseBody;
            });

            return result.Id;
        }

        private async Task<string> GetOrCreateFolderAsync(string name, string parentId, CancellationToken c = default)
        {
            var escapedName = name.Replace("'", "\\'");
            var listRequest = _service.Files.List();
            listRequest.Q = $"name = '{escapedName}' and mimeType = '{FolderMimeType}' and '{parentId}' in parents and trashed = false";
            listRequest.Fields = "files(id)";
            listRequest.PageSize = 1;

            var response = await ExecuteWithRetryAsync(() => listRequest.ExecuteAsync(c));

            if (response.Files?.Count > 0)
                return response.Files[0].Id;
            
            var folderMetadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = name,
                MimeType = FolderMimeType,
                Parents = new List<string> { parentId }
            };

            var createRequest = _service.Files.Create(folderMetadata);
            createRequest.Fields = "id";
            var folder = await ExecuteWithRetryAsync(() => createRequest.ExecuteAsync(c));
            return folder.Id;
        }

        private static async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, int maxRetries = 3)
        {
            int delay = 1000;
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    return await action();
                }
                catch (Google.GoogleApiException ex) when (IsTransient(ex) && attempt < maxRetries)
                {
                    await Task.Delay(delay);
                    delay *= 2;
                }
            }

            return await action();
        }

        private static bool IsTransient(Google.GoogleApiException ex)
            => ex.HttpStatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                (int)ex.HttpStatusCode >= 500;

        private static DriveFileInfo ToFileInfo(Google.Apis.Drive.v3.Data.File file)
            => new(
                file.Id,
                file.Name,
                file.MimeType,
                file.Size,
                file.ModifiedTime?.ToString()
            );

        private static bool IsGoogleWorkspaceFile(string? mimeType)
            => !string.IsNullOrEmpty(mimeType)
                && mimeType.StartsWith("application/vnd.google-apps.")
                && mimeType != FolderMimeType;

        private static string GetMimeType(string fileName)
        {
            var ext = Path.GetExtension(fileName).ToLower();
            return ext switch
            {
                ".pdf" => "application/pdf",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".txt" => "text/plain",
                ".csv" => "text/csv",
                ".json" => "application/json",
                ".zip" => "application/zip",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                _ => "application/octet-stream"
            };
        }
    }
}
