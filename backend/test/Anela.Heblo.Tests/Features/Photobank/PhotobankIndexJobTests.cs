using Anela.Heblo.Application.Features.Photobank.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.Photobank;

public class PhotobankIndexJobTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly Mock<IPhotobankGraphService> _graphServiceMock;
    private readonly Mock<IRecurringJobStatusChecker> _statusCheckerMock;
    private readonly PhotobankIndexJob _job;

    public PhotobankIndexJobTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new ApplicationDbContext(options);

        _graphServiceMock = new Mock<IPhotobankGraphService>();
        _statusCheckerMock = new Mock<IRecurringJobStatusChecker>();

        // Default: job is enabled
        _statusCheckerMock
            .Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _job = new PhotobankIndexJob(
            _graphServiceMock.Object,
            _db,
            _statusCheckerMock.Object,
            NullLogger<PhotobankIndexJob>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_InsertsNewPhoto_WithRuleTagsApplied()
    {
        // Arrange
        var root = new PhotobankIndexRoot
        {
            SharePointPath = "/sites/test/photos",
            DriveId = "drive-1",
            RootItemId = "root-item-1",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        _db.PhotobankIndexRoots.Add(root);

        var tagRule = new TagRule
        {
            PathPattern = "Fotky/Produkty",
            TagName = "produkty",
            IsActive = true,
            SortOrder = 0,
        };
        _db.PhotobankTagRules.Add(tagRule);
        await _db.SaveChangesAsync();

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
        var photo = await _db.Photos.FirstOrDefaultAsync(p => p.SharePointFileId == "file-abc-123");
        photo.Should().NotBeNull();
        photo!.FileName.Should().Be("photo.jpg");
        photo.FolderPath.Should().Be("Fotky/Produkty");
        photo.SharePointWebUrl.Should().Be("https://sharepoint.example.com/photo.jpg");
        photo.FileSizeBytes.Should().Be(1024);

        var photoTags = await _db.PhotoTags.Where(pt => pt.PhotoId == photo.Id).ToListAsync();
        photoTags.Should().ContainSingle();
        photoTags[0].Source.Should().Be(PhotoTagSource.Rule);

        var tag = await _db.PhotobankTags.FirstOrDefaultAsync(t => t.Id == photoTags[0].TagId);
        tag.Should().NotBeNull();
        tag!.Name.Should().Be("produkty");
    }

    [Fact]
    public async Task ExecuteAsync_RemovesPhoto_WhenDeleted()
    {
        // Arrange
        var root = new PhotobankIndexRoot
        {
            SharePointPath = "/sites/test/photos",
            DriveId = "drive-1",
            RootItemId = "root-item-1",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        _db.PhotobankIndexRoots.Add(root);

        var existingPhoto = new Photo
        {
            SharePointFileId = "file-to-delete",
            FileName = "old.jpg",
            FolderPath = "Fotky",
            IndexedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
        };
        _db.Photos.Add(existingPhoto);
        await _db.SaveChangesAsync();

        var deletedItem = new GraphPhotoItem
        {
            ItemId = "file-to-delete",
            Name = string.Empty,
            FolderPath = string.Empty,
            DriveId = "drive-1",
            IsDeleted = true,
        };

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
        var photo = await _db.Photos.FirstOrDefaultAsync(p => p.SharePointFileId == "file-to-delete");
        photo.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_PersistsDeltaLink_AfterRun()
    {
        // Arrange
        const string expectedDeltaLink = "https://graph.microsoft.com/v1.0/drives/drive-1/items/root-1/delta?token=delta123";

        var root = new PhotobankIndexRoot
        {
            SharePointPath = "/sites/test/photos",
            DriveId = "drive-1",
            RootItemId = "root-1",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        _db.PhotobankIndexRoots.Add(root);
        await _db.SaveChangesAsync();

        _graphServiceMock
            .Setup(g => g.GetDeltaAsync("drive-1", "root-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphDeltaResult
            {
                Items = [],
                NewDeltaLink = expectedDeltaLink,
            });

        // Act
        await _job.ExecuteAsync();

        // Assert
        var updatedRoot = await _db.PhotobankIndexRoots.FirstAsync(r => r.Id == root.Id);
        updatedRoot.DeltaLink.Should().Be(expectedDeltaLink);
        updatedRoot.LastIndexedAt.Should().NotBeNull();
        updatedRoot.LastIndexedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ExecuteAsync_SkipsInactiveRoots()
    {
        // Arrange
        var inactiveRoot = new PhotobankIndexRoot
        {
            SharePointPath = "/sites/test/photos",
            DriveId = "drive-1",
            RootItemId = "root-item-1",
            IsActive = false,
            CreatedAt = DateTime.UtcNow,
        };
        _db.PhotobankIndexRoots.Add(inactiveRoot);
        await _db.SaveChangesAsync();

        // Act
        await _job.ExecuteAsync();

        // Assert — graph service should never be called for inactive roots
        _graphServiceMock.Verify(
            g => g.GetDeltaAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
