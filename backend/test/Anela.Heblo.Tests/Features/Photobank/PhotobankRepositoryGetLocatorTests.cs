using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence;
using Anela.Heblo.Application.Features.Photobank;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public sealed class PhotobankRepositoryGetLocatorTests : IAsyncLifetime
{
    private readonly ApplicationDbContext _context;
    private readonly PhotobankRepository _repository;

    public PhotobankRepositoryGetLocatorTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"PhotobankLocatorTests_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new PhotobankRepository(_context);
    }

    [Fact]
    public async Task GetLocatorAsync_ReturnsLocator_WhenPhotoExists()
    {
        // Arrange
        var modifiedAt = new DateTime(2026, 3, 15, 10, 0, 0, DateTimeKind.Utc);
        var photo = new Photo
        {
            SharePointFileId = "sp-file-001",
            DriveId = "drive-abc",
            FileName = "photo.jpg",
            FolderPath = "/Fotky",
            ModifiedAt = modifiedAt,
            IndexedAt = DateTime.UtcNow,
        };
        _context.Photos.Add(photo);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetLocatorAsync(photo.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.DriveId.Should().Be("drive-abc");
        result.SharePointFileId.Should().Be("sp-file-001");
        result.ModifiedAt.Should().Be(modifiedAt);
    }

    [Fact]
    public async Task GetLocatorAsync_ReturnsNull_WhenPhotoDoesNotExist()
    {
        // Act
        var result = await _repository.GetLocatorAsync(99999, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetLocatorAsync_ReturnsNull_WhenPhotoHasNullDriveId()
    {
        // Arrange
        var photo = new Photo
        {
            SharePointFileId = "sp-file-no-drive",
            DriveId = null,
            FileName = "photo.jpg",
            FolderPath = "/Fotky",
            ModifiedAt = DateTime.UtcNow,
            IndexedAt = DateTime.UtcNow,
        };
        _context.Photos.Add(photo);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetLocatorAsync(photo.Id, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _context.DisposeAsync();
}
