using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using MediatR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.UseCases;

public class AskQuestionHandlerTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IChatClient> _chatClient = new();

    private AskQuestionHandler CreateHandler() =>
        new(_mediator.Object, _chatClient.Object, Options.Create(new KnowledgeBaseOptions()));

    [Fact]
    public async Task Handle_ReturnsAnswerWithSources()
    {
        var searchResponse = new SearchDocumentsResponse
        {
            Chunks =
            [
                new ChunkResult
                {
                    ChunkId = Guid.NewGuid(),
                    DocumentId = Guid.NewGuid(),
                    Content = "Max phenoxyethanol 1.0% per EU regulation",
                    Score = 0.95,
                    SourceFilename = "EU_reg.pdf",
                    SourcePath = "/archived/EU_reg.pdf"
                }
            ]
        };

        _mediator
            .Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), default))
            .ReturnsAsync(searchResponse);

        var chatResponse = new ChatResponse([new ChatMessage(ChatRole.Assistant, "The maximum allowed concentration is 1.0%.")]);
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                default))
            .ReturnsAsync(chatResponse);

        var result = await CreateHandler().Handle(
            new AskQuestionRequest { Question = "Max phenoxyethanol?", TopK = 5 },
            default);

        Assert.Equal("The maximum allowed concentration is 1.0%.", result.Answer);
        Assert.Single(result.Sources);
        Assert.Equal("EU_reg.pdf", result.Sources[0].Filename);
    }

    [Fact]
    public async Task Handle_EmptyChunks_ReturnsFallbackAnswerWithNoSources()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), default))
            .ReturnsAsync(new SearchDocumentsResponse { Chunks = [], BelowThresholdCount = 3 });

        var result = await CreateHandler().Handle(
            new AskQuestionRequest { Question = "Co mi poradis na akne?", TopK = 5 },
            default);

        Assert.Equal("V dostupných dokumentech jsem nenašla relevantní informaci k vaší otázce.", result.Answer);
        Assert.Empty(result.Sources);
        _chatClient.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            default), Times.Never);
    }
}
