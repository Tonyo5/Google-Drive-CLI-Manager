namespace Model
{
    public record DriveFileInfo(
        string Id,
        string Name,
        string? MimeType,
        long? Size,
        string? ModifiedTime
    );

    public class SyncStatistic
    {
        public int _successCount;
        public int _failedCount;
        private int _skippedCount;
        private long _totalBytesSynced;

        public int SuccessCount => _successCount;
        public int FailedCount => _failedCount;
        public int SkippedCount => _skippedCount;
        public long TotalBytesSynced => _totalBytesSynced;
        public int TotalProcessed => _successCount + _failedCount + _skippedCount;

        public void RecordSuccess(long bytesSynced)
        {
            Interlocked.Increment(ref _successCount);
            Interlocked.Add(ref _totalBytesSynced, bytesSynced);
        }

        public void RecordFailure() => Interlocked.Increment(ref _failedCount);
        public void RecordSkipped() => Interlocked.Increment(ref _skippedCount);
    }
    public record ManifestEntry(string DriveId, string LocalPath, DateTime DownloadAt);
}
