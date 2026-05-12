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

/// <summary>
/// Tests that pin the current behavior of CountFilteredPhotosAsync and GetFilteredPhotoIdsMissingTagAsync.
/// Both methods call BuildFilterQuery internally, so these tests guard against regressions when
/// BuildFilterQuery is refactored (Task 3/4).
/// </summary>
[Trait("Category", "Integration")]
public class PhotobankRepositoryBuildFilterQueryConsumerTests : IAsyncLifetime
{
    static PhotobankRepositoryBuildFilterQueryConsumerTests()
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

        // Seed 3 photos
        var photos = new List<Photo>
        {
            new() { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "Marketing/Produkty", IndexedAt = now, ModifiedAt = now },
            new() { Id = 2, SharePointFileId = "sp-2", FileName = "b.jpg", FolderPath = "Marketing/Produkty", IndexedAt = now, ModifiedAt = now },
            new() { Id = 3, SharePointFileId = "sp-3", FileName = "c.jpg", FolderPath = "Vyrobky/2025", IndexedAt = now, ModifiedAt = now },
        };
        _context.Photos.AddRange(photos);
        await _context.SaveChangesAsync();

        // Seed 2 tags
        var tags = new List<Tag>
        {
            new() { Id = 100, Name = "featured" },
            new() { Id = 101, Name = "hero" },
        };
        _context.PhotobankTags.AddRange(tags);
        await _context.SaveChangesAsync();

        // Seed photo-tag relationships
        var photoTags = new List<PhotoTag>
        {
            new() { PhotoId = 1, TagId = 100, Source = PhotoTagSource.Manual, CreatedAt = now },
            new() { PhotoId = 3, TagId = 101, Source = PhotoTagSource.Manual, CreatedAt = now },
        };
        _context.PhotoTags.AddRange(photoTags);
        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task CountFilteredPhotosAsync_searchOnly_returnsSameCountAsGetPhotosAsync()
    {
        var ct = CancellationToken.None;

        // Arrange: Get expected count from GetPhotosAsync
        var (items, expectedTotal) = await _repository.GetPhotosAsync(
            null, "Produkty", false, false, 1, 48, ct);

        // Act: Call CountFilteredPhotosAsync with same parameters
        var actual = await _repository.CountFilteredPhotosAsync(null, "Produkty", ct);

        // Assert: Counts should match and equal 2
        actual.Should().Be(expectedTotal).And.Be(2);
    }

    [Fact]
    public async Task CountFilteredPhotosAsync_withTag_returnsSameCountAsGetPhotosAsync()
    {
        var ct = CancellationToken.None;

        // Arrange: Get expected count from GetPhotosAsync
        var (items, expectedTotal) = await _repository.GetPhotosAsync(
            new List<string> { "featured" }, null, false, false, 1, 48, ct);

        // Act: Call CountFilteredPhotosAsync with same parameters
        var actual = await _repository.CountFilteredPhotosAsync(
            new List<string> { "featured" }, null, ct);

        // Assert: Counts should match and equal 1
        actual.Should().Be(expectedTotal).And.Be(1);
    }

    [Fact]
    public async Task GetFilteredPhotoIdsMissingTagAsync_returnsOnlyPhotosLackingTheTargetTag()
    {
        var ct = CancellationToken.None;

        // Act: Get photo IDs with "Produkty" search that are missing tag 101 ("hero")
        var ids = await _repository.GetFilteredPhotoIdsMissingTagAsync(
            null, "Produkty", tagId: 101, ct);

        // Assert: Should return photos 1 and 2 (both in Marketing/Produkty; neither has tag 101)
        ids.Should().BeEquivalentTo(new[] { 1, 2 });
    }
}
