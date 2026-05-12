using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class PhotobankRepositoryGetTagsTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly PhotobankRepository _repository;

    public PhotobankRepositoryGetTagsTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
        _repository = new PhotobankRepository(_context);
        SeedTagsAndPhotoTags();
    }

    public void Dispose() => _context.Dispose();

    private void SeedTagsAndPhotoTags()
    {
        var summer = new Tag { Id = 1, Name = "summer" };
        var winter = new Tag { Id = 2, Name = "winter" };
        var product = new Tag { Id = 3, Name = "products" };
        var orphan = new Tag { Id = 4, Name = "orphan" };

        _context.Photos.AddRange(
            new Photo { Id = 100, SharePointFileId = "sp-100", FileName = "a.jpg", FolderPath = "p", ModifiedAt = DateTime.UtcNow },
            new Photo { Id = 101, SharePointFileId = "sp-101", FileName = "b.jpg", FolderPath = "p", ModifiedAt = DateTime.UtcNow },
            new Photo { Id = 102, SharePointFileId = "sp-102", FileName = "c.jpg", FolderPath = "p", ModifiedAt = DateTime.UtcNow });

        _context.PhotobankTags.AddRange(summer, winter, product, orphan);

        // summer: 3, products: 2, winter: 1, orphan: 0
        _context.PhotoTags.AddRange(
            new PhotoTag { PhotoId = 100, TagId = 1, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 101, TagId = 1, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 102, TagId = 1, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 100, TagId = 3, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 101, TagId = 3, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 100, TagId = 2, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow });
        _context.SaveChanges();
    }

    [Fact]
    public async Task GetTagsWithCountsAsync_ReturnsAllTagsIncludingOrphansWithZeroCount()
    {
        var result = await _repository.GetTagsWithCountsAsync(CancellationToken.None);

        result.Should().HaveCount(4);
        result.Single(t => t.Name == "orphan").Count.Should().Be(0);
    }

    [Fact]
    public async Task GetTagsWithCountsAsync_OrdersByCountDescThenNameAsc()
    {
        var result = await _repository.GetTagsWithCountsAsync(CancellationToken.None);

        result.Select(t => t.Name).Should().ContainInOrder("summer", "products", "winter", "orphan");
    }

    [Fact]
    public async Task GetTagsWithCountsAsync_ReturnsProjectionsNotEntities()
    {
        var result = await _repository.GetTagsWithCountsAsync(CancellationToken.None);

        result.Should().AllBeOfType<TagCount>();
    }

    [Fact]
    public async Task GetTagsWithCountsAsync_ReturnsTagCountRecord()
    {
        var result = await _repository.GetTagsWithCountsAsync(CancellationToken.None);

        result.Should().AllSatisfy(r => r.Should().BeOfType<TagCount>());
    }

    [Fact]
    public async Task GetTagsWithCountsAsync_DoesNotTrackTagEntities()
    {
        _context.ChangeTracker.Clear();
        var result = await _repository.GetTagsWithCountsAsync(CancellationToken.None);

        result.Should().HaveCount(4);
        _context.ChangeTracker.Entries<Tag>().Should().BeEmpty(
            "FR-4 requires the read path to project without entity tracking");
    }
}
