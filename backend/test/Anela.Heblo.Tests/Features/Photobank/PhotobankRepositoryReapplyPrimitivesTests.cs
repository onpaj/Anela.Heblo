using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Anela.Heblo.Persistence.Photobank;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class PhotobankRepositoryReapplyPrimitivesTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly PhotobankAutoTagRepository _autoTagRepository;
    private readonly PhotobankPhotoTagRepository _photoTagRepository;
    private readonly PhotobankTagRepository _tagRepository;

    public PhotobankRepositoryReapplyPrimitivesTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _autoTagRepository = new PhotobankAutoTagRepository(_context);
        _photoTagRepository = new PhotobankPhotoTagRepository(_context);
        _tagRepository = new PhotobankTagRepository(_context);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async System.Threading.Tasks.Task GetPhotoRuleCandidatesPageAsync_firstPage_returnsProjectionOrderedById()
    {
        // Arrange
        _context.Photos.AddRange(
            new Photo { Id = 2, SharePointFileId = "sp-2", FileName = "b.jpg", FolderPath = "Events", ModifiedAt = DateTime.UtcNow },
            new Photo { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "Products", ModifiedAt = DateTime.UtcNow },
            new Photo { Id = 3, SharePointFileId = "sp-3", FileName = "c.jpg", FolderPath = "Events", ModifiedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act
        var page = await _autoTagRepository.GetPhotoRuleCandidatesPageAsync(pageSize: 2, offset: 0, CancellationToken.None);

        // Assert
        page.Should().HaveCount(2);
        page.Select(p => p.Id).Should().Equal(new[] { 1, 2 }); // ordered by Id, not insertion order
        page[0].FolderPath.Should().Be("Products");
        page[0].FileName.Should().Be("a.jpg");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPhotoRuleCandidatesPageAsync_secondPage_returnsRemainingRowsViaOffset()
    {
        // Arrange
        _context.Photos.AddRange(
            new Photo { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "Products", ModifiedAt = DateTime.UtcNow },
            new Photo { Id = 2, SharePointFileId = "sp-2", FileName = "b.jpg", FolderPath = "Events", ModifiedAt = DateTime.UtcNow },
            new Photo { Id = 3, SharePointFileId = "sp-3", FileName = "c.jpg", FolderPath = "Archive", ModifiedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act — page size 2: first page has 2 rows, second page (offset 2) has the remaining 1
        var secondPage = await _autoTagRepository.GetPhotoRuleCandidatesPageAsync(pageSize: 2, offset: 2, CancellationToken.None);

        // Assert
        secondPage.Should().ContainSingle();
        secondPage[0].Id.Should().Be(3);
        secondPage[0].FolderPath.Should().Be("Archive");
        secondPage[0].FileName.Should().Be("c.jpg");
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
        await _photoTagRepository.RemoveRuleTagsAsync(null, CancellationToken.None);
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
        await _photoTagRepository.RemoveRuleTagsAsync("products", CancellationToken.None);
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
        await _photoTagRepository.RemoveRuleTagsAsync(null, CancellationToken.None);

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
        var occupied = await _photoTagRepository.GetOccupiedTagPairsAsync(null, CancellationToken.None);

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
        var occupied = await _photoTagRepository.GetOccupiedTagPairsAsync("products", CancellationToken.None);

        // Assert
        occupied.Should().BeEquivalentTo(new HashSet<(int, int)> { (1, 10) });
    }

    [Fact]
    public async System.Threading.Tasks.Task GetOccupiedTagPairsByPhotosAsync_scopesToRequestedPhotoIdsOnly()
    {
        // Arrange — three photos, each with a non-Rule tag; only photos 1 and 2 are requested.
        // A photo-ID-scoped query must return only pairs for the requested photos, never
        // re-scanning (or returning pairs for) the whole table like the unscoped
        // GetOccupiedTagPairsAsync(null, ...) does.
        _context.Photos.AddRange(
            new Photo { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "P", ModifiedAt = DateTime.UtcNow },
            new Photo { Id = 2, SharePointFileId = "sp-2", FileName = "b.jpg", FolderPath = "P", ModifiedAt = DateTime.UtcNow },
            new Photo { Id = 3, SharePointFileId = "sp-3", FileName = "c.jpg", FolderPath = "P", ModifiedAt = DateTime.UtcNow });
        _context.PhotobankTags.AddRange(
            new Tag { Id = 10, Name = "products" },
            new Tag { Id = 11, Name = "ruletag" });
        _context.PhotoTags.AddRange(
            new PhotoTag { PhotoId = 1, TagId = 10, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 1, TagId = 11, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 2, TagId = 10, Source = PhotoTagSource.AI, CreatedAt = DateTime.UtcNow },
            // Photo 3 has a non-Rule pair too, but is NOT in the requested photoIds below —
            // it must be excluded from the result even though GetOccupiedTagPairsAsync(null, ...)
            // would include it.
            new PhotoTag { PhotoId = 3, TagId = 10, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act
        var occupied = await _photoTagRepository.GetOccupiedTagPairsByPhotosAsync(new[] { 1, 2 }, CancellationToken.None);

        // Assert
        occupied.Should().BeEquivalentTo(new HashSet<(int, int)> { (1, 10), (2, 10) });
        occupied.Should().NotContain((1, 11)); // Rule pair excluded
        occupied.Should().NotContain((3, 10)); // out-of-scope photo excluded
    }

    [Fact]
    public async System.Threading.Tasks.Task GetOccupiedTagPairsByPhotosAsync_emptyPhotoIds_returnsEmptyWithoutQuerying()
    {
        // Arrange
        _context.Photos.Add(new Photo { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "P", ModifiedAt = DateTime.UtcNow });
        _context.PhotobankTags.Add(new Tag { Id = 10, Name = "products" });
        _context.PhotoTags.Add(new PhotoTag { PhotoId = 1, TagId = 10, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act
        var occupied = await _photoTagRepository.GetOccupiedTagPairsByPhotosAsync(Array.Empty<int>(), CancellationToken.None);

        // Assert
        occupied.Should().BeEmpty();
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPhotoTagsByPhotosAndSourceAsync_multiplePhotos_returnsOnlyMatchingSourceGroupedByPhotoId()
    {
        // Arrange
        _context.Photos.AddRange(
            new Photo { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "P", ModifiedAt = DateTime.UtcNow },
            new Photo { Id = 2, SharePointFileId = "sp-2", FileName = "b.jpg", FolderPath = "P", ModifiedAt = DateTime.UtcNow },
            new Photo { Id = 3, SharePointFileId = "sp-3", FileName = "c.jpg", FolderPath = "P", ModifiedAt = DateTime.UtcNow });
        _context.PhotobankTags.AddRange(
            new Tag { Id = 10, Name = "products" },
            new Tag { Id = 11, Name = "events" },
            new Tag { Id = 12, Name = "manualtag" });
        _context.PhotoTags.AddRange(
            new PhotoTag { PhotoId = 1, TagId = 10, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 1, TagId = 11, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 2, TagId = 10, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 2, TagId = 12, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow },
            // Photo 3 has no Rule tags at all, and is NOT in the requested photoIds set below.
            new PhotoTag { PhotoId = 3, TagId = 10, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act — only ask for photos 1 and 2; photo 3's Rule tag must not leak in even though
        // it matches the source filter, because it is outside the requested photo-ID set.
        var result = await _photoTagRepository.GetPhotoTagsByPhotosAndSourceAsync(
            new[] { 1, 2 }, PhotoTagSource.Rule, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result[1].Select(pt => pt.TagId).Should().BeEquivalentTo(new[] { 10, 11 });
        result[2].Select(pt => pt.TagId).Should().BeEquivalentTo(new[] { 10 }); // Manual tag excluded
        result.Should().NotContainKey(3);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPhotoTagsByPhotosAndSourceAsync_emptyPhotoIds_returnsEmptyDictionaryWithoutQuerying()
    {
        // Act
        var result = await _photoTagRepository.GetPhotoTagsByPhotosAndSourceAsync(
            Array.Empty<int>(), PhotoTagSource.Rule, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
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
        await _photoTagRepository.AddPhotoTagsAsync(toAdd, CancellationToken.None);
        await _context.SaveChangesAsync(CancellationToken.None); // primitive does not save

        // Assert
        var rows = await _context.PhotoTags.ToListAsync(CancellationToken.None);
        rows.Should().ContainSingle();
        rows[0].Source.Should().Be(PhotoTagSource.Rule);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetOrCreateTagsAsync_returnsExistingIds_andCreatesMissing()
    {
        // Arrange
        _context.PhotobankTags.Add(new Tag { Id = 10, Name = "products" });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act — "products" exists, "events" is new
        var map = await _tagRepository.GetOrCreateTagsAsync(new[] { "products", "events" }, CancellationToken.None);

        // Assert
        map.Should().ContainKey("products").WhoseValue.Should().Be(10);
        map.Should().ContainKey("events");
        map["events"].Should().BeGreaterThan(0); // DB-assigned id

        var persisted = await _context.PhotobankTags.ToListAsync(CancellationToken.None);
        persisted.Select(t => t.Name).Should().BeEquivalentTo(new[] { "products", "events" });
    }

    [Fact]
    public async System.Threading.Tasks.Task GetOrCreateTagsAsync_allExisting_returnsIdsWithoutCreating()
    {
        // Arrange
        _context.PhotobankTags.AddRange(
            new Tag { Id = 10, Name = "products" },
            new Tag { Id = 11, Name = "events" });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act
        var map = await _tagRepository.GetOrCreateTagsAsync(new[] { "products", "events" }, CancellationToken.None);

        // Assert
        map["products"].Should().Be(10);
        map["events"].Should().Be(11);
        (await _context.PhotobankTags.CountAsync(CancellationToken.None)).Should().Be(2);
    }
}
