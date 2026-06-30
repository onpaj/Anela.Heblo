using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Persistence.Photobank;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Features.Photobank.UseCases.ReapplyRules;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class ReapplyRulesBehaviorPreservationTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly ReapplyRulesHandler _handler;

    public ReapplyRulesBehaviorPreservationTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        var repository = new PhotobankRepository(_context);
        var cache = new Mock<IPhotobankTagsCache>();
        _handler = new ReapplyRulesHandler(repository, cache.Object);
    }

    public void Dispose() => _context.Dispose();

    private async Task<List<PhotoTag>> AllPhotoTagsAsync() =>
        await _context.PhotoTags.AsNoTracking().ToListAsync(CancellationToken.None);

    [Fact]
    public async Task ManualTagWins_RuleTagNotInsertedOverSharedPk()
    {
        // Arrange — photo matches the "products" rule, but a Manual tag already owns (1, 10).
        _context.Photos.Add(new Photo
        {
            Id = 1,
            SharePointFileId = "sp-1",
            FolderPath = "Products/A",
            FileName = "a.jpg",
            IndexedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        });
        _context.PhotobankTags.Add(new Tag { Id = 10, Name = "products" });
        _context.PhotoTags.Add(new PhotoTag { PhotoId = 1, TagId = 10, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow });
        _context.PhotobankTagRules.Add(new TagRule { Id = 1, PathPattern = "Products", TagName = "products", IsActive = true, SortOrder = 0 });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act
        var result = await _handler.Handle(new ReapplyRulesRequest(), CancellationToken.None);

        // Assert — the pair stays Manual, no Rule row added, photo not counted.
        result.PhotosUpdated.Should().Be(0);
        var tags = await AllPhotoTagsAsync();
        tags.Should().ContainSingle();
        tags[0].Source.Should().Be(PhotoTagSource.Manual);
    }

    [Fact]
    public async Task DuplicateMatch_AddsOneRow_PhotosUpdatedCountsPhotosNotTags()
    {
        // Arrange — photo 1 matches two rules producing the SAME tag; photo 2 matches two
        // rules producing DIFFERENT tags. photosUpdated should be 2 (photos), rows added = 3.
        _context.Photos.AddRange(
            new Photo
            {
                Id = 1,
                SharePointFileId = "sp-1",
                FolderPath = "Products/A",
                FileName = "a.jpg",
                IndexedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            },
            new Photo
            {
                Id = 2,
                SharePointFileId = "sp-2",
                FolderPath = "Products/Events",
                FileName = "b.jpg",
                IndexedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            });
        _context.PhotobankTagRules.AddRange(
            new TagRule { Id = 1, PathPattern = "Products", TagName = "products", IsActive = true, SortOrder = 0 },
            new TagRule { Id = 2, PathPattern = "A", TagName = "products", IsActive = true, SortOrder = 1 }, // dup tag for photo 1
            new TagRule { Id = 3, PathPattern = "Events", TagName = "events", IsActive = true, SortOrder = 2 }); // distinct tag for photo 2
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act
        var result = await _handler.Handle(new ReapplyRulesRequest(), CancellationToken.None);

        // Assert
        result.PhotosUpdated.Should().Be(2);
        var tags = await AllPhotoTagsAsync();
        tags.Should().HaveCount(3);
        tags.Where(t => t.PhotoId == 1).Should().ContainSingle(); // dedup
        tags.Where(t => t.PhotoId == 2).Should().HaveCount(2);
        tags.Should().OnlyContain(t => t.Source == PhotoTagSource.Rule);
    }

    [Fact]
    public async Task EmptyActiveRules_RemovesAllRuleTags_AndReturnsZero()
    {
        // Arrange — pre-existing Rule + Manual tags; the only rule is INACTIVE.
        // Current behavior: removal is committed (handler always saved), so all Rule tags go.
        _context.Photos.Add(new Photo
        {
            Id = 1,
            SharePointFileId = "sp-1",
            FolderPath = "Products/A",
            FileName = "a.jpg",
            IndexedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        });
        _context.PhotobankTags.AddRange(
            new Tag { Id = 10, Name = "products" },
            new Tag { Id = 11, Name = "manual" });
        _context.PhotoTags.AddRange(
            new PhotoTag { PhotoId = 1, TagId = 10, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 1, TagId = 11, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow });
        _context.PhotobankTagRules.Add(new TagRule { Id = 1, PathPattern = "Products", TagName = "products", IsActive = false, SortOrder = 0 });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act
        var result = await _handler.Handle(new ReapplyRulesRequest(), CancellationToken.None);

        // Assert
        result.PhotosUpdated.Should().Be(0);
        var tags = await AllPhotoTagsAsync();
        tags.Should().ContainSingle();
        tags[0].Source.Should().Be(PhotoTagSource.Manual); // Rule tag removed, Manual preserved
    }

    [Fact]
    public async Task ScopedReapply_OnlyTouchesTargetRuleTag()
    {
        // Arrange — photo matches both rules. A pre-existing "events" Rule tag must survive a
        // scoped re-apply of the "products" rule; a "products" Rule tag is recomputed.
        _context.Photos.Add(new Photo
        {
            Id = 1,
            SharePointFileId = "sp-1",
            FolderPath = "Products/Events",
            FileName = "a.jpg",
            IndexedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        });
        _context.PhotobankTags.AddRange(
            new Tag { Id = 10, Name = "products" },
            new Tag { Id = 11, Name = "events" });
        _context.PhotoTags.Add(new PhotoTag { PhotoId = 1, TagId = 11, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow });
        _context.PhotobankTagRules.AddRange(
            new TagRule { Id = 1, PathPattern = "Products", TagName = "products", IsActive = true, SortOrder = 0 },
            new TagRule { Id = 2, PathPattern = "Events", TagName = "events", IsActive = true, SortOrder = 1 });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act — scope to rule 1 ("products")
        var result = await _handler.Handle(new ReapplyRulesRequest { RuleId = 1 }, CancellationToken.None);

        // Assert
        result.PhotosUpdated.Should().Be(1);
        var tags = await AllPhotoTagsAsync();
        tags.Should().HaveCount(2);
        tags.Should().Contain(t => t.TagId == 10); // products added
        tags.Should().Contain(t => t.TagId == 11); // events untouched
    }

    [Fact]
    public async Task DoubleApply_NoNewTags_IsIdempotent_AndDoesNotThrow()
    {
        // Arrange — tag already exists, photo matches, a Rule tag already present.
        // This exercises the delete-then-re-add change-tracker hazard on the no-new-tags path.
        _context.Photos.Add(new Photo
        {
            Id = 1,
            SharePointFileId = "sp-1",
            FolderPath = "Products/A",
            FileName = "a.jpg",
            IndexedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        });
        _context.PhotobankTags.Add(new Tag { Id = 10, Name = "products" });
        _context.PhotoTags.Add(new PhotoTag { PhotoId = 1, TagId = 10, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow });
        _context.PhotobankTagRules.Add(new TagRule { Id = 1, PathPattern = "Products", TagName = "products", IsActive = true, SortOrder = 0 });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act — apply twice; neither should throw, and the result rows stay identical.
        var first = await _handler.Handle(new ReapplyRulesRequest(), CancellationToken.None);
        var second = await _handler.Handle(new ReapplyRulesRequest(), CancellationToken.None);

        // Assert
        first.PhotosUpdated.Should().Be(1);
        second.PhotosUpdated.Should().Be(1);
        var tags = await AllPhotoTagsAsync();
        tags.Should().ContainSingle();
        tags[0].Should().BeEquivalentTo(new { PhotoId = 1, TagId = 10, Source = PhotoTagSource.Rule },
            o => o.ExcludingMissingMembers());
    }
}
