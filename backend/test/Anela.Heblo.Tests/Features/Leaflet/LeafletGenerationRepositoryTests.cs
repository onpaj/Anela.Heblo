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

    public void Dispose()
    {
        _context.Dispose();
    }
}
