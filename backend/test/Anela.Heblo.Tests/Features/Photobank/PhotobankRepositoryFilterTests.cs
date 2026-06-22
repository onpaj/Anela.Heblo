using System;
using System.Collections.Generic;
using System.Threading;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Photobank;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

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
        // Arrange — "Produkty" is a substring of two combined paths
        var search = "Produkty";

        // Act
        var (items, total) = await _repository.GetPhotosAsync(
            null, search, false, false, 1, 48, CancellationToken.None);

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
        var search = "MARKETING/WEB";

        // Act
        var (items, total) = await _repository.GetPhotosAsync(
            null, search, false, false, 1, 48, CancellationToken.None);

        // Assert
        total.Should().Be(1);
        items.Should().ContainSingle(p => p.FileName == "banner-homepage.png");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPhotosAsync_searchMatchesInsideCombinedPath()
    {
        // Arrange — "ruze" appears in both the folder name and the filename of photo 1
        var search = "ruze";

        // Act
        var (items, total) = await _repository.GetPhotosAsync(
            null, search, false, false, 1, 48, CancellationToken.None);

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

        // Act — search "Produkty" matches photos 1 & 2; tag "featured" is only on photo 1
        var (items, total) = await _repository.GetPhotosAsync(
            new List<string> { "featured" }, "Produkty", false, false, 1, 48, CancellationToken.None);

        // Assert
        total.Should().Be(1);
        items.Should().ContainSingle(p => p.FileName == "ruze-cervena.jpg");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async System.Threading.Tasks.Task GetPhotosAsync_emptySearch_doesNotFilter(string? search)
    {
        // Act
        var (items, total) = await _repository.GetPhotosAsync(
            null, search, false, false, 1, 48, CancellationToken.None);

        // Assert
        total.Should().Be(4);
        items.Should().HaveCount(4);
    }
}

// NOTE: These tests run against the EF Core InMemory provider, which evaluates
// Regex.IsMatch via the .NET regex engine. In production, Npgsql translates
// this to Postgres POSIX ~* syntax. The two engines differ on some constructs
// (e.g., .NET lookahead, \b). Postgres-specific failures are caught by the
// PostgresException handler in GetPhotosHandler.
public class PhotobankRepositoryRegexFilterTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly PhotobankRepository _repository;

    public PhotobankRepositoryRegexFilterTests()
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
            new() { Id = 1, SharePointFileId = "sp-1", FileName = "report_2024.pdf",  FolderPath = "Reports", ModifiedAt = DateTime.UtcNow },
            new() { Id = 2, SharePointFileId = "sp-2", FileName = "IMG_001.png",      FolderPath = "Photos",  ModifiedAt = DateTime.UtcNow },
            new() { Id = 3, SharePointFileId = "sp-3", FileName = "report_final.pdf", FolderPath = "Reports", ModifiedAt = DateTime.UtcNow },
        };

        _context.Photos.AddRange(photos);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPhotosAsync_regexSearch_matchesOnlyNumericReport()
    {
        // Arrange — pattern matches "report_" followed by digits; no ^ anchor since search targets
        // the combined folderPath+"/"+fileName string (e.g. "Reports/report_2024.pdf")
        var pattern = @"report_\d+";

        // Act
        var (items, total) = await _repository.GetPhotosAsync(
            null, pattern, true, false, 1, 48, CancellationToken.None);

        // Assert
        total.Should().Be(1);
        items.Should().ContainSingle(p => p.FileName == "report_2024.pdf");
        items.Should().NotContain(p => p.FileName == "report_final.pdf");
        items.Should().NotContain(p => p.FileName == "IMG_001.png");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPhotosAsync_substringSearch_matchesBothReportFiles()
    {
        // Arrange — plain substring search returns all files in folders whose combined path contains "report"
        var search = "report";

        // Act
        var (items, total) = await _repository.GetPhotosAsync(
            null, search, false, false, 1, 48, CancellationToken.None);

        // Assert
        total.Should().Be(2);
        items.Should().Contain(p => p.FileName == "report_2024.pdf");
        items.Should().Contain(p => p.FileName == "report_final.pdf");
        items.Should().NotContain(p => p.FileName == "IMG_001.png");
    }
}

public class PhotobankRepositoryPathRegexFilterTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly PhotobankRepository _repository;

    public PhotobankRepositoryPathRegexFilterTests()
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
            new() { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "Marketing/2025/Q1", ModifiedAt = DateTime.UtcNow },
            new() { Id = 2, SharePointFileId = "sp-2", FileName = "b.jpg", FolderPath = "Marketing/2025/Q2", ModifiedAt = DateTime.UtcNow },
            new() { Id = 3, SharePointFileId = "sp-3", FileName = "c.jpg", FolderPath = "Vyrobky/2025",      ModifiedAt = DateTime.UtcNow },
        };

        _context.Photos.AddRange(photos);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPhotosAsync_regexSearch_matchesOnlyMarketingFolderPaths()
    {
        // Arrange — anchored pattern matches combined paths that start with "Marketing/"
        var pattern = @"^Marketing/";

        // Act
        var (items, total) = await _repository.GetPhotosAsync(
            null, pattern, true, false, 1, 48, CancellationToken.None);

        // Assert
        total.Should().Be(2);
        items.Should().OnlyContain(p => p.FolderPath.StartsWith("Marketing/"));
        items.Should().NotContain(p => p.FileName == "c.jpg");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPhotosAsync_substringSearch_matchesAllPathsContaining2025()
    {
        // Arrange
        var term = "2025";

        // Act
        var (items, total) = await _repository.GetPhotosAsync(
            null, term, false, false, 1, 48, CancellationToken.None);

        // Assert
        total.Should().Be(3);
    }
}
