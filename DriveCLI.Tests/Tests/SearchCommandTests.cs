using FluentAssertions;
using Model;
using Services;
using GoogleDriveCli.Command;
using Moq;

namespace GDriveCLI.Tests;

public class SearchCommandTests : IDisposable
{
    private readonly Mock<IApiService> _driveApiMock;
    private readonly string _tempDir;
    private readonly LocalFileService _localFiles;
    private readonly SearchCommand _sut;

    public SearchCommandTests()
    {
        _driveApiMock = new Mock<IApiService>();
        _tempDir = Path.Combine(Path.GetTempPath(), $"gdrive-search-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _localFiles = new LocalFileService(_tempDir);
        _sut = new SearchCommand(_driveApiMock.Object, _localFiles);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsOne_WhenQueryIsEmpty()
    {
        var result = await _sut.ExecuteAsync(string.Empty);

        result.Should().Be(1, "empty query is an invalid input");
        _driveApiMock.Verify(x => x.SearchFilesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsOne_WhenQueryIsWhitespace()
    {
        var result = await _sut.ExecuteAsync("   ");

        result.Should().Be(1);
        _driveApiMock.Verify(x => x.SearchFilesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsZero_WhenNoResultsFound()
    {
        _driveApiMock
            .Setup(x => x.SearchFilesAsync("budget", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DriveFileInfo>());

        var result = await _sut.ExecuteAsync("budget");

        result.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsZero_WhenResultsFound()
    {
        var files = new List<DriveFileInfo>
        {
            new("id-1", "Q1 Budget.xlsx", "application/vnd.ms-excel", 2048, "2024-01-01"),
            new("id-2", "Q2 Budget.xlsx", "application/vnd.ms-excel", 3072, "2024-04-01"),
        };

        _driveApiMock
            .Setup(x => x.SearchFilesAsync("budget", It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        var result = await _sut.ExecuteAsync("budget");

        result.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_CallsSearchApiWithExactQuery()
    {
        _driveApiMock
            .Setup(x => x.SearchFilesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DriveFileInfo>());

        await _sut.ExecuteAsync("my important doc");

        _driveApiMock.Verify(
            x => x.SearchFilesAsync("my important doc", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsOne_WhenApiThrows()
    {
        _driveApiMock
            .Setup(x => x.SearchFilesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        var result = await _sut.ExecuteAsync("anything");

        result.Should().Be(1, "API failures should surface as exit code 1");
    }

    [Fact]
    public async Task ExecuteAsync_CorrectlyDetects_DownloadedVsNotDownloaded()
    {
        var downloadedPath = Path.Combine(_localFiles.DownloadsDirectory, "present.pdf");
        File.WriteAllText(downloadedPath, "data");
        _localFiles.RecordDownload("id-present", downloadedPath);

        var files = new List<DriveFileInfo>
        {
            new("id-present", "present.pdf", "application/pdf", 100, null),
            new("id-absent",  "absent.pdf",  "application/pdf", 200, null),
        };

        _driveApiMock
            .Setup(x => x.SearchFilesAsync("pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        var result = await _sut.ExecuteAsync("pdf");

        result.Should().Be(0);
        _localFiles.IsDownloaded("id-present").Should().BeTrue();
        _localFiles.IsDownloaded("id-absent").Should().BeFalse();
    }
}