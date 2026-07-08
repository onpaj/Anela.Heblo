using Anela.Heblo.Application.Features.Smartsupp.UseCases.GetDraftReplyFeedbackList;
using Anela.Heblo.Application.Shared.Users;
using Anela.Heblo.Domain.Features.Rag;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class GetDraftReplyFeedbackListHandlerTests
{
    private readonly Mock<IRagInteractionLogRepository> _repository = new();
    private readonly Mock<IUserDisplayNameResolver> _userDisplayNameResolver = new();

    public GetDraftReplyFeedbackListHandlerTests()
    {
        _userDisplayNameResolver
            .Setup(r => r.ResolveAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>());
    }

    private GetDraftReplyFeedbackListHandler CreateHandler() =>
        new(_repository.Object, _userDisplayNameResolver.Object);

    private void SetupStats() =>
        _repository
            .Setup(r => r.GetFeedbackStatsAsync(It.IsAny<RagFeature?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagFeedbackAggregateStats { TotalQuestions = 1, TotalWithFeedback = 1 });

    [Fact]
    public async Task Handle_FiltersBySmartsuppDraftReplyFeature()
    {
        RagFeature? capturedFeature = null;
        _repository
            .Setup(r => r.GetFeedbackLogsPagedAsync(
                It.IsAny<RagFeature?>(), It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<RagFeature?, bool?, string?, string, bool, int, int, CancellationToken>(
                (feature, _, _, _, _, _, _, _) => capturedFeature = feature)
            .ReturnsAsync((new List<RagInteractionLog>(), 0));
        SetupStats();

        await CreateHandler().Handle(new GetDraftReplyFeedbackListRequest(), default);

        capturedFeature.Should().Be(RagFeature.SmartsuppDraftReply);
    }

    [Fact]
    public async Task Handle_MapsSmartsuppFieldsAndChunks()
    {
        var log = new RagInteractionLog
        {
            Id = Guid.NewGuid(),
            Feature = RagFeature.SmartsuppDraftReply,
            Question = "Kdy dorazí balík?",
            Answer = "Balík odešleme do 24 hodin.",
            ExpandedQuery = "doprava balík dodání",
            SystemPrompt = "System prompt text",
            RetrievedChunksJson = """[{"ChunkId":"11111111-1111-1111-1111-111111111111","DocumentId":"22222222-2222-2222-2222-222222222222","Filename":"doprava.pdf","SourcePath":"/doprava.pdf","Content":"Balíky odesíláme do 24 hodin.","Score":0.91}]""",
            ConversationId = "conv-1",
            Topic = "Doprava",
            SentAnswer = "Balík odešleme ještě dnes.",
            WasEdited = true,
            SentAt = DateTimeOffset.UtcNow,
            TopK = 5,
            SourceCount = 1,
            PrecisionScore = 4,
            StyleScore = 5,
        };
        _repository
            .Setup(r => r.GetFeedbackLogsPagedAsync(
                It.IsAny<RagFeature?>(), It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<RagInteractionLog> { log }, 1));
        SetupStats();

        var result = await CreateHandler().Handle(new GetDraftReplyFeedbackListRequest(), default);

        result.Success.Should().BeTrue();
        var dto = result.Logs.Should().ContainSingle().Subject;
        dto.Feature.Should().Be(RagFeature.SmartsuppDraftReply);
        dto.Topic.Should().Be("Doprava");
        dto.ConversationId.Should().Be("conv-1");
        dto.SentAnswer.Should().Be("Balík odešleme ještě dnes.");
        dto.WasEdited.Should().BeTrue();
        dto.ExpandedQuery.Should().Be("doprava balík dodání");
        dto.SystemPrompt.Should().Be("System prompt text");
        dto.RetrievedChunks.Should().ContainSingle();
        dto.RetrievedChunks[0].Filename.Should().Be("doprava.pdf");
        dto.RetrievedChunks[0].Score.Should().Be(0.91);
        dto.RetrievedChunks[0].Content.Should().Be("Balíky odesíláme do 24 hodin.");
        dto.HasFeedback.Should().BeTrue();
    }
}
