using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetQuestionTagStats;
using Anela.Heblo.Domain.Features.Rag;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.UseCases;

public class GetQuestionTagStatsHandlerTests
{
    private readonly Mock<IRagInteractionLogRepository> _repository = new();
    private readonly Mock<IProductEnrichmentCache> _cache = new();

    private GetQuestionTagStatsHandler Create() => new(_repository.Object, _cache.Object);

    private void SetupCache(params (string code, string name)[] products) =>
        _cache
            .Setup(c => c.GetProductLookupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products.ToDictionary(
                p => p.code,
                p => new ProductEnrichmentEntry { ProductCode = p.code, ProductName = p.name }));

    [Fact]
    public async Task Handle_GroupsByKindOrderedByCountThenTag()
    {
        // Arrange
        _repository
            .Setup(r => r.GetTagCountsAsync(RagFeature.KnowledgeBase, It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new RagTagCount(RagTagKind.Problem, "růžovka", 2),
                new RagTagCount(RagTagKind.Problem, "akné", 5),
                new RagTagCount(RagTagKind.Problem, "ekzém", 2),
                new RagTagCount(RagTagKind.Product, "hořčíkové máslo", 1),
            });
        SetupCache();

        // Act
        var result = await Create().Handle(new GetQuestionTagStatsRequest(), default);

        // Assert
        result.Success.Should().BeTrue();
        result.ProblemTags.Select(t => (t.Tag, t.Count)).Should().Equal(("akné", 5), ("ekzém", 2), ("růžovka", 2));
        result.ProblemTags.Should().OnlyContain(t => t.Label == t.Tag);
        result.ProductTags.Select(t => t.Tag).Should().Equal("hořčíkové máslo");
        result.RecommendedProducts.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_RecommendedProducts_ResolvesNameAndFallsBackToCode()
    {
        // Arrange
        _repository
            .Setup(r => r.GetTagCountsAsync(RagFeature.KnowledgeBase, It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new RagTagCount(RagTagKind.RecommendedProduct, "MAS009", 3),
                new RagTagCount(RagTagKind.RecommendedProduct, "GONE01", 1),
            });
        SetupCache(("MAS009", "Klidné nožky balzám"));

        // Act
        var result = await Create().Handle(new GetQuestionTagStatsRequest(), default);

        // Assert
        result.RecommendedProducts.Select(p => (p.Tag, p.Label, p.Count))
            .Should().Equal(("MAS009", "Klidné nožky balzám", 3), ("GONE01", "GONE01", 1));
    }

    [Fact]
    public async Task Handle_ConvertsPragueDaysToHalfOpenUtcRange()
    {
        // Arrange
        _repository
            .Setup(r => r.GetTagCountsAsync(It.IsAny<RagFeature>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RagTagCount>());
        SetupCache();

        // Act
        await Create().Handle(new GetQuestionTagStatsRequest { DateFrom = new DateOnly(2026, 10, 1), DateTo = new DateOnly(2026, 10, 31) }, default);

        // Assert
        _repository.Verify(r => r.GetTagCountsAsync(
            RagFeature.KnowledgeBase,
            new DateTimeOffset(2026, 9, 30, 22, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 31, 23, 0, 0, TimeSpan.Zero),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NoRecommended_DoesNotLoadProductCatalog()
    {
        // Arrange
        _repository
            .Setup(r => r.GetTagCountsAsync(It.IsAny<RagFeature>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new RagTagCount(RagTagKind.Problem, "akné", 1) });

        // Act
        await Create().Handle(new GetQuestionTagStatsRequest(), default);

        // Assert
        _cache.Verify(c => c.GetProductLookupAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
