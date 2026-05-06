using FluentAssertions;
using Spectre.Console;
using GoogleDriveCli.Command;
using Model;
using Moq;
using Services;

namespace Tests
{
    public class SyncCommandTests : IDisposable
    {
        private readonly Mock<IApiService> _driveApiMock;
        private readonly string _tempDir;
        private readonly LocalFileService _localFiles;
        private readonly SyncCommand _sut;

        public SyncCommandTests()
        {
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(TextWriter.Null)
            });

            _driveApiMock = new Mock<IApiService>();
            _tempDir = Path.Combine(Path.GetTempPath(), $"gdrive-sync-test-{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDir);
            _localFiles = new LocalFileService(_tempDir);
            _sut = new SyncCommand(_driveApiMock.Object, _localFiles);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        [Fact]
        public async Task ExecuteAsync_ReturnsZero_WhenDriveIsEmpty()
        {
            _driveApiMock
                .Setup(x => x.ListAllFilesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DriveFileInfo>());

            var result = await _sut.ExecuteAsync();

            result.Should().Be(0);
        }

        [Fact]
        public async Task ExecuteAsync_ReturnsOne_WhenListAllFilesThrows()
        {
            _driveApiMock
                .Setup(x => x.ListAllFilesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Network error"));

            var result = await _sut.ExecuteAsync();

            result.Should().Be(1);
        }

        [Fact]
        public async Task ExecuteAsync_DownloadsAllFiles_WhenNoneAreLocal()
        {
            var files = new List<DriveFileInfo>
        {
            new("id-1", "file1.txt", "text/plain", 100, null),
            new("id-2", "file2.txt", "text/plain", 200, null),
        };

            _driveApiMock
                .Setup(x => x.ListAllFilesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(files);

            _driveApiMock
                .Setup(x => x.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, Stream, CancellationToken>((_, _, stream, _) =>
                {
                    var data = "hello"u8.ToArray();
                    stream.Write(data);
                })
                .Returns(Task.CompletedTask);

            var result = await _sut.ExecuteAsync();

            result.Should().Be(0);

            _driveApiMock.Verify(
                x => x.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }

        [Fact]
        public async Task ExecuteAsync_SkipsFilesAlreadyOnDisk()
        {
            var existingPath = Path.Combine(_localFiles.DownloadsDirectory, "existing.txt");
            File.WriteAllText(existingPath, "already here");
            _localFiles.RecordDownload("id-existing", existingPath);

            var files = new List<DriveFileInfo>
        {
            new("id-existing", "existing.txt", "text/plain", 100, null),
            new("id-new",      "new.txt",      "text/plain", 200, null),
        };

            _driveApiMock
                .Setup(x => x.ListAllFilesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(files);

            _driveApiMock
                .Setup(x => x.DownloadFileAsync("id-new", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            await _sut.ExecuteAsync();

            _driveApiMock.Verify(
                x => x.DownloadFileAsync("id-existing", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
                Times.Never);

            _driveApiMock.Verify(
                x => x.DownloadFileAsync("id-new", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_ReturnsOne_WhenAnyDownloadFails()
        {
            var files = new List<DriveFileInfo>
        {
            new("id-ok",   "ok.txt",   "text/plain", 100, null),
            new("id-fail", "fail.txt", "text/plain", 200, null),
        };

            _driveApiMock
                .Setup(x => x.ListAllFilesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(files);

            _driveApiMock
                .Setup(x => x.DownloadFileAsync("id-ok", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _driveApiMock
                .Setup(x => x.DownloadFileAsync("id-fail", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Simulated download error"));

            var result = await _sut.ExecuteAsync();

            result.Should().Be(1);

            _driveApiMock.Verify(
                x => x.DownloadFileAsync("id-ok", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_ContinuesDownloadingOtherFiles_WhenOneFileFails()
        {
            var files = Enumerable.Range(1, 5)
                .Select(i => new DriveFileInfo($"id-{i}", $"file{i}.txt", "text/plain", 100, null))
                .ToList();

            _driveApiMock
                .Setup(x => x.ListAllFilesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(files);

            foreach (var file in files.Where(f => f.Id != "id-3"))
            {
                _driveApiMock
                    .Setup(x => x.DownloadFileAsync(file.Id, It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
            }

            _driveApiMock
                .Setup(x => x.DownloadFileAsync("id-3", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Server error"));

            await _sut.ExecuteAsync();

            _driveApiMock.Verify(
                x => x.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
                Times.Exactly(5));
        }

    }
}
