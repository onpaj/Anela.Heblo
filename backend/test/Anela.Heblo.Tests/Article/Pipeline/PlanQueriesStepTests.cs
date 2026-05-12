using Anela.Heblo.Application.Features.Article;
using Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;
using Anela.Heblo.Domain.Features.Article;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Tests.Article.Pipeline;

public class PlanQueriesStepTests
{
    private readonly Mock<IChatClient> _chat = new();
    private readonly ArticleOptions _options = new();

    private static PipelineStepRecorder CreateNoOpRecorder()
    {
        var repo = new Mock<IArticleRepository>();
        repo.Setup(r => r.AddStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return new PipelineStepRecorder(repo.Object);
    }

    private PlanQueriesStep CreateStep() =>
        new(_chat.Object, Options.Create(_options), NullLogger<PlanQueriesStep>.Instance, CreateNoOpRecorder());

    private static ArticlePipelineContext CreateContext(string topic = "Sun protection") =>
        new() { Article = new DomainArticle { Topic = topic } };

    private void SetupChatResponse(string text) =>
        _chat
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]));

    [Fact]
    public async Task ExecuteAsync_ValidJsonResponse_SetsQueriesOnContext()
    {
        SetupChatResponse("""{"queries":["sun cream","spf review","best sunscreens 2025"]}""");
        var context = CreateContext();

        await CreateStep().ExecuteAsync(context, default);

        context.SearchQueries.Should().BeEquivalentTo(
            new[] { "sun cream", "spf review", "best sunscreens 2025" });
    }

    [Fact]
    public async Task ExecuteAsync_GarbageResponse_FallsBackToThreeQueries()
    {
        SetupChatResponse("This is not JSON at all.");
        var context = CreateContext("Skin care basics");

        await CreateStep().ExecuteAsync(context, default);

        context.SearchQueries.Should().HaveCount(3);
        context.SearchQueries[0].Should().Be("Skin care basics");
        context.SearchQueries[1].Should().Be("Skin care basics statistiky");
        context.SearchQueries[2].Should().Be("Skin care basics recenze");
    }

    [Fact]
    public async Task ExecuteAsync_TenQueriesReturned_StoresOnlyEight()
    {
        var manyQueries = string.Join(",", Enumerable.Range(1, 10).Select(i => $"\"q{i}\""));
        SetupChatResponse($"{{\"queries\":[{manyQueries}]}}");
        var context = CreateContext();

        await CreateStep().ExecuteAsync(context, default);

        context.SearchQueries.Should().HaveCount(8);
        context.SearchQueries.Should().Equal("q1", "q2", "q3", "q4", "q5", "q6", "q7", "q8");
    }
}
