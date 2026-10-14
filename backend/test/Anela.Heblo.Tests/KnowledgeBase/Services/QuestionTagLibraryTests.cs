using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Domain.Features.Rag;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class QuestionTagLibraryTests
{
    private readonly Mock<IRagInteractionLogRepository> _repository = new();
    private readonly Mock<ILogger<QuestionTagLibrary>> _logger = new();

    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    private QuestionTagLibrary Create() => new(_repository.Object, _cache, _logger.Object);

    private static RagQueryExpansionConfig Config(string prompt) => new(Enabled: true, Model: "m", Prompt: prompt);

    private void SetupCounts(params RagTagCount[] counts) =>
        _repository
            .Setup(r => r.GetTagCountsAsync(RagFeature.KnowledgeBase, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(counts);

    [Fact]
    public async Task BuildExpansionConfigAsync_SubstitutesTagsOrderedByCountThenName()
    {
        // Arrange
        SetupCounts(
            new RagTagCount(RagTagKind.Problem, "růžovka", 1),
            new RagTagCount(RagTagKind.Problem, "akné", 3),
            new RagTagCount(RagTagKind.Problem, "ekzém", 1),
            new RagTagCount(RagTagKind.Product, "hořčíkové máslo", 2),
            new RagTagCount(RagTagKind.RecommendedProduct, "MAS009", 9));

        // Act
        var result = await Create().BuildExpansionConfigAsync(Config("A\n{EXISTING_TAGS}\nB"), CancellationToken.None);

        // Assert
        result.Prompt.Should().Be("A\nProblémy: akné, ekzém, růžovka\nProdukty: hořčíkové máslo\nB");
    }

    [Fact]
    public async Task BuildExpansionConfigAsync_EmptyLibrary_UsesPlaceholderText()
    {
        // Arrange
        SetupCounts();

        // Act
        var result = await Create().BuildExpansionConfigAsync(Config("{EXISTING_TAGS}"), CancellationToken.None);

        // Assert
        result.Prompt.Should().Be("Problémy: (zatím žádné)\nProdukty: (zatím žádné)");
    }

    [Fact]
    public async Task BuildExpansionConfigAsync_CapsEachKindAt200()
    {
        // Arrange
        var counts = Enumerable.Range(0, 250)
            .Select(i => new RagTagCount(RagTagKind.Problem, $"tag{i:D3}", 250 - i))
            .ToArray();
        SetupCounts(counts);

        // Act
        var result = await Create().BuildExpansionConfigAsync(Config("{EXISTING_TAGS}"), CancellationToken.None);

        // Assert
        var problems = result.Prompt.Split('\n')[0]["Problémy: ".Length..].Split(", ");
        problems.Should().HaveCount(QuestionTagLibrary.MaxLibraryTagsPerKind);
        problems.Should().Contain("tag000").And.NotContain("tag200");
    }

    [Fact]
    public async Task BuildExpansionConfigAsync_PromptWithoutPlaceholder_ReturnedUnchangedWithoutQuery()
    {
        // Arrange
        var config = Config("Plain prompt");

        // Act
        var result = await Create().BuildExpansionConfigAsync(config, CancellationToken.None);

        // Assert
        result.Should().Be(config);
        _repository.Verify(
            r => r.GetTagCountsAsync(It.IsAny<RagFeature>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task BuildExpansionConfigAsync_RepositoryThrows_FallsBackToEmptyLibraryAndLogsError()
    {
        // Arrange
        _repository
            .Setup(r => r.GetTagCountsAsync(RagFeature.KnowledgeBase, null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("db"));

        // Act
        var result = await Create().BuildExpansionConfigAsync(Config("{EXISTING_TAGS}"), CancellationToken.None);

        // Assert
        result.Prompt.Should().Be("Problémy: (zatím žádné)\nProdukty: (zatím žádné)");
        _logger.Verify(
            l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task BuildExpansionConfigAsync_CallerCancelled_Rethrows()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _repository
            .Setup(r => r.GetTagCountsAsync(RagFeature.KnowledgeBase, null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        // Act
        var act = () => Create().BuildExpansionConfigAsync(Config("{EXISTING_TAGS}"), cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task BuildExpansionConfigAsync_SecondCall_ServedFromCacheWithoutQuery()
    {
        // Arrange
        SetupCounts(new RagTagCount(RagTagKind.Problem, "akné", 1));

        // Act
        await Create().BuildExpansionConfigAsync(Config("{EXISTING_TAGS}"), CancellationToken.None);
        var result = await Create().BuildExpansionConfigAsync(Config("{EXISTING_TAGS}"), CancellationToken.None);

        // Assert
        result.Prompt.Should().StartWith("Problémy: akné");
        _repository.Verify(
            r => r.GetTagCountsAsync(RagFeature.KnowledgeBase, null, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task BuildExpansionConfigAsync_FailedLoad_IsNotCached()
    {
        // Arrange
        _repository
            .SetupSequence(r => r.GetTagCountsAsync(RagFeature.KnowledgeBase, null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("db"))
            .ReturnsAsync([new RagTagCount(RagTagKind.Problem, "akné", 1)]);

        // Act
        await Create().BuildExpansionConfigAsync(Config("{EXISTING_TAGS}"), CancellationToken.None);
        var result = await Create().BuildExpansionConfigAsync(Config("{EXISTING_TAGS}"), CancellationToken.None);

        // Assert
        result.Prompt.Should().StartWith("Problémy: akné");
    }
}
