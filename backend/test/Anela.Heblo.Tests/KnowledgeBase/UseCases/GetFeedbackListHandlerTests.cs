using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetFeedbackList;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.UseCases;

public class GetFeedbackListHandlerTests
{
    private readonly Mock<IKnowledgeBaseRepository> _repository = new();

    private static KnowledgeBaseQuestionLog MakeLog(
        bool hasFeedback = false,
        int? precisionScore = null,
        int? styleScore = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Question = "What is the return policy?",
            Answer = "You can return items within 30 days.",
            TopK = 5,
            SourceCount = 3,
            DurationMs = 450,
            CreatedAt = DateTimeOffset.UtcNow,
            UserId = hasFeedback ? "user-1" : null,
            PrecisionScore = hasFeedback ? (precisionScore ?? 4) : null,
            StyleScore = hasFeedback ? (styleScore ?? 3) : null,
        };

    private static FeedbackAggregateStats DefaultStats() =>
        new()
        {
            TotalQuestions = 10,
            TotalWithFeedback = 5,
            AvgPrecisionScore = 3.8,
            AvgStyleScore = 4.1,
        };

    private void SetupRepository(
        List<KnowledgeBaseQuestionLog> logs,
        int? totalCount = null,
        FeedbackAggregateStats? stats = null)
    {
        _repository
            .Setup(r => r.GetFeedbackLogsPagedAsync(
                It.IsAny<bool?>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((logs, totalCount ?? logs.Count));

        _repository
            .Setup(r => r.GetFeedbackStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats ?? DefaultStats());
    }

    [Fact]
    public async Task Handle_ReturnsMappedLogs()
    {
        var log = MakeLog(hasFeedback: true);
        SetupRepository([log]);

        var handler = new GetFeedbackListHandler(_repository.Object);
        var result = await handler.Handle(new GetFeedbackListRequest(), default);

        Assert.True(result.Success);
        Assert.Single(result.Logs);
        var dto = result.Logs[0];
        Assert.Equal(log.Id, dto.Id);
        Assert.Equal(log.Question, dto.Question);
        Assert.Equal(log.Answer, dto.Answer);
        Assert.Equal(log.TopK, dto.TopK);
        Assert.Equal(log.SourceCount, dto.SourceCount);
        Assert.Equal(log.DurationMs, dto.DurationMs);
        Assert.Equal(log.UserId, dto.UserId);
        Assert.Equal(log.PrecisionScore, dto.PrecisionScore);
        Assert.Equal(log.StyleScore, dto.StyleScore);
        Assert.True(dto.HasFeedback);
    }

    [Fact]
    public async Task Handle_ReturnsStatsFromRepository()
    {
        var stats = new FeedbackAggregateStats
        {
            TotalQuestions = 42,
            TotalWithFeedback = 17,
            AvgPrecisionScore = 3.5,
            AvgStyleScore = 4.2,
        };
        SetupRepository([], stats: stats);

        var handler = new GetFeedbackListHandler(_repository.Object);
        var result = await handler.Handle(new GetFeedbackListRequest(), default);

        Assert.Equal(42, result.Stats.TotalQuestions);
        Assert.Equal(17, result.Stats.TotalWithFeedback);
        Assert.Equal(3.5, result.Stats.AvgPrecisionScore);
        Assert.Equal(4.2, result.Stats.AvgStyleScore);
    }

    [Fact]
    public async Task Handle_ReturnsPaginationMetadata()
    {
        var logs = Enumerable.Range(1, 10).Select(_ => MakeLog()).ToList();
        SetupRepository(logs, totalCount: 35);

        var handler = new GetFeedbackListHandler(_repository.Object);
        var result = await handler.Handle(
            new GetFeedbackListRequest { PageNumber = 2, PageSize = 10 },
            default);

        Assert.Equal(35, result.TotalCount);
        Assert.Equal(2, result.PageNumber);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(4, result.TotalPages); // ceil(35/10)
    }

    [Theory]
    [InlineData(5, 20)]    // not in allowed list → 20
    [InlineData(0, 20)]    // 0 not allowed → 20
    [InlineData(100, 20)]  // 100 not allowed → 20
    [InlineData(10, 10)]   // valid
    [InlineData(50, 50)]   // valid
    public async Task Handle_ClampsInvalidPageSize(int requested, int expected)
    {
        SetupRepository([]);

        var handler = new GetFeedbackListHandler(_repository.Object);
        var result = await handler.Handle(
            new GetFeedbackListRequest { PageSize = requested },
            default);

        Assert.Equal(expected, result.PageSize);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(2, 2)]
    public async Task Handle_ClampsPageNumberToMinimumOne(int requested, int expected)
    {
        SetupRepository([]);

        var handler = new GetFeedbackListHandler(_repository.Object);
        var result = await handler.Handle(
            new GetFeedbackListRequest { PageNumber = requested },
            default);

        Assert.Equal(expected, result.PageNumber);
    }

    [Theory]
    [InlineData("UnknownColumn", "CreatedAt")]
    [InlineData("", "CreatedAt")]
    [InlineData("CreatedAt", "CreatedAt")]
    [InlineData("PrecisionScore", "PrecisionScore")]
    [InlineData("StyleScore", "StyleScore")]
    public async Task Handle_FallsBackInvalidSortBy(string requested, string expected)
    {
        string? capturedSortBy = null;
        _repository
            .Setup(r => r.GetFeedbackLogsPagedAsync(
                It.IsAny<bool?>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<bool?, string?, string, bool, int, int, CancellationToken>(
                (_, _, sortBy, _, _, _, _) => capturedSortBy = sortBy)
            .ReturnsAsync((new List<KnowledgeBaseQuestionLog>(), 0));
        _repository
            .Setup(r => r.GetFeedbackStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DefaultStats());

        var handler = new GetFeedbackListHandler(_repository.Object);
        await handler.Handle(new GetFeedbackListRequest { SortBy = requested }, default);

        Assert.Equal(expected, capturedSortBy);
    }

    [Fact]
    public async Task Handle_PassesAllFiltersToRepository()
    {
        bool? capturedHasFeedback = null;
        string? capturedUserId = null;
        string? capturedSortBy = null;
        bool? capturedSortDesc = null;
        int? capturedPage = null;
        int? capturedPageSize = null;

        _repository
            .Setup(r => r.GetFeedbackLogsPagedAsync(
                It.IsAny<bool?>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<bool?, string?, string, bool, int, int, CancellationToken>(
                (hf, uid, sb, sd, pn, ps, _) =>
                {
                    capturedHasFeedback = hf;
                    capturedUserId = uid;
                    capturedSortBy = sb;
                    capturedSortDesc = sd;
                    capturedPage = pn;
                    capturedPageSize = ps;
                })
            .ReturnsAsync((new List<KnowledgeBaseQuestionLog>(), 0));
        _repository
            .Setup(r => r.GetFeedbackStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DefaultStats());

        var handler = new GetFeedbackListHandler(_repository.Object);
        await handler.Handle(new GetFeedbackListRequest
        {
            HasFeedback = true,
            UserId = "user-42",
            SortBy = "PrecisionScore",
            SortDescending = false,
            PageNumber = 3,
            PageSize = 50,
        }, default);

        Assert.True(capturedHasFeedback);
        Assert.Equal("user-42", capturedUserId);
        Assert.Equal("PrecisionScore", capturedSortBy);
        Assert.False(capturedSortDesc);
        Assert.Equal(3, capturedPage);
        Assert.Equal(50, capturedPageSize);
    }

    [Fact]
    public async Task Handle_TotalPagesRoundsUp()
    {
        SetupRepository([], totalCount: 25);

        var handler = new GetFeedbackListHandler(_repository.Object);
        var result = await handler.Handle(
            new GetFeedbackListRequest { PageSize = 10 },
            default);

        Assert.Equal(3, result.TotalPages);
    }

    [Fact]
    public async Task Handle_LogWithoutFeedbackHasHasFeedbackFalse()
    {
        var log = MakeLog(hasFeedback: false);
        SetupRepository([log]);

        var handler = new GetFeedbackListHandler(_repository.Object);
        var result = await handler.Handle(new GetFeedbackListRequest(), default);

        Assert.False(result.Logs[0].HasFeedback);
        Assert.Null(result.Logs[0].PrecisionScore);
        Assert.Null(result.Logs[0].StyleScore);
    }
}
