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
    private readonly Mock<IPhotobankRootRepository> _rootRepoMock;
    private readonly Mock<IPhotobankTagRuleRepository> _tagRuleRepoMock;
    private readonly Mock<IPhotobankPhotoRepository> _photoRepoMock;
    private readonly Mock<IPhotobankTagRepository> _tagRepoMock;
    private readonly Mock<IPhotobankPhotoTagRepository> _photoTagRepoMock;
    private readonly Mock<IPhotobankGraphService> _graphServiceMock;
    private readonly Mock<IRecurringJobStatusChecker> _statusCheckerMock;
    private readonly PhotobankIndexJob _job;

    public PhotobankIndexJobTests()
    {
        _rootRepoMock = new Mock<IPhotobankRootRepository>();
        _tagRuleRepoMock = new Mock<IPhotobankTagRuleRepository>();
        _photoRepoMock = new Mock<IPhotobankPhotoRepository>();
        _tagRepoMock = new Mock<IPhotobankTagRepository>();
        _photoTagRepoMock = new Mock<IPhotobankPhotoTagRepository>();
        _graphServiceMock = new Mock<IPhotobankGraphService>();
        _statusCheckerMock = new Mock<IRecurringJobStatusChecker>();

        _statusCheckerMock
            .Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), true))
            .ReturnsAsync(true);

        _job = new PhotobankIndexJob(
            _graphServiceMock.Object,
            _rootRepoMock.Object,
            _tagRuleRepoMock.Object,
            _photoRepoMock.Object,
            _tagRepoMock.Object,
            _photoTagRepoMock.Object,
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

        _rootRepoMock
            .Setup(r => r.GetActiveRootsWithDriveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([root]);

        _tagRuleRepoMock
            .Setup(r => r.GetActiveTagRulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([tagRule]);

        _photoRepoMock
            .Setup(r => r.GetPhotoBySharePointFileIdAsync("file-abc-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Photo?)null);

        Photo? capturedPhoto = null;
        _photoRepoMock
            .Setup(r => r.AddPhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()))
            .Callback<Photo, CancellationToken>((p, _) => capturedPhoto = p)
            .Returns(Task.CompletedTask);

        _photoTagRepoMock
            .Setup(r => r.GetPhotoTagsByPhotoAndSourceAsync(It.IsAny<int>(), PhotoTagSource.Rule, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _photoTagRepoMock
            .Setup(r => r.RemovePhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _tagRepoMock
            .Setup(r => r.GetOrCreateTagsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int> { ["produkty"] = 42 });

        _photoTagRepoMock
            .Setup(r => r.PhotoTagExistsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        PhotoTag? capturedPhotoTag = null;
        _photoTagRepoMock
            .Setup(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()))
            .Callback<PhotoTag, CancellationToken>((pt, _) => capturedPhotoTag = pt)
            .Returns(Task.CompletedTask);

        _photoRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _photoTagRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _rootRepoMock
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

        _photoRepoMock.Verify(r => r.AddPhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()), Times.Once);
        _photoTagRepoMock.Verify(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertPhotoBatch_GraphItemLastModifiedAtHasUnspecifiedKind_InMemoryPhotoModifiedAtIsStampedUtc()
    {
        // Arrange — simulate System.Text.Json handing back a DateTime with Kind=Unspecified for
        // the Graph delta item's lastModifiedDateTime. This test only asserts the in-memory
        // application-layer contract on the captured `Photo` object (the assigned Kind is always
        // Utc, regardless of the source's Kind) — it does NOT go through ApplicationDbContext.
        // ApplicationDbContext.OnModelCreating installs a global DateTime value converter that
        // unconditionally re-stamps every DateTime/DateTime? property to Kind=Unspecified right
        // before every write, so this in-memory Kind has no effect on what is actually persisted
        // and this test cannot and does not validate/reproduce the recurring
        // "Cannot write DateTime with Kind=Unspecified to ... 'timestamp with time zone'"
        // exception or its fix — see PhotobankSchemaHealthCheck for the schema-drift diagnosis
        // that actually determines whether that exception still occurs in a given environment.
        var unspecifiedInstant = new DateTime(2026, 7, 27, 1, 28, 0, DateTimeKind.Unspecified);

        var root = new PhotobankIndexRoot
        {
            Id = 1,
            SharePointPath = "/sites/test/photos",
            DriveId = "drive-1",
            RootItemId = "root-item-1",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        var photoItem = new GraphPhotoItem
        {
            ItemId = "file-kind-test",
            Name = "photo.jpg",
            FolderPath = "Fotky/Produkty",
            WebUrl = "https://sharepoint.example.com/photo.jpg",
            FileSizeBytes = 1024,
            LastModifiedAt = unspecifiedInstant,
            DriveId = "drive-1",
            IsDeleted = false,
        };

        _rootRepoMock
            .Setup(r => r.GetActiveRootsWithDriveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([root]);

        _tagRuleRepoMock
            .Setup(r => r.GetActiveTagRulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TagRule>());

        _photoRepoMock
            .Setup(r => r.GetPhotoBySharePointFileIdAsync("file-kind-test", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Photo?)null);

        Photo? capturedPhoto = null;
        _photoRepoMock
            .Setup(r => r.AddPhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()))
            .Callback<Photo, CancellationToken>((p, _) => capturedPhoto = p)
            .Returns(Task.CompletedTask);

        _photoTagRepoMock
            .Setup(r => r.GetPhotoTagsByPhotoAndSourceAsync(It.IsAny<int>(), PhotoTagSource.Rule, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _photoTagRepoMock
            .Setup(r => r.RemovePhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _photoRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _photoTagRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _rootRepoMock
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
        capturedPhoto!.ModifiedAt.Kind.Should().Be(DateTimeKind.Utc);
        capturedPhoto.ModifiedAt.Should().Be(DateTime.SpecifyKind(unspecifiedInstant, DateTimeKind.Utc));
    }

    [Fact]
    public async Task UpsertPhoto_WhenTagAlreadyExists_SkipsInsert()
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

        _rootRepoMock
            .Setup(r => r.GetActiveRootsWithDriveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([root]);

        _tagRuleRepoMock
            .Setup(r => r.GetActiveTagRulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([tagRule]);

        _photoRepoMock
            .Setup(r => r.GetPhotoBySharePointFileIdAsync("file-abc-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Photo?)null);

        _photoRepoMock
            .Setup(r => r.AddPhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _photoTagRepoMock
            .Setup(r => r.GetPhotoTagsByPhotoAndSourceAsync(It.IsAny<int>(), PhotoTagSource.Rule, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _photoTagRepoMock
            .Setup(r => r.RemovePhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _tagRepoMock
            .Setup(r => r.GetOrCreateTagsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int> { ["produkty"] = 42 });

        _photoTagRepoMock
            .Setup(r => r.PhotoTagExistsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _photoRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _photoTagRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _rootRepoMock
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
        _photoTagRepoMock.Verify(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()), Times.Never);
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

        _rootRepoMock
            .Setup(r => r.GetActiveRootsWithDriveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([root]);

        _photoRepoMock
            .Setup(r => r.GetPhotoBySharePointFileIdAsync("file-to-delete", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingPhoto);

        _photoRepoMock
            .Setup(r => r.RemovePhotoAsync(existingPhoto, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _rootRepoMock
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
        _photoRepoMock.Verify(r => r.RemovePhotoAsync(existingPhoto, It.IsAny<CancellationToken>()), Times.Once);
        _rootRepoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
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

        _rootRepoMock
            .Setup(r => r.GetActiveRootsWithDriveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([root]);

        _rootRepoMock
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
        _rootRepoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsInactiveRoots()
    {
        // Arrange
        var activeRoots = new List<PhotobankIndexRoot>(); // GetActiveRootsWithDriveAsync already filters

        _rootRepoMock
            .Setup(r => r.GetActiveRootsWithDriveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeRoots);

        // Act
        await _job.ExecuteAsync();

        // Assert — graph service should never be called when no active roots
        _graphServiceMock.Verify(
            g => g.GetDeltaAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpsertPhotoBatch_MultipleItemsInSameBatch_FlushesSaveChangesExactlyThreeTimesTotal()
    {
        // Arrange — 2 non-deleted items, both fit in a single batch (BatchSize = 200).
        // Expected SaveChangesAsync calls: 1 (Phase A, photo repo) + 1 (Phase B, photo-tag repo)
        // + 1 (root bookkeeping, root repo) = 3, one per repository.
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

        var item1 = new GraphPhotoItem
        {
            ItemId = "file-1",
            Name = "one.jpg",
            FolderPath = "Fotky/Produkty",
            WebUrl = "https://sharepoint.example.com/one.jpg",
            FileSizeBytes = 100,
            LastModifiedAt = DateTime.UtcNow,
            DriveId = "drive-1",
            IsDeleted = false,
        };
        var item2 = new GraphPhotoItem
        {
            ItemId = "file-2",
            Name = "two.jpg",
            FolderPath = "Fotky/Produkty",
            WebUrl = "https://sharepoint.example.com/two.jpg",
            FileSizeBytes = 200,
            LastModifiedAt = DateTime.UtcNow,
            DriveId = "drive-1",
            IsDeleted = false,
        };

        _rootRepoMock
            .Setup(r => r.GetActiveRootsWithDriveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([root]);

        _tagRuleRepoMock
            .Setup(r => r.GetActiveTagRulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([tagRule]);

        _photoRepoMock
            .Setup(r => r.GetPhotoBySharePointFileIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Photo?)null);

        _photoRepoMock
            .Setup(r => r.AddPhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _tagRepoMock
            .Setup(r => r.GetOrCreateTagsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int> { ["produkty"] = 42 });

        _photoTagRepoMock
            .Setup(r => r.GetPhotoTagsByPhotoAndSourceAsync(It.IsAny<int>(), PhotoTagSource.Rule, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _photoTagRepoMock
            .Setup(r => r.RemovePhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _photoTagRepoMock
            .Setup(r => r.PhotoTagExistsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _photoTagRepoMock
            .Setup(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _photoRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _photoTagRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _rootRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _graphServiceMock
            .Setup(g => g.GetDeltaAsync("drive-1", "root-item-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphDeltaResult
            {
                Items = [item1, item2],
                NewDeltaLink = "https://graph.microsoft.com/v1.0/drives/drive-1/items/root-item-1/delta?token=abc",
            });

        // Act
        await _job.ExecuteAsync();

        // Assert
        _photoRepoMock.Verify(r => r.AddPhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _photoTagRepoMock.Verify(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _tagRepoMock.Verify(r => r.GetOrCreateTagsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        _photoRepoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _photoTagRepoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _rootRepoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertPhotoBatch_DeltaLargerThanBatchSize_FlushesCeilNOverBatchSizeTimes()
    {
        // Arrange — 201 non-deleted items, no matching tag rules (empty active rule list),
        // so BatchSize = 200 forces 2 batches: [200 items] + [1 item].
        // Expected SaveChangesAsync calls: photo repo x2 (Phase A per batch), photo-tag repo x2
        // (Phase B per batch), root repo x1 (bookkeeping) = 5 total.
        var root = new PhotobankIndexRoot
        {
            Id = 1,
            SharePointPath = "/sites/test/photos",
            DriveId = "drive-1",
            RootItemId = "root-item-1",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        const int itemCount = 201;
        var items = Enumerable.Range(0, itemCount)
            .Select(i => new GraphPhotoItem
            {
                ItemId = $"file-{i:D4}",
                Name = $"photo-{i:D4}.jpg",
                FolderPath = "Fotky/Ostatni",
                WebUrl = $"https://sharepoint.example.com/photo-{i:D4}.jpg",
                FileSizeBytes = 100,
                LastModifiedAt = DateTime.UtcNow,
                DriveId = "drive-1",
                IsDeleted = false,
            })
            .ToList();

        _rootRepoMock
            .Setup(r => r.GetActiveRootsWithDriveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([root]);

        _tagRuleRepoMock
            .Setup(r => r.GetActiveTagRulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TagRule>());

        _photoRepoMock
            .Setup(r => r.GetPhotoBySharePointFileIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Photo?)null);

        _photoRepoMock
            .Setup(r => r.AddPhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _photoTagRepoMock
            .Setup(r => r.GetPhotoTagsByPhotoAndSourceAsync(It.IsAny<int>(), PhotoTagSource.Rule, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _photoTagRepoMock
            .Setup(r => r.RemovePhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _photoRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _photoTagRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _rootRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _graphServiceMock
            .Setup(g => g.GetDeltaAsync("drive-1", "root-item-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphDeltaResult
            {
                Items = items,
                NewDeltaLink = "https://graph.microsoft.com/v1.0/drives/drive-1/items/root-item-1/delta?token=abc",
            });

        // Act
        await _job.ExecuteAsync();

        // Assert
        _photoRepoMock.Verify(r => r.AddPhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()), Times.Exactly(itemCount));
        _tagRepoMock.Verify(r => r.GetOrCreateTagsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        _photoRepoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        _photoTagRepoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        _rootRepoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertPhotoBatch_DuplicateSharePointFileIdWithinOneBatch_ResultsInSinglePhotoRow()
    {
        // Arrange — the same SharePointFileId appears twice as a non-deleted item in one
        // delta/batch (e.g. modified twice since last sync). The batch-local cache must
        // make both occurrences resolve to, and mutate, the same Photo instance — not
        // create two rows.
        var root = new PhotobankIndexRoot
        {
            Id = 1,
            SharePointPath = "/sites/test/photos",
            DriveId = "drive-1",
            RootItemId = "root-item-1",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        var item1 = new GraphPhotoItem
        {
            ItemId = "file-dup",
            Name = "first-name.jpg",
            FolderPath = "Fotky/A",
            WebUrl = "https://sharepoint.example.com/first-name.jpg",
            FileSizeBytes = 111,
            LastModifiedAt = DateTime.UtcNow,
            DriveId = "drive-1",
            IsDeleted = false,
        };
        var item2 = new GraphPhotoItem
        {
            ItemId = "file-dup",
            Name = "second-name.jpg",
            FolderPath = "Fotky/B",
            WebUrl = "https://sharepoint.example.com/second-name.jpg",
            FileSizeBytes = 222,
            LastModifiedAt = DateTime.UtcNow,
            DriveId = "drive-1",
            IsDeleted = false,
        };

        _rootRepoMock
            .Setup(r => r.GetActiveRootsWithDriveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([root]);

        _tagRuleRepoMock
            .Setup(r => r.GetActiveTagRulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TagRule>());

        _photoRepoMock
            .Setup(r => r.GetPhotoBySharePointFileIdAsync("file-dup", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Photo?)null);

        Photo? capturedPhoto = null;
        _photoRepoMock
            .Setup(r => r.AddPhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()))
            .Callback<Photo, CancellationToken>((p, _) => capturedPhoto = p)
            .Returns(Task.CompletedTask);

        _photoTagRepoMock
            .Setup(r => r.GetPhotoTagsByPhotoAndSourceAsync(It.IsAny<int>(), PhotoTagSource.Rule, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _photoTagRepoMock
            .Setup(r => r.RemovePhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _photoRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _photoTagRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _rootRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _graphServiceMock
            .Setup(g => g.GetDeltaAsync("drive-1", "root-item-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphDeltaResult
            {
                Items = [item1, item2],
                NewDeltaLink = "https://graph.microsoft.com/v1.0/drives/drive-1/items/root-item-1/delta?token=abc",
            });

        // Act
        await _job.ExecuteAsync();

        // Assert — a single Photo row is created, and its final field values reflect the
        // second (later) item in delta order, proving both occurrences shared one instance.
        _photoRepoMock.Verify(r => r.AddPhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()), Times.Once);
        _photoRepoMock.Verify(r => r.GetPhotoBySharePointFileIdAsync("file-dup", It.IsAny<CancellationToken>()), Times.Once);

        capturedPhoto.Should().NotBeNull();
        capturedPhoto!.FileName.Should().Be("second-name.jpg");
        capturedPhoto.FolderPath.Should().Be("Fotky/B");
        capturedPhoto.FileSizeBytes.Should().Be(222);
    }

    [Fact]
    public async Task UpsertPhotoBatch_UpsertThenDeleteSameItemInSameDelta_PhotoEndsUpRemoved()
    {
        // Arrange — one delta containing, in order: a non-deleted item for SharePointFileId
        // "file-x", then a deleted item for the same "file-x". Per the deletion-flush rule,
        // the pending upsert batch (containing the first item) must be flushed before the
        // deletion is processed, so the deletion's lookup finds and removes the just-created
        // photo — exactly as today's per-item-flush behavior guarantees.
        var root = new PhotobankIndexRoot
        {
            Id = 1,
            SharePointPath = "/sites/test/photos",
            DriveId = "drive-1",
            RootItemId = "root-item-1",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        var upsertItem = new GraphPhotoItem
        {
            ItemId = "file-x",
            Name = "renamed.jpg",
            FolderPath = "Fotky/Produkty",
            WebUrl = "https://sharepoint.example.com/renamed.jpg",
            FileSizeBytes = 333,
            LastModifiedAt = DateTime.UtcNow,
            DriveId = "drive-1",
            IsDeleted = false,
        };
        var deleteItem = new GraphPhotoItem
        {
            ItemId = "file-x",
            Name = string.Empty,
            FolderPath = string.Empty,
            DriveId = "drive-1",
            IsDeleted = true,
        };

        _rootRepoMock
            .Setup(r => r.GetActiveRootsWithDriveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([root]);

        _tagRuleRepoMock
            .Setup(r => r.GetActiveTagRulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TagRule>());

        Photo? capturedPhoto = null;
        _photoRepoMock
            .Setup(r => r.AddPhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()))
            .Callback<Photo, CancellationToken>((p, _) => capturedPhoto = p)
            .Returns(Task.CompletedTask);

        // Lazily evaluated: returns null on the first (Phase A, upsert) lookup — before
        // AddPhotoAsync has run — and returns the just-created Photo on the second
        // (deletion) lookup, simulating that the pending batch's flush has committed it.
        _photoRepoMock
            .Setup(r => r.GetPhotoBySharePointFileIdAsync("file-x", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => capturedPhoto);

        _photoTagRepoMock
            .Setup(r => r.GetPhotoTagsByPhotoAndSourceAsync(It.IsAny<int>(), PhotoTagSource.Rule, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _photoTagRepoMock
            .Setup(r => r.RemovePhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _photoRepoMock
            .Setup(r => r.RemovePhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _photoRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _photoTagRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _rootRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _graphServiceMock
            .Setup(g => g.GetDeltaAsync("drive-1", "root-item-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphDeltaResult
            {
                Items = [upsertItem, deleteItem],
                NewDeltaLink = "https://graph.microsoft.com/v1.0/drives/drive-1/items/root-item-1/delta?token=abc",
            });

        // Act
        await _job.ExecuteAsync();

        // Assert — the photo was created, then removed: no orphaned row.
        capturedPhoto.Should().NotBeNull();
        _photoRepoMock.Verify(r => r.AddPhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()), Times.Once);
        _photoRepoMock.Verify(r => r.RemovePhotoAsync(capturedPhoto!, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertPhotoBatch_DuplicateSharePointFileIdMatchingDifferentTagRules_OnlyLastItemsTagsSurvive()
    {
        // Arrange — the same SharePointFileId appears twice in one batch (both non-deleted),
        // with different FolderPath/Name that match different tag rules. Phase B must dedupe
        // by the shared Photo instance and apply only the LAST item's matched tags — not both,
        // and not just the first — otherwise the first occurrence's tag would incorrectly
        // survive because the per-item DB-reading checks can't see this batch's own unflushed
        // changes for the same photo.
        var root = new PhotobankIndexRoot
        {
            Id = 1,
            SharePointPath = "/sites/test/photos",
            DriveId = "drive-1",
            RootItemId = "root-item-1",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        var tagRuleA = new TagRule
        {
            PathPattern = "Fotky/A",
            TagName = "tag-a",
            IsActive = true,
            SortOrder = 0,
        };
        var tagRuleB = new TagRule
        {
            PathPattern = "Fotky/B",
            TagName = "tag-b",
            IsActive = true,
            SortOrder = 1,
        };

        var item1 = new GraphPhotoItem
        {
            ItemId = "file-dup",
            Name = "first.jpg",
            FolderPath = "Fotky/A",
            WebUrl = "https://sharepoint.example.com/first.jpg",
            FileSizeBytes = 111,
            LastModifiedAt = DateTime.UtcNow,
            DriveId = "drive-1",
            IsDeleted = false,
        };
        var item2 = new GraphPhotoItem
        {
            ItemId = "file-dup",
            Name = "second.jpg",
            FolderPath = "Fotky/B",
            WebUrl = "https://sharepoint.example.com/second.jpg",
            FileSizeBytes = 222,
            LastModifiedAt = DateTime.UtcNow,
            DriveId = "drive-1",
            IsDeleted = false,
        };

        _rootRepoMock
            .Setup(r => r.GetActiveRootsWithDriveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([root]);

        _tagRuleRepoMock
            .Setup(r => r.GetActiveTagRulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([tagRuleA, tagRuleB]);

        _photoRepoMock
            .Setup(r => r.GetPhotoBySharePointFileIdAsync("file-dup", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Photo?)null);

        _photoRepoMock
            .Setup(r => r.AddPhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _photoTagRepoMock
            .Setup(r => r.GetPhotoTagsByPhotoAndSourceAsync(It.IsAny<int>(), PhotoTagSource.Rule, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _photoTagRepoMock
            .Setup(r => r.RemovePhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _tagRepoMock
            .Setup(r => r.GetOrCreateTagsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int> { ["tag-a"] = 1, ["tag-b"] = 2 });

        _photoTagRepoMock
            .Setup(r => r.PhotoTagExistsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var addedPhotoTags = new List<PhotoTag>();
        _photoTagRepoMock
            .Setup(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()))
            .Callback<PhotoTag, CancellationToken>((pt, _) => addedPhotoTags.Add(pt))
            .Returns(Task.CompletedTask);

        _photoRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _photoTagRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _rootRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _graphServiceMock
            .Setup(g => g.GetDeltaAsync("drive-1", "root-item-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphDeltaResult
            {
                Items = [item1, item2],
                NewDeltaLink = "https://graph.microsoft.com/v1.0/drives/drive-1/items/root-item-1/delta?token=abc",
            });

        // Act
        await _job.ExecuteAsync();

        // Assert — the photo's tag-application path ran exactly once (per distinct photo, not
        // per raw item), and only the last item's ("Fotky/B" -> "tag-b") match was applied.
        _photoTagRepoMock.Verify(r => r.GetPhotoTagsByPhotoAndSourceAsync(It.IsAny<int>(), PhotoTagSource.Rule, It.IsAny<CancellationToken>()), Times.Once);
        _photoTagRepoMock.Verify(r => r.RemovePhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()), Times.Once);
        _photoTagRepoMock.Verify(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()), Times.Once);

        addedPhotoTags.Should().ContainSingle();
        addedPhotoTags[0].TagId.Should().Be(2); // "tag-b", from the last item ("Fotky/B")
    }

    [Fact]
    public async Task UpsertPhotoBatch_DuplicateSharePointFileIdMatchingSameTagRule_AppliesTagOnceWithNoDuplicateKeyRisk()
    {
        // Arrange — the same SharePointFileId appears twice in one batch (both non-deleted),
        // both occurrences matching the SAME tag rule. Before the fix, Phase B's per-item loop
        // would attempt AddPhotoTagAsync for the identical (PhotoId, TagId) pair twice — since
        // PhotoTagExistsAsync's real-DB check can't see the first occurrence's unflushed insert
        // — which would violate PhotoTag's composite primary key on SaveChangesAsync. After the
        // fix, the photo's tag set is computed and applied exactly once per batch.
        var root = new PhotobankIndexRoot
        {
            Id = 1,
            SharePointPath = "/sites/test/photos",
            DriveId = "drive-1",
            RootItemId = "root-item-1",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        var tagRuleCommon = new TagRule
        {
            PathPattern = "Fotky/Common",
            TagName = "common",
            IsActive = true,
            SortOrder = 0,
        };

        var item1 = new GraphPhotoItem
        {
            ItemId = "file-dup",
            Name = "first.jpg",
            FolderPath = "Fotky/Common",
            WebUrl = "https://sharepoint.example.com/first.jpg",
            FileSizeBytes = 111,
            LastModifiedAt = DateTime.UtcNow,
            DriveId = "drive-1",
            IsDeleted = false,
        };
        var item2 = new GraphPhotoItem
        {
            ItemId = "file-dup",
            Name = "second.jpg",
            FolderPath = "Fotky/Common",
            WebUrl = "https://sharepoint.example.com/second.jpg",
            FileSizeBytes = 222,
            LastModifiedAt = DateTime.UtcNow,
            DriveId = "drive-1",
            IsDeleted = false,
        };

        _rootRepoMock
            .Setup(r => r.GetActiveRootsWithDriveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([root]);

        _tagRuleRepoMock
            .Setup(r => r.GetActiveTagRulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([tagRuleCommon]);

        _photoRepoMock
            .Setup(r => r.GetPhotoBySharePointFileIdAsync("file-dup", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Photo?)null);

        _photoRepoMock
            .Setup(r => r.AddPhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _photoTagRepoMock
            .Setup(r => r.GetPhotoTagsByPhotoAndSourceAsync(It.IsAny<int>(), PhotoTagSource.Rule, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _photoTagRepoMock
            .Setup(r => r.RemovePhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _tagRepoMock
            .Setup(r => r.GetOrCreateTagsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int> { ["common"] = 99 });

        // Real DB semantics: PhotoTagExistsAsync can never see this batch's own unflushed
        // inserts, so it always returns false here — the fix must avoid relying on this check
        // to prevent the duplicate insert.
        _photoTagRepoMock
            .Setup(r => r.PhotoTagExistsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _photoTagRepoMock
            .Setup(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _photoRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _photoTagRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _rootRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _graphServiceMock
            .Setup(g => g.GetDeltaAsync("drive-1", "root-item-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphDeltaResult
            {
                Items = [item1, item2],
                NewDeltaLink = "https://graph.microsoft.com/v1.0/drives/drive-1/items/root-item-1/delta?token=abc",
            });

        // Act
        await _job.ExecuteAsync();

        // Assert — AddPhotoTagAsync is called exactly once for the (PhotoId, TagId) pair,
        // never twice, so a real DbContext's composite-key constraint would never be violated.
        _photoTagRepoMock.Verify(r => r.AddPhotoTagAsync(It.Is<PhotoTag>(pt => pt.TagId == 99), It.IsAny<CancellationToken>()), Times.Once);
    }
}
