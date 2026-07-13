using Anela.Heblo.Domain.Features.Leaflet;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Leaflet;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet;

public class LeafletGenerationRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly LeafletGenerationRepository _repository;

    public LeafletGenerationRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"LeafletGenerationRepositoryTests_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new LeafletGenerationRepository(_context);
    }

    [Fact]
    public async Task UpdateFeedbackAsync_returns_NotFound_when_generation_missing()
    {
        // Act
        var result = await _repository.UpdateFeedbackAsync(
            Guid.NewGuid(), precisionScore: 4, styleScore: 5, comment: "x", default);

        // Assert
        Assert.Equal(UpdateFeedbackResult.NotFound, result);
    }

    [Fact]
    public async Task UpdateFeedbackAsync_returns_AlreadySubmitted_when_score_already_present()
    {
        // Arrange
        var generation = new LeafletGeneration
        {
            Id = Guid.NewGuid(),
            Topic = "X",
            UserId = "u1",
            PrecisionScore = 3,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _context.LeafletGenerations.Add(generation);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.UpdateFeedbackAsync(
            generation.Id, precisionScore: 4, styleScore: 5, comment: "x", default);

        // Assert
        Assert.Equal(UpdateFeedbackResult.AlreadySubmitted, result);
    }

    [Fact]
    public async Task UpdateFeedbackAsync_persists_scores_and_comment_then_returns_Updated()
    {
        // Arrange
        var generation = new LeafletGeneration
        {
            Id = Guid.NewGuid(),
            Topic = "X",
            UserId = "u1",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _context.LeafletGenerations.Add(generation);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Act
        var result = await _repository.UpdateFeedbackAsync(
            generation.Id, precisionScore: 4, styleScore: 5, comment: "great", default);

        // Assert
        Assert.Equal(UpdateFeedbackResult.Updated, result);

        _context.ChangeTracker.Clear();
        var reloaded = await _context.LeafletGenerations.FindAsync(generation.Id);
        Assert.Equal(4, reloaded!.PrecisionScore);
        Assert.Equal(5, reloaded.StyleScore);
        Assert.Equal("great", reloaded.FeedbackComment);
    }

    [Fact]
    public async Task GetGenerationStatsAsync_returns_zeroed_stats_when_table_is_empty()
    {
        // Act
        var stats = await _repository.GetGenerationStatsAsync(default);

        // Assert
        Assert.Equal(0, stats.TotalGenerations);
        Assert.Equal(0, stats.TotalWithFeedback);
        Assert.Null(stats.AvgPrecisionScore);
        Assert.Null(stats.AvgStyleScore);
    }

    [Fact]
    public async Task GetGenerationStatsAsync_returns_zero_with_feedback_and_null_averages_when_no_rows_have_feedback()
    {
        // Arrange
        _context.LeafletGenerations.AddRange(
            new LeafletGeneration
            {
                Id = Guid.NewGuid(),
                Topic = "X",
                UserId = "u1",
                CreatedAt = DateTimeOffset.UtcNow,
                PrecisionScore = null,
                StyleScore = null,
            },
            new LeafletGeneration
            {
                Id = Guid.NewGuid(),
                Topic = "X",
                UserId = "u1",
                CreatedAt = DateTimeOffset.UtcNow,
                PrecisionScore = null,
                StyleScore = null,
            },
            new LeafletGeneration
            {
                Id = Guid.NewGuid(),
                Topic = "X",
                UserId = "u1",
                CreatedAt = DateTimeOffset.UtcNow,
                PrecisionScore = null,
                StyleScore = null,
            });
        await _context.SaveChangesAsync();

        // Act
        var stats = await _repository.GetGenerationStatsAsync(default);

        // Assert
        Assert.Equal(3, stats.TotalGenerations);
        Assert.Equal(0, stats.TotalWithFeedback);
        Assert.Null(stats.AvgPrecisionScore);
        Assert.Null(stats.AvgStyleScore);
    }

    [Fact]
    public async Task GetGenerationStatsAsync_computes_correct_totals_and_averages_for_mixed_feedback_rows()
    {
        // Arrange
        _context.LeafletGenerations.AddRange(
            new LeafletGeneration
            {
                Id = Guid.NewGuid(),
                Topic = "X",
                UserId = "u1",
                CreatedAt = DateTimeOffset.UtcNow,
                PrecisionScore = 4,
                StyleScore = 5,
            },
            new LeafletGeneration
            {
                Id = Guid.NewGuid(),
                Topic = "X",
                UserId = "u1",
                CreatedAt = DateTimeOffset.UtcNow,
                PrecisionScore = 2,
                StyleScore = 3,
            },
            new LeafletGeneration
            {
                Id = Guid.NewGuid(),
                Topic = "X",
                UserId = "u1",
                CreatedAt = DateTimeOffset.UtcNow,
                PrecisionScore = null,
                StyleScore = null,
            },
            new LeafletGeneration
            {
                Id = Guid.NewGuid(),
                Topic = "X",
                UserId = "u1",
                CreatedAt = DateTimeOffset.UtcNow,
                PrecisionScore = 5,
                StyleScore = null,
            },
            new LeafletGeneration
            {
                Id = Guid.NewGuid(),
                Topic = "X",
                UserId = "u1",
                CreatedAt = DateTimeOffset.UtcNow,
                PrecisionScore = null,
                StyleScore = 1,
            });
        await _context.SaveChangesAsync();

        // Act
        var stats = await _repository.GetGenerationStatsAsync(default);

        // Assert
        Assert.Equal(5, stats.TotalGenerations);
        Assert.Equal(4, stats.TotalWithFeedback);
        Assert.Equal((4 + 2 + 5) / 3.0, stats.AvgPrecisionScore);
        Assert.Equal((5 + 3 + 1) / 3.0, stats.AvgStyleScore);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
