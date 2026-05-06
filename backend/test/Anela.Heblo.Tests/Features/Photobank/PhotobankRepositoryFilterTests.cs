using System;
using System.Collections.Generic;
using System.Threading;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence;
using Anela.Heblo.Application.Features.Photobank;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Features.Photobank;

public class PhotobankRepositoryFilterTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly PhotobankRepository _repository;

    public PhotobankRepositoryFilterTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new PhotobankRepository(_context);

        SeedTestData();
    }

    private void SeedTestData()
    {
        var photos = new List<Photo>
        {
            new() { Id = 1, SharePointFileId = "sp-1", FileName = "ruze-cervena.jpg",    FolderPath = "Marketing/Produkty/Ruze",    ModifiedAt = DateTime.UtcNow },
            new() { Id = 2, SharePointFileId = "sp-2", FileName = "levandule.jpg",       FolderPath = "Marketing/Produkty/Levandule", ModifiedAt = DateTime.UtcNow },
            new() { Id = 3, SharePointFileId = "sp-3", FileName = "banner-homepage.png", FolderPath = "Marketing/Web",              ModifiedAt = DateTime.UtcNow },
            new() { Id = 4, SharePointFileId = "sp-4", FileName = "vyrobek-01.jpg",      FolderPath = "Vyrobky/2025",               ModifiedAt = DateTime.UtcNow },
        };

        _context.Photos.AddRange(photos);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPhotosAsync_filtersByFolderPath_substringMatch()
    {
        // Arrange — "Produkty" is a substring of two folder paths
        var folderPath = "Produkty";

        // Act
        var (items, total) = await _repository.GetPhotosAsync(
            null, null, folderPath, 1, 48, CancellationToken.None);

        // Assert
        total.Should().Be(2);
        items.Should().OnlyContain(p => p.FolderPath.Contains("Produkty", StringComparison.OrdinalIgnoreCase));
        items.Should().NotContain(p => p.FileName == "banner-homepage.png");
        items.Should().NotContain(p => p.FileName == "vyrobek-01.jpg");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPhotosAsync_filterByFolderPath_caseInsensitive()
    {
        // Arrange — uppercase input, lowercase stored path
        var folderPath = "MARKETING/WEB";

        // Act
        var (items, total) = await _repository.GetPhotosAsync(
            null, null, folderPath, 1, 48, CancellationToken.None);

        // Assert
        total.Should().Be(1);
        items.Should().ContainSingle(p => p.FileName == "banner-homepage.png");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPhotosAsync_combinesFolderPathWithFilename()
    {
        // Arrange — folderPath matches two photos, filename narrows to one
        var folderPath = "Produkty";
        var search = "ruze";

        // Act
        var (items, total) = await _repository.GetPhotosAsync(
            null, search, folderPath, 1, 48, CancellationToken.None);

        // Assert
        total.Should().Be(1);
        items.Should().ContainSingle(p => p.FileName == "ruze-cervena.jpg");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPhotosAsync_combinesFolderPathWithTag()
    {
        // Arrange — seed a tag on one of the two "Produkty" photos
        var tag = new Tag { Id = 10, Name = "featured" };
        // Safe: each test gets its own in-memory DB (Guid name + per-instance constructor).
        _context.PhotobankTags.Add(tag);

        var photoTag = new PhotoTag
        {
            PhotoId = 1, // ruze-cervena.jpg
            TagId = 10,
            Source = PhotoTagSource.Manual,
            CreatedAt = DateTime.UtcNow,
        };
        _context.PhotoTags.Add(photoTag);
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act — folderPath "Produkty" matches photos 1 & 2; tag "featured" is only on photo 1
        var (items, total) = await _repository.GetPhotosAsync(
            new List<string> { "featured" }, null, "Produkty", 1, 48, CancellationToken.None);

        // Assert
        total.Should().Be(1);
        items.Should().ContainSingle(p => p.FileName == "ruze-cervena.jpg");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async System.Threading.Tasks.Task GetPhotosAsync_emptyFolderPath_doesNotFilter(string? folderPath)
    {
        // Act
        var (items, total) = await _repository.GetPhotosAsync(
            null, null, folderPath, 1, 48, CancellationToken.None);

        // Assert
        total.Should().Be(4);
        items.Should().HaveCount(4);
    }
}
