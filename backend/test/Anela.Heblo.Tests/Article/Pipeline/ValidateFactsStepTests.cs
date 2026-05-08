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

public class ValidateFactsStepTests
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

    private ValidateFactsStep CreateStep() =>
        new(_chat.Object, Options.Create(_options), NullLogger<ValidateFactsStep>.Instance, CreateNoOpRecorder());

    private static ArticlePipelineContext CreateContext(List<AggregatedFact> facts) =>
        new()
        {
            Article = new DomainArticle { Topic = "Topic" },
            Facts = facts
        };

    private void SetupChatResponse(string text) =>
        _chat
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]));

    [Fact]
    public async Task ExecuteAsync_LlmReturnsNotes_AnnotatesFactsWithValidationNotes()
    {
        SetupChatResponse(
            """
            {"validated_facts":[
              {"fact":"Claim A","note":"Solid","reliable":true},
              {"fact":"Claim B","note":"Outdated","reliable":false}
            ]}
            """);

        var facts = new List<AggregatedFact>
        {
            new() { Claim = "Claim A", Confidence = 0.9 },
            new() { Claim = "Claim B", Confidence = 0.4 }
        };
        var context = CreateContext(facts);

        await CreateStep().ExecuteAsync(context, default);

        context.Facts.Should().HaveCount(2);
        context.Facts[0].ValidationNote.Should().Be("Solid");
        context.Facts[1].ValidationNote.Should().Be("Outdated");
    }

    [Fact]
    public async Task ExecuteAsync_LlmReturnsFewerNotesThanFacts_ExtraFactsPassThroughUnchanged()
    {
        SetupChatResponse(
            """
            {"validated_facts":[
              {"fact":"Claim A","note":"Verified","reliable":true}
            ]}
            """);

        var facts = new List<AggregatedFact>
        {
            new() { Claim = "Claim A", Confidence = 0.9 },
            new() { Claim = "Claim B", Confidence = 0.7 },
            new() { Claim = "Claim C", Confidence = 0.5 }
        };
        var context = CreateContext(facts);

        await CreateStep().ExecuteAsync(context, default);

        context.Facts.Should().HaveCount(3);
        context.Facts[0].ValidationNote.Should().Be("Verified");
        context.Facts[1].ValidationNote.Should().BeNull();
        context.Facts[2].ValidationNote.Should().BeNull();
        context.Facts[1].Claim.Should().Be("Claim B");
        context.Facts[2].Claim.Should().Be("Claim C");
    }

    [Fact]
    public async Task ExecuteAsync_NoFacts_DoesNotCallChatClient()
    {
        var context = CreateContext(facts: []);

        await CreateStep().ExecuteAsync(context, default);

        _chat.Verify(
            c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
