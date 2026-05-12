using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence;
using DotNet.Testcontainers.Configurations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

[Trait("Category", "Integration")]
public class PhotobankRepositoryFilterTests : IAsyncLifetime
{
    static PhotobankRepositoryFilterTests()
    {
        TestcontainersSettings.ResourceReaperEnabled = false;
    }

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private ApplicationDbContext _context = null!;
    private PhotobankRepository _repository = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        _context = new ApplicationDbContext(options);

        await SetupSchemaAsync();

        _repository = new PhotobankRepository(_context);

        await SeedAsync();
    }

    private async Task SetupSchemaAsync()
    {
        await using var conn = new NpgsqlConnection(_container.GetConnectionString());
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS public."Photos" (
                "Id" SERIAL PRIMARY KEY,
                "SharePointFileId" VARCHAR(500) NOT NULL UNIQUE,
                "FolderPath" VARCHAR(2000) NOT NULL,
                "FileName" VARCHAR(200) NOT NULL,
                "SharePointWebUrl" VARCHAR(2000),
                "DriveId" VARCHAR(500),
                "MimeType" VARCHAR(50),
                "FileSizeBytes" BIGINT,
                "TakenAt" TIMESTAMP,
                "IndexedAt" TIMESTAMP NOT NULL,
                "ModifiedAt" TIMESTAMP NOT NULL,
                "LastAutoTaggedAt" TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS public."PhotobankTags" (
                "Id" SERIAL PRIMARY KEY,
                "Name" VARCHAR(100) NOT NULL
            );

            CREATE TABLE IF NOT EXISTS public."PhotoTags" (
                "PhotoId" INT NOT NULL REFERENCES public."Photos"("Id") ON DELETE CASCADE,
                "TagId" INT NOT NULL REFERENCES public."PhotobankTags"("Id") ON DELETE CASCADE,
                "Source" VARCHAR(20) NOT NULL,
                "CreatedAt" TIMESTAMP NOT NULL,
                PRIMARY KEY ("PhotoId", "TagId")
            );

            CREATE EXTENSION IF NOT EXISTS pg_trgm;

            CREATE INDEX IF NOT EXISTS "IX_Photos_ModifiedAt" ON public."Photos" ("ModifiedAt" DESC, "Id" DESC);

            CREATE INDEX IF NOT EXISTS "IX_Photos_PathTrgm" ON public."Photos" USING GIN ((LOWER("FolderPath" || '/' || "FileName")) gin_trgm_ops);
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    private async Task SeedAsync()
    {
        var now = DateTime.UtcNow;
        var photos = new List<Photo>
        {
            new() { Id = 1, SharePointFileId = "sp-1", FileName = "ruze-cervena.jpg",    FolderPath = "Marketing/Produkty/Ruze",    IndexedAt = now, ModifiedAt = now },
            new() { Id = 2, SharePointFileId = "sp-2", FileName = "levandule.jpg",       FolderPath = "Marketing/Produkty/Levandule", IndexedAt = now, ModifiedAt = now },
            new() { Id = 3, SharePointFileId = "sp-3", FileName = "banner-homepage.png", FolderPath = "Marketing/Web",              IndexedAt = now, ModifiedAt = now },
            new() { Id = 4, SharePointFileId = "sp-4", FileName = "vyrobek-01.jpg",      FolderPath = "Vyrobky/2025",               IndexedAt = now, ModifiedAt = now },
        };
        _context.Photos.AddRange(photos);
        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task GetPhotosAsync_filtersByFolderPath_substringMatch()
    {
        var (items, total) = await _repository.GetPhotosAsync(
            null, "Produkty", false, false, 1, 48, CancellationToken.None);

        total.Should().Be(2);
        items.Should().OnlyContain(p => p.FolderPath.Contains("Produkty", StringComparison.OrdinalIgnoreCase));
        items.Should().NotContain(p => p.FileName == "banner-homepage.png");
        items.Should().NotContain(p => p.FileName == "vyrobek-01.jpg");
    }

    [Fact]
    public async Task GetPhotosAsync_filterByFolderPath_caseInsensitive()
    {
        var (items, total) = await _repository.GetPhotosAsync(
            null, "MARKETING/WEB", false, false, 1, 48, CancellationToken.None);

        total.Should().Be(1);
        items.Should().ContainSingle(p => p.FileName == "banner-homepage.png");
    }

    [Fact]
    public async Task GetPhotosAsync_searchMatchesInsideCombinedPath()
    {
        var (items, total) = await _repository.GetPhotosAsync(
            null, "ruze", false, false, 1, 48, CancellationToken.None);

        total.Should().Be(1);
        items.Should().ContainSingle(p => p.FileName == "ruze-cervena.jpg");
    }

    [Fact]
    public async Task GetPhotosAsync_combinesFolderPathWithTag()
    {
        var tag = new Tag { Id = 10, Name = "featured" };
        _context.PhotobankTags.Add(tag);
        _context.PhotoTags.Add(new PhotoTag
        {
            PhotoId = 1,
            TagId = 10,
            Source = PhotoTagSource.Manual,
            CreatedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync();

        var (items, total) = await _repository.GetPhotosAsync(
            new List<string> { "featured" }, "Produkty", false, false, 1, 48, CancellationToken.None);

        total.Should().Be(1);
        items.Should().ContainSingle(p => p.FileName == "ruze-cervena.jpg");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetPhotosAsync_emptySearch_doesNotFilter(string? search)
    {
        var (items, total) = await _repository.GetPhotosAsync(
            null, search, false, false, 1, 48, CancellationToken.None);

        total.Should().Be(4);
        items.Should().HaveCount(4);
    }

    [Fact]
    public async Task GetPhotosAsync_searchWithLikeWildcards_treatsWildcardsAsLiterals()
    {
        var now = DateTime.UtcNow;
        _context.Photos.Add(new Photo
        {
            Id = 100,
            SharePointFileId = "sp-100",
            FileName = "with_underscore.jpg",
            FolderPath = "Marketing/Special",
            IndexedAt = now,
            ModifiedAt = now,
        });
        _context.Photos.Add(new Photo
        {
            Id = 101,
            SharePointFileId = "sp-101",
            FileName = "without-underscore.jpg",
            FolderPath = "Marketing/Special",
            IndexedAt = now,
            ModifiedAt = now,
        });
        await _context.SaveChangesAsync();

        var (items, total) = await _repository.GetPhotosAsync(
            null, "with_underscore", false, false, 1, 48, CancellationToken.None);

        total.Should().Be(1);
        items.Should().ContainSingle(p => p.FileName == "with_underscore.jpg");
    }

    [Fact]
    public async Task GetPhotosAsync_multiTagAnd_returnsOnlyPhotosWithEveryTag()
    {
        var now = DateTime.UtcNow;
        var tagA = new Tag { Id = 200, Name = "alpha" };
        var tagB = new Tag { Id = 201, Name = "beta" };
        var tagC = new Tag { Id = 202, Name = "gamma" };
        _context.PhotobankTags.AddRange(tagA, tagB, tagC);

        _context.PhotoTags.AddRange(
            new PhotoTag { PhotoId = 1, TagId = 200, Source = PhotoTagSource.Manual, CreatedAt = now },
            new PhotoTag { PhotoId = 1, TagId = 201, Source = PhotoTagSource.Manual, CreatedAt = now },
            new PhotoTag { PhotoId = 1, TagId = 202, Source = PhotoTagSource.Manual, CreatedAt = now },
            new PhotoTag { PhotoId = 2, TagId = 200, Source = PhotoTagSource.Manual, CreatedAt = now },
            new PhotoTag { PhotoId = 2, TagId = 201, Source = PhotoTagSource.Manual, CreatedAt = now },
            new PhotoTag { PhotoId = 3, TagId = 200, Source = PhotoTagSource.Manual, CreatedAt = now });
        await _context.SaveChangesAsync();

        var (items, total) = await _repository.GetPhotosAsync(
            new List<string> { "alpha", "beta", "gamma" }, null, false, false, 1, 48, CancellationToken.None);

        total.Should().Be(1);
        items.Should().ContainSingle(p => p.Id == 1);
    }
}

[Trait("Category", "Integration")]
public class PhotobankRepositoryRegexFilterTests : IAsyncLifetime
{
    static PhotobankRepositoryRegexFilterTests()
    {
        TestcontainersSettings.ResourceReaperEnabled = false;
    }

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private ApplicationDbContext _context = null!;
    private PhotobankRepository _repository = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        _context = new ApplicationDbContext(options);

        await SetupSchemaAsync();

        _repository = new PhotobankRepository(_context);

        var now = DateTime.UtcNow;
        _context.Photos.AddRange(new List<Photo>
        {
            new() { Id = 1, SharePointFileId = "sp-1", FileName = "report_2024.pdf",  FolderPath = "Reports", IndexedAt = now, ModifiedAt = now },
            new() { Id = 2, SharePointFileId = "sp-2", FileName = "IMG_001.png",      FolderPath = "Photos",  IndexedAt = now, ModifiedAt = now },
            new() { Id = 3, SharePointFileId = "sp-3", FileName = "report_final.pdf", FolderPath = "Reports", IndexedAt = now, ModifiedAt = now },
        });
        await _context.SaveChangesAsync();
    }

    private async Task SetupSchemaAsync()
    {
        await using var conn = new NpgsqlConnection(_container.GetConnectionString());
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS public."Photos" (
                "Id" SERIAL PRIMARY KEY,
                "SharePointFileId" VARCHAR(500) NOT NULL UNIQUE,
                "FolderPath" VARCHAR(2000) NOT NULL,
                "FileName" VARCHAR(200) NOT NULL,
                "SharePointWebUrl" VARCHAR(2000),
                "DriveId" VARCHAR(500),
                "MimeType" VARCHAR(50),
                "FileSizeBytes" BIGINT,
                "TakenAt" TIMESTAMP,
                "IndexedAt" TIMESTAMP NOT NULL,
                "ModifiedAt" TIMESTAMP NOT NULL,
                "LastAutoTaggedAt" TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS public."PhotobankTags" (
                "Id" SERIAL PRIMARY KEY,
                "Name" VARCHAR(100) NOT NULL
            );

            CREATE TABLE IF NOT EXISTS public."PhotoTags" (
                "PhotoId" INT NOT NULL REFERENCES public."Photos"("Id") ON DELETE CASCADE,
                "TagId" INT NOT NULL REFERENCES public."PhotobankTags"("Id") ON DELETE CASCADE,
                "Source" VARCHAR(20) NOT NULL,
                "CreatedAt" TIMESTAMP NOT NULL,
                PRIMARY KEY ("PhotoId", "TagId")
            );

            CREATE EXTENSION IF NOT EXISTS pg_trgm;

            CREATE INDEX IF NOT EXISTS "IX_Photos_ModifiedAt" ON public."Photos" ("ModifiedAt" DESC, "Id" DESC);

            CREATE INDEX IF NOT EXISTS "IX_Photos_PathTrgm" ON public."Photos" USING GIN ((LOWER("FolderPath" || '/' || "FileName")) gin_trgm_ops);
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task GetPhotosAsync_regexSearch_matchesOnlyNumericReport()
    {
        var (items, total) = await _repository.GetPhotosAsync(
            null, @"report_\d+", true, false, 1, 48, CancellationToken.None);

        total.Should().Be(1);
        items.Should().ContainSingle(p => p.FileName == "report_2024.pdf");
        items.Should().NotContain(p => p.FileName == "report_final.pdf");
        items.Should().NotContain(p => p.FileName == "IMG_001.png");
    }

    [Fact]
    public async Task GetPhotosAsync_substringSearch_matchesBothReportFiles()
    {
        var (items, total) = await _repository.GetPhotosAsync(
            null, "report", false, false, 1, 48, CancellationToken.None);

        total.Should().Be(2);
        items.Should().Contain(p => p.FileName == "report_2024.pdf");
        items.Should().Contain(p => p.FileName == "report_final.pdf");
        items.Should().NotContain(p => p.FileName == "IMG_001.png");
    }
}

[Trait("Category", "Integration")]
public class PhotobankRepositoryPathRegexFilterTests : IAsyncLifetime
{
    static PhotobankRepositoryPathRegexFilterTests()
    {
        TestcontainersSettings.ResourceReaperEnabled = false;
    }

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private ApplicationDbContext _context = null!;
    private PhotobankRepository _repository = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        _context = new ApplicationDbContext(options);

        await SetupSchemaAsync();

        _repository = new PhotobankRepository(_context);

        var now = DateTime.UtcNow;
        _context.Photos.AddRange(new List<Photo>
        {
            new() { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "Marketing/2025/Q1", IndexedAt = now, ModifiedAt = now },
            new() { Id = 2, SharePointFileId = "sp-2", FileName = "b.jpg", FolderPath = "Marketing/2025/Q2", IndexedAt = now, ModifiedAt = now },
            new() { Id = 3, SharePointFileId = "sp-3", FileName = "c.jpg", FolderPath = "Vyrobky/2025",      IndexedAt = now, ModifiedAt = now },
        });
        await _context.SaveChangesAsync();
    }

    private async Task SetupSchemaAsync()
    {
        await using var conn = new NpgsqlConnection(_container.GetConnectionString());
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS public."Photos" (
                "Id" SERIAL PRIMARY KEY,
                "SharePointFileId" VARCHAR(500) NOT NULL UNIQUE,
                "FolderPath" VARCHAR(2000) NOT NULL,
                "FileName" VARCHAR(200) NOT NULL,
                "SharePointWebUrl" VARCHAR(2000),
                "DriveId" VARCHAR(500),
                "MimeType" VARCHAR(50),
                "FileSizeBytes" BIGINT,
                "TakenAt" TIMESTAMP,
                "IndexedAt" TIMESTAMP NOT NULL,
                "ModifiedAt" TIMESTAMP NOT NULL,
                "LastAutoTaggedAt" TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS public."PhotobankTags" (
                "Id" SERIAL PRIMARY KEY,
                "Name" VARCHAR(100) NOT NULL
            );

            CREATE TABLE IF NOT EXISTS public."PhotoTags" (
                "PhotoId" INT NOT NULL REFERENCES public."Photos"("Id") ON DELETE CASCADE,
                "TagId" INT NOT NULL REFERENCES public."PhotobankTags"("Id") ON DELETE CASCADE,
                "Source" VARCHAR(20) NOT NULL,
                "CreatedAt" TIMESTAMP NOT NULL,
                PRIMARY KEY ("PhotoId", "TagId")
            );

            CREATE EXTENSION IF NOT EXISTS pg_trgm;

            CREATE INDEX IF NOT EXISTS "IX_Photos_ModifiedAt" ON public."Photos" ("ModifiedAt" DESC, "Id" DESC);

            CREATE INDEX IF NOT EXISTS "IX_Photos_PathTrgm" ON public."Photos" USING GIN ((LOWER("FolderPath" || '/' || "FileName")) gin_trgm_ops);
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task GetPhotosAsync_regexSearch_matchesOnlyMarketingFolderPaths()
    {
        var (items, total) = await _repository.GetPhotosAsync(
            null, @"^Marketing/", true, false, 1, 48, CancellationToken.None);

        total.Should().Be(2);
        items.Should().OnlyContain(p => p.FolderPath.StartsWith("Marketing/"));
        items.Should().NotContain(p => p.FileName == "c.jpg");
    }

    [Fact]
    public async Task GetPhotosAsync_substringSearch_matchesAllPathsContaining2025()
    {
        var (items, total) = await _repository.GetPhotosAsync(
            null, "2025", false, false, 1, 48, CancellationToken.None);

        total.Should().Be(3);
    }
}
