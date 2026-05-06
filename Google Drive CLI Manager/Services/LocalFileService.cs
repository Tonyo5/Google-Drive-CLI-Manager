using Model;
using System.Text.Json;

namespace Services
{
    public class LocalFileService
    {
        private readonly string _downloadsDir;
        private readonly string _manifestPath;

        private Dictionary<string, ManifestEntry> _manifest = new();
        private readonly object _manifestLock = new();

        public string DownloadsDirectory => _downloadsDir;

        public LocalFileService(string? baseDir = null)
        {
            var root = baseDir ?? AppContext.BaseDirectory;
            _downloadsDir = Path.Combine(root, "Downloads");
            _manifestPath = Path.Combine(root, ".gdrive-manifest.json");

            Directory.CreateDirectory(_downloadsDir);
            LoadManifest();
        }

        public string GetLocalPath(string fileName)
        {
            var safe = string.Concat(fileName.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(_downloadsDir, safe);
        }

        public bool IsDownloaded(string driveId)
        {
            lock (_manifestLock)
            {
                if (!_manifest.TryGetValue(driveId, out var entry)) return false;
                return File.Exists(entry.LocalPath);
            }
        }

        public void RecordDownload(string driveId, string localPath)
        {
            lock (_manifestLock)
            {
                _manifest[driveId] = new ManifestEntry(driveId, localPath, DateTime.UtcNow);
            }
        }

        public void SaveManifest()
        {
            lock (_manifestLock)
            {
                var json = JsonSerializer.Serialize(_manifest, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_manifestPath, json);
            }
        }

        private void LoadManifest()
        {
            if (!File.Exists(_manifestPath)) return;

            try
            {
                var json = File.ReadAllText(_manifestPath);
                _manifest = JsonSerializer.Deserialize<Dictionary<string, ManifestEntry>>(json)
                            ?? new Dictionary<string, ManifestEntry>();
            }
            catch
            {
                _manifest = new Dictionary<string, ManifestEntry>();
            }
        }
    }
}
