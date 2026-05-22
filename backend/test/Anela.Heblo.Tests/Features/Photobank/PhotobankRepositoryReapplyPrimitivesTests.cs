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
}
