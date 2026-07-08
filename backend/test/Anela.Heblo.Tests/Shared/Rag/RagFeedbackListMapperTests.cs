using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Domain.Features.Rag;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Shared.Rag;

public class RagFeedbackListMapperTests
{
    private static RagInteractionLog LogWithChunks(string chunksJson) => new()
    {
        Id = Guid.NewGuid(),
        Feature = RagFeature.KnowledgeBase,
        Question = "q",
        Answer = "a",
        RetrievedChunksJson = chunksJson,
    };

    [Fact]
    public void ToSummary_ParsesRetrievedChunksFromJson()
    {
        var log = LogWithChunks("""[{"ChunkId":"11111111-1111-1111-1111-111111111111","Filename":"a.pdf","Content":"hello","Score":0.75}]""");

        var summary = RagFeedbackListMapper.ToSummary(log, userName: "Alice");

        summary.UserName.Should().Be("Alice");
        summary.RetrievedChunks.Should().ContainSingle();
        summary.RetrievedChunks[0].Filename.Should().Be("a.pdf");
        summary.RetrievedChunks[0].Content.Should().Be("hello");
        summary.RetrievedChunks[0].Score.Should().Be(0.75);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("not-json")]
    [InlineData("{\"bad\":true}")]
    public void ToSummary_ReturnsEmptyChunks_ForMissingOrMalformedJson(string json)
    {
        var summary = RagFeedbackListMapper.ToSummary(LogWithChunks(json), userName: null);

        summary.RetrievedChunks.Should().BeEmpty();
    }

    [Fact]
    public void ToStats_MapsAggregateStats()
    {
        var stats = new RagFeedbackAggregateStats
        {
            TotalQuestions = 12,
            TotalWithFeedback = 7,
            AvgPrecisionScore = 3.4,
            AvgStyleScore = 4.1,
        };

        var dto = RagFeedbackListMapper.ToStats(stats);

        dto.TotalQuestions.Should().Be(12);
        dto.TotalWithFeedback.Should().Be(7);
        dto.AvgPrecisionScore.Should().Be(3.4);
        dto.AvgStyleScore.Should().Be(4.1);
    }
}
