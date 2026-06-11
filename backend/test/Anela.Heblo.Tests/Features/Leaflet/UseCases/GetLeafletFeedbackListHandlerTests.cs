using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletFeedbackList;
using Anela.Heblo.Application.Shared.Users;
using Anela.Heblo.Domain.Features.Leaflet;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet.UseCases;

public class GetLeafletFeedbackListHandlerTests
{
    private readonly Mock<ILeafletGenerationRepository> _repo = new();
    private readonly Mock<IUserDisplayNameResolver> _userDisplayNameResolver = new();

    public GetLeafletFeedbackListHandlerTests()
    {
        _userDisplayNameResolver
            .Setup(r => r.ResolveAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>());
    }

    private static LeafletGeneration MakeGeneration(bool hasFeedback = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            Topic = "Retinol cream",
            Audience = "EndConsumer",
            Length = "Medium",
            FinalMarkdown = "# Content",
            KbSourceCount = 3,
            LeafletSourceCount = 1,
            DurationMs = 1200,
            CreatedAt = DateTimeOffset.UtcNow,
            UserId = "user-1",
            PrecisionScore = hasFeedback ? 4 : null,
            StyleScore = hasFeedback ? 5 : null,
        };

    private static LeafletFeedbackStats DefaultStats() =>
        new(TotalGenerations: 10, TotalWithFeedback: 4, AvgPrecisionScore: 3.8, AvgStyleScore: 4.1);

    private void SetupRepo(List<LeafletGeneration> items, int? total = null, LeafletFeedbackStats? stats = null)
    {
        _repo.Setup(r => r.GetGenerationsPagedAsync(
                It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((items, total ?? items.Count));
        _repo.Setup(r => r.GetGenerationStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats ?? DefaultStats());
    }

    private GetLeafletFeedbackListHandler CreateHandler() =>
        new(_repo.Object, _userDisplayNameResolver.Object);

    [Fact]
    public async Task Handle_ReturnsMappedGenerations()
    {
        var gen = MakeGeneration(hasFeedback: true);
        SetupRepo([gen]);

        var result = await CreateHandler().Handle(new GetLeafletFeedbackListRequest(), default);

        Assert.True(result.Success);
        Assert.Single(result.Items);
        var dto = result.Items[0];
        Assert.Equal(gen.Id, dto.Id);
        Assert.Equal(gen.Topic, dto.Topic);
        Assert.Equal(gen.Audience, dto.Audience);
        Assert.Equal(gen.Length, dto.Length);
        Assert.Equal(gen.KbSourceCount, dto.KbSourceCount);
        Assert.Equal(gen.LeafletSourceCount, dto.LeafletSourceCount);
        Assert.Equal(gen.PrecisionScore, dto.PrecisionScore);
        Assert.Equal(gen.StyleScore, dto.StyleScore);
        Assert.True(dto.HasFeedback);
    }

    [Fact]
    public async Task Handle_ResolvesUserNameFromResolver()
    {
        var gen = MakeGeneration(hasFeedback: true); // UserId = "user-1"
        SetupRepo([gen]);
        _userDisplayNameResolver
            .Setup(r => r.ResolveAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>
            {
                ["user-1"] = "Alice Example",
            });

        var result = await CreateHandler().Handle(new GetLeafletFeedbackListRequest(), default);

        Assert.Equal("Alice Example", result.Items[0].UserName);
        Assert.Equal("user-1", result.Items[0].UserId);
    }

    [Fact]
    public async Task Handle_ReturnsStatsFromRepository()
    {
        var stats = new LeafletFeedbackStats(42, 17, 3.5, 4.2);
        SetupRepo([], stats: stats);

        var result = await CreateHandler().Handle(new GetLeafletFeedbackListRequest(), default);

        Assert.Equal(42, result.Stats.TotalGenerations);
        Assert.Equal(17, result.Stats.TotalWithFeedback);
        Assert.Equal(3.5, result.Stats.AvgPrecisionScore);
        Assert.Equal(4.2, result.Stats.AvgStyleScore);
    }

    [Fact]
    public async Task Handle_ReturnsPaginationMetadata()
    {
        var items = Enumerable.Range(1, 10).Select(_ => MakeGeneration()).ToList();
        SetupRepo(items, total: 35);

        var result = await CreateHandler().Handle(
            new GetLeafletFeedbackListRequest { PageNumber = 2, PageSize = 10 }, default);

        Assert.Equal(35, result.TotalCount);
        Assert.Equal(2, result.PageNumber);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(4, result.TotalPages);
    }

    [Theory]
    [InlineData(5, 20)]
    [InlineData(0, 20)]
    [InlineData(100, 20)]
    [InlineData(10, 10)]
    [InlineData(50, 50)]
    public async Task Handle_ClampsInvalidPageSize(int requested, int expected)
    {
        SetupRepo([]);

        var result = await CreateHandler().Handle(
            new GetLeafletFeedbackListRequest { PageSize = requested }, default);

        Assert.Equal(expected, result.PageSize);
    }

    [Theory]
    [InlineData("UnknownColumn", "CreatedAt")]
    [InlineData("", "CreatedAt")]
    [InlineData("CreatedAt", "CreatedAt")]
    [InlineData("PrecisionScore", "PrecisionScore")]
    [InlineData("StyleScore", "StyleScore")]
    public async Task Handle_FallsBackInvalidSortBy(string requested, string expected)
    {
        string? captured = null;
        _repo.Setup(r => r.GetGenerationsPagedAsync(
                It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<bool?, string?, string, bool, int, int, CancellationToken>(
                (_, _, sb, _, _, _, _) => captured = sb)
            .ReturnsAsync((new List<LeafletGeneration>(), 0));
        _repo.Setup(r => r.GetGenerationStatsAsync(default)).ReturnsAsync(DefaultStats());

        await CreateHandler().Handle(new GetLeafletFeedbackListRequest { SortBy = requested }, default);

        Assert.Equal(expected, captured);
    }

    [Fact]
    public async Task Handle_ItemWithoutFeedback_HasFeedbackIsFalse()
    {
        SetupRepo([MakeGeneration(hasFeedback: false)]);

        var result = await CreateHandler().Handle(new GetLeafletFeedbackListRequest(), default);

        Assert.False(result.Items[0].HasFeedback);
    }
}
