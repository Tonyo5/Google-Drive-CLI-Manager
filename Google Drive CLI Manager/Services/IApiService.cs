using Model;

namespace Services
{
    public interface IApiService
    {
        Task<List<DriveFileInfo>> ListAllFilesAsync(CancellationToken c = default);
        Task<List<DriveFileInfo>> SearchFilesAsync(string query, CancellationToken c = default);
        Task DownloadFileAsync(string fileId, string mimeType, Stream destination, CancellationToken c = default);
        Task<string> EnsureFolderPathAsync(string drivePath, CancellationToken c = default);
        Task<string> UploadFileAsync(string localPath, string parentFolderId, CancellationToken c = default);
    }
}
