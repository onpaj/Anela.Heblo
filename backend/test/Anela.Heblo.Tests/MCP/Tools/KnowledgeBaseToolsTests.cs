using System.Text.Json;
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using MediatR;
using ModelContextProtocol;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class KnowledgeBaseToolsTests
{
    private readonly Mock<IMediator> _mediator = new();

    [Fact]
    public async Task SearchKnowledgeBase_ShouldMapParametersCorrectly()
    {
        var expected = new SearchDocumentsResponse
        {
            Chunks = [new ChunkResult { Content = "Test chunk", Score = 0.9, SourceFilename = "doc.pdf" }]
        };

        _mediator
            .Setup(m => m.Send(
                It.Is<SearchDocumentsRequest>(r => r.Query == "phenoxyethanol" && r.TopK == 3),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tools = new KnowledgeBaseTools(_mediator.Object);
        var result = await tools.SearchKnowledgeBase("phenoxyethanol", 3);

        var deserialized = JsonSerializer.Deserialize<SearchDocumentsResponse>(result);
        Assert.NotNull(deserialized);
        Assert.Single(deserialized!.Chunks);
        Assert.Equal("Test chunk", deserialized.Chunks[0].Content);
    }

    [Fact]
    public async Task AskKnowledgeBase_ShouldMapParametersCorrectly()
    {
        var expected = new AskQuestionResponse
        {
            Answer = "The max is 1.0%.",
            Sources = [new SourceReference { Filename = "EU_reg.pdf", Score = 0.95 }]
        };

        _mediator
            .Setup(m => m.Send(
                It.Is<AskQuestionRequest>(r => r.Question == "Max phenoxyethanol?"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tools = new KnowledgeBaseTools(_mediator.Object);
        var result = await tools.AskKnowledgeBase("Max phenoxyethanol?");

        var deserialized = JsonSerializer.Deserialize<AskQuestionResponse>(result);
        Assert.NotNull(deserialized);
        Assert.Equal("The max is 1.0%.", deserialized!.Answer);
        Assert.Single(deserialized.Sources);
    }

    [Fact]
    public async Task SearchKnowledgeBase_ShouldThrowMcpException_WhenMediatorThrows()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB error"));

        var tools = new KnowledgeBaseTools(_mediator.Object);

        await Assert.ThrowsAsync<McpException>(() =>
            tools.SearchKnowledgeBase("query"));
    }
}
