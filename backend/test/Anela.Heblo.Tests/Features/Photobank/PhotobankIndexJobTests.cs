using Anela.Heblo.Application.Features.Photobank.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Photobank;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.Photobank;

public class PhotobankIndexJobTests
{
    private readonly Mock<IPhotobankRepository> _repoMock;
    private readonly Mock<IPhotobankGraphService> _graphServiceMock;
    private readonly Mock<IRecurringJobStatusChecker> _statusCheckerMock;
    private readonly PhotobankIndexJob _job;

    public PhotobankIndexJobTests()
    {
        _repoMock = new Mock<IPhotobankRepository>();
        _graphServiceMock = new Mock<IPhotobankGraphService>();
        _statusCheckerMock = new Mock<IRecurringJobStatusChecker>();

        _statusCheckerMock
            .Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _job = new PhotobankIndexJob(
            _graphServiceMock.Object,
            _repoMock.Object,
            _statusCheckerMock.Object,
            NullLogger<PhotobankIndexJob>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_InsertsNewPhoto_WithRuleTagsApplied()
    {
        // Arrange
        var root = new PhotobankIndexRoot
        {
            Id = 1,
            SharePointPath = "/sites/test/photos",
            DriveId = "drive-1",
            RootItemId = "root-item-1",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        var tagRule = new TagRule
        {
            PathPattern = "Fotky/Produkty",
            TagName = "produkty",
            IsActive = true,
            SortOrder = 0,
        };

        var photoItem = new GraphPhotoItem
        {
            ItemId = "file-abc-123",
            Name = "photo.jpg",
            FolderPath = "Fotky/Produkty",
            WebUrl = "https://sharepoint.example.com/photo.jpg",
            FileSizeBytes = 1024,
            LastModifiedAt = DateTime.UtcNow,
            DriveId = "drive-1",
            IsDeleted = false,
        };

        _repoMock
            .Setup(r => r.GetActiveRootsWithDriveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([root]);

        _repoMock
            .Setup(r => r.GetActiveTagRulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([tagRule]);

        _repoMock
            .Setup(r => r.GetPhotoBySharePointFileIdAsync("file-abc-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Photo?)null);

        Photo? capturedPhoto = null;
        _repoMock
            .Setup(r => r.AddPhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()))
            .Callback<Photo, CancellationToken>((p, _) => capturedPhoto = p)
            .Returns(Task.CompletedTask);

        _repoMock
            .Setup(r => r.GetPhotoTagsByPhotoAndSourceAsync(It.IsAny<int>(), PhotoTagSource.Rule, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _repoMock
            .Setup(r => r.RemovePhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tag = new Tag { Id = 42, Name = "produkty" };
        _repoMock
            .Setup(r => r.GetOrCreateTagAsync("produkty", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tag);

        PhotoTag? capturedPhotoTag = null;
        _repoMock
            .Setup(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()))
            .Callback<PhotoTag, CancellationToken>((pt, _) => capturedPhotoTag = pt)
            .Returns(Task.CompletedTask);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _graphServiceMock
            .Setup(g => g.GetDeltaAsync("drive-1", "root-item-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphDeltaResult
            {
                Items = [photoItem],
                NewDeltaLink = "https://graph.microsoft.com/v1.0/drives/drive-1/items/root-item-1/delta?token=abc",
            });

        // Act
        await _job.ExecuteAsync();

        // Assert
        capturedPhoto.Should().NotBeNull();
        capturedPhoto!.SharePointFileId.Should().Be("file-abc-123");
        capturedPhoto.FileName.Should().Be("photo.jpg");
        capturedPhoto.FolderPath.Should().Be("Fotky/Produkty");
        capturedPhoto.SharePointWebUrl.Should().Be("https://sharepoint.example.com/photo.jpg");
        capturedPhoto.FileSizeBytes.Should().Be(1024);

        capturedPhotoTag.Should().NotBeNull();
        capturedPhotoTag!.TagId.Should().Be(42);
        capturedPhotoTag.Source.Should().Be(PhotoTagSource.Rule);

        _repoMock.Verify(r => r.AddPhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()), Times.Once);
        _repoMock.Verify(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_RemovesPhoto_WhenDeleted()
    {
        // Arrange
        var root = new PhotobankIndexRoot
        {
            Id = 1,
            SharePointPath = "/sites/test/photos",
            DriveId = "drive-1",
            RootItemId = "root-item-1",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        var existingPhoto = new Photo
        {
            Id = 10,
            SharePointFileId = "file-to-delete",
            FileName = "old.jpg",
            FolderPath = "Fotky",
            IndexedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
        };

        var deletedItem = new GraphPhotoItem
        {
            ItemId = "file-to-delete",
            Name = string.Empty,
            FolderPath = string.Empty,
            DriveId = "drive-1",
            IsDeleted = true,
        };

        _repoMock
            .Setup(r => r.GetActiveRootsWithDriveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([root]);

        _repoMock
            .Setup(r => r.GetPhotoBySharePointFileIdAsync("file-to-delete", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingPhoto);

        _repoMock
            .Setup(r => r.RemovePhotoAsync(existingPhoto, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _graphServiceMock
            .Setup(g => g.GetDeltaAsync("drive-1", "root-item-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphDeltaResult
            {
                Items = [deletedItem],
                NewDeltaLink = "https://graph.microsoft.com/v1.0/drives/drive-1/delta?token=xyz",
            });

        // Act
        await _job.ExecuteAsync();

        // Assert
        _repoMock.Verify(r => r.RemovePhotoAsync(existingPhoto, It.IsAny<CancellationToken>()), Times.Once);
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_PersistsDeltaLink_AfterRun()
    {
        // Arrange
        const string expectedDeltaLink = "https://graph.microsoft.com/v1.0/drives/drive-1/items/root-1/delta?token=delta123";

        var root = new PhotobankIndexRoot
        {
            Id = 1,
            SharePointPath = "/sites/test/photos",
            DriveId = "drive-1",
            RootItemId = "root-1",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        _repoMock
            .Setup(r => r.GetActiveRootsWithDriveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([root]);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _graphServiceMock
            .Setup(g => g.GetDeltaAsync("drive-1", "root-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphDeltaResult
            {
                Items = [],
                NewDeltaLink = expectedDeltaLink,
            });

        // Act
        await _job.ExecuteAsync();

        // Assert — root object is mutated in-place (tracked entity pattern)
        root.DeltaLink.Should().Be(expectedDeltaLink);
        root.LastIndexedAt.Should().NotBeNull();
        root.LastIndexedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsInactiveRoots()
    {
        // Arrange
        var activeRoots = new List<PhotobankIndexRoot>(); // GetActiveRootsWithDriveAsync already filters

        _repoMock
            .Setup(r => r.GetActiveRootsWithDriveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeRoots);

        // Act
        await _job.ExecuteAsync();

        // Assert — graph service should never be called when no active roots
        _graphServiceMock.Verify(
            g => g.GetDeltaAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
