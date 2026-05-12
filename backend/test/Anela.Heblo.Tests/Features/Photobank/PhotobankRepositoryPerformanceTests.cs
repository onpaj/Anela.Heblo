using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank;
using Anela.Heblo.Persistence;
using DotNet.Testcontainers.Configurations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

[Trait("Category", "Integration")]
public class PhotobankRepositoryPerformanceTests : IAsyncLifetime
{
    private const int PhotoCount = 10_000;
    private const int TagCount = 20;
    private const int PhotoTagsPerPhoto = 3;
    private const int MaxAllowedMilliseconds = 1500;

    static PhotobankRepositoryPerformanceTests()
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

        await SeedLargeDatasetAsync();

        await RunAnalyzeAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _container.DisposeAsync();
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

            CREATE INDEX IF NOT EXISTS "IX_Photos_ModifiedAt_Id" ON public."Photos" ("ModifiedAt" DESC, "Id" DESC);

            CREATE INDEX IF NOT EXISTS "IX_Photos_PathTrgm" ON public."Photos" USING GIN ((LOWER("FolderPath" || '/' || "FileName")) gin_trgm_ops);

            CREATE INDEX IF NOT EXISTS "IX_PhotoTags_TagId_PhotoId" ON public."PhotoTags" ("TagId", "PhotoId");
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedLargeDatasetAsync()
    {
        await using var conn = new NpgsqlConnection(_container.GetConnectionString());
        await conn.OpenAsync();

        await using (var cmd = conn.CreateCommand())
        {
            var values = string.Join(",",
                Enumerable.Range(1, TagCount).Select(i => $"({i}, 'tag-{i:D3}')"));
            cmd.CommandText = $"""INSERT INTO public."PhotobankTags" ("Id", "Name") VALUES {values};""";
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"""
                INSERT INTO public."Photos" ("Id", "SharePointFileId", "FileName", "FolderPath", "IndexedAt", "ModifiedAt")
                SELECT
                    g,
                    'sp-' || g,
                    'photo-' || g || '.jpg',
                    CASE (g % 10)
                        WHEN 0 THEN 'Marketing/Produkty/Ruze'
                        WHEN 1 THEN 'Marketing/Web'
                        WHEN 2 THEN 'Vyrobky/2025'
                        WHEN 3 THEN 'Marketing/Banners'
                        WHEN 4 THEN 'Reports/Q1'
                        WHEN 5 THEN 'Reports/Q2'
                        WHEN 6 THEN 'Archive/2024'
                        WHEN 7 THEN 'Archive/2025'
                        WHEN 8 THEN 'Marketing/Produkty/Levandule'
                        ELSE         'Misc/Other'
                    END,
                    NOW() - (g || ' minutes')::interval,
                    NOW() - (g || ' minutes')::interval
                FROM generate_series(1, {PhotoCount}) AS g;
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"""
                INSERT INTO public."PhotoTags" ("PhotoId", "TagId", "Source", "CreatedAt")
                SELECT
                    p,
                    ((p + t) % {TagCount}) + 1,
                    'Manual',
                    NOW()
                FROM generate_series(1, {PhotoCount}) AS p
                CROSS JOIN generate_series(0, {PhotoTagsPerPhoto - 1}) AS t
                ON CONFLICT DO NOTHING;
                """;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task RunAnalyzeAsync()
    {
        await using var conn = new NpgsqlConnection(_container.GetConnectionString());
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """ANALYZE public."Photos"; ANALYZE public."PhotoTags"; ANALYZE public."PhotobankTags";""";
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task GetPhotosAsync_defaultRequest_completesWithinBudget()
    {
        // Warm up (discard timing)
        await _repository.GetPhotosAsync(null, null, false, false, 1, 48, CancellationToken.None);

        var sw = Stopwatch.StartNew();
        var (items, total) = await _repository.GetPhotosAsync(
            null, null, false, false, 1, 48, CancellationToken.None);
        sw.Stop();

        total.Should().Be(PhotoCount);
        items.Should().HaveCount(48);
        sw.ElapsedMilliseconds.Should().BeLessThan(MaxAllowedMilliseconds,
            $"default page query must complete in < {MaxAllowedMilliseconds}ms with {PhotoCount} photos, but took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task GetPhotosAsync_searchAndTags_completesWithinBudget()
    {
        // Warm up (discard timing)
        await _repository.GetPhotosAsync(
            new System.Collections.Generic.List<string> { "tag-001" }, "produkty",
            false, false, 1, 48, CancellationToken.None);

        var sw = Stopwatch.StartNew();
        var (items, total) = await _repository.GetPhotosAsync(
            new System.Collections.Generic.List<string> { "tag-001" }, "produkty",
            false, false, 1, 48, CancellationToken.None);
        sw.Stop();

        total.Should().BeGreaterThan(0);
        items.Should().NotBeEmpty();
        sw.ElapsedMilliseconds.Should().BeLessThan(MaxAllowedMilliseconds,
            $"filtered page query must complete in < {MaxAllowedMilliseconds}ms with {PhotoCount} photos, but took {sw.ElapsedMilliseconds}ms");
    }
}
