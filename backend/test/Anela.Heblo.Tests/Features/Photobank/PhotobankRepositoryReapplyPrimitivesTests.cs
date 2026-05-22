using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Anela.Heblo.Application.Features.Photobank;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class PhotobankRepositoryReapplyPrimitivesTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly PhotobankRepository _repository;

    public PhotobankRepositoryReapplyPrimitivesTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new PhotobankRepository(_context);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async System.Threading.Tasks.Task GetAllPhotosAsync_returnsAllPhotos()
    {
        // Arrange
        _context.Photos.AddRange(
            new Photo { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "Products", ModifiedAt = DateTime.UtcNow },
            new Photo { Id = 2, SharePointFileId = "sp-2", FileName = "b.jpg", FolderPath = "Events", ModifiedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act
        var photos = await _repository.GetAllPhotosAsync(CancellationToken.None);

        // Assert
        photos.Should().HaveCount(2);
        photos.Select(p => p.Id).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public async System.Threading.Tasks.Task RemoveRuleTagsAsync_unscoped_removesOnlyRuleTags()
    {
        // Arrange
        _context.Photos.Add(new Photo { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "P", ModifiedAt = DateTime.UtcNow });
        _context.PhotobankTags.AddRange(
            new Tag { Id = 10, Name = "products" },
            new Tag { Id = 11, Name = "manualtag" });
        _context.PhotoTags.AddRange(
            new PhotoTag { PhotoId = 1, TagId = 10, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 1, TagId = 11, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act
        await _repository.RemoveRuleTagsAsync(null, CancellationToken.None);
        await _context.SaveChangesAsync(CancellationToken.None); // primitive does not save

        // Assert
        var remaining = await _context.PhotoTags.ToListAsync(CancellationToken.None);
        remaining.Should().ContainSingle();
        remaining[0].Source.Should().Be(PhotoTagSource.Manual);
    }

    [Fact]
    public async System.Threading.Tasks.Task RemoveRuleTagsAsync_scoped_removesOnlyMatchingTagName()
    {
        // Arrange
        _context.Photos.Add(new Photo { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "P", ModifiedAt = DateTime.UtcNow });
        _context.PhotobankTags.AddRange(
            new Tag { Id = 10, Name = "products" },
            new Tag { Id = 11, Name = "events" });
        _context.PhotoTags.AddRange(
            new PhotoTag { PhotoId = 1, TagId = 10, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 1, TagId = 11, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act
        await _repository.RemoveRuleTagsAsync("products", CancellationToken.None);
        await _context.SaveChangesAsync(CancellationToken.None);

        // Assert
        var remaining = await _context.PhotoTags.ToListAsync(CancellationToken.None);
        remaining.Should().ContainSingle();
        remaining[0].TagId.Should().Be(11); // events untouched
    }

    [Fact]
    public async System.Threading.Tasks.Task RemoveRuleTagsAsync_doesNotSaveByItself()
    {
        // Arrange
        _context.Photos.Add(new Photo { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "P", ModifiedAt = DateTime.UtcNow });
        _context.PhotobankTags.Add(new Tag { Id = 10, Name = "products" });
        _context.PhotoTags.Add(new PhotoTag { PhotoId = 1, TagId = 10, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act — call the primitive but DO NOT save
        await _repository.RemoveRuleTagsAsync(null, CancellationToken.None);

        // Assert — the deletion is only staged; the change tracker holds it as Deleted
        _context.ChangeTracker.Entries<PhotoTag>()
            .Should().Contain(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Deleted);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetOccupiedTagPairsAsync_unscoped_returnsOnlyNonRulePairs()
    {
        // Arrange
        _context.Photos.Add(new Photo { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "P", ModifiedAt = DateTime.UtcNow });
        _context.PhotobankTags.AddRange(
            new Tag { Id = 10, Name = "products" },
            new Tag { Id = 11, Name = "aitag" },
            new Tag { Id = 12, Name = "ruletag" });
        _context.PhotoTags.AddRange(
            new PhotoTag { PhotoId = 1, TagId = 10, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 1, TagId = 11, Source = PhotoTagSource.AI, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 1, TagId = 12, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act
        var occupied = await _repository.GetOccupiedTagPairsAsync(null, CancellationToken.None);

        // Assert
        occupied.Should().BeEquivalentTo(new HashSet<(int, int)> { (1, 10), (1, 11) });
        occupied.Should().NotContain((1, 12)); // Rule pair excluded
    }

    [Fact]
    public async System.Threading.Tasks.Task GetOccupiedTagPairsAsync_scoped_filtersByTagName()
    {
        // Arrange
        _context.Photos.Add(new Photo { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "P", ModifiedAt = DateTime.UtcNow });
        _context.PhotobankTags.AddRange(
            new Tag { Id = 10, Name = "products" },
            new Tag { Id = 11, Name = "events" });
        _context.PhotoTags.AddRange(
            new PhotoTag { PhotoId = 1, TagId = 10, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 1, TagId = 11, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act
        var occupied = await _repository.GetOccupiedTagPairsAsync("products", CancellationToken.None);

        // Assert
        occupied.Should().BeEquivalentTo(new HashSet<(int, int)> { (1, 10) });
    }

    [Fact]
    public async System.Threading.Tasks.Task AddPhotoTagsAsync_stagesRows_persistedAfterSave()
    {
        // Arrange
        _context.Photos.Add(new Photo { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "P", ModifiedAt = DateTime.UtcNow });
        _context.PhotobankTags.Add(new Tag { Id = 10, Name = "products" });
        await _context.SaveChangesAsync(CancellationToken.None);

        var toAdd = new List<PhotoTag>
        {
            new() { PhotoId = 1, TagId = 10, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow },
        };

        // Act
        await _repository.AddPhotoTagsAsync(toAdd, CancellationToken.None);
        await _context.SaveChangesAsync(CancellationToken.None); // primitive does not save

        // Assert
        var rows = await _context.PhotoTags.ToListAsync(CancellationToken.None);
        rows.Should().ContainSingle();
        rows[0].Source.Should().Be(PhotoTagSource.Rule);
    }
}
