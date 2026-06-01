using System.Text.Json;
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using MediatR;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class KnowledgeBaseToolsTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<ILogger<KnowledgeBaseTools>> _logger = new();

    private KnowledgeBaseTools CreateTools() => new(_mediator.Object, _logger.Object);

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

        var result = await CreateTools().SearchKnowledgeBase("phenoxyethanol", 3);

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

        var result = await CreateTools().AskKnowledgeBase("Max phenoxyethanol?");

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

        await Assert.ThrowsAsync<McpException>(() =>
            CreateTools().SearchKnowledgeBase("query"));
    }

    [Fact]
    public async Task SearchKnowledgeBase_ShouldLogWarning_WhenMediatorThrows()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("stream error"));

        await Assert.ThrowsAsync<McpException>(() =>
            CreateTools().SearchKnowledgeBase("query"));

        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("SearchKnowledgeBase")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task AskKnowledgeBase_ShouldThrowMcpException_WhenMediatorThrows()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<AskQuestionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("AI error"));

        await Assert.ThrowsAsync<McpException>(() =>
            CreateTools().AskKnowledgeBase("question?"));
    }

    [Fact]
    public async Task AskKnowledgeBase_ShouldLogWarning_WhenMediatorThrows()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<AskQuestionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("AI error"));

        await Assert.ThrowsAsync<McpException>(() =>
            CreateTools().AskKnowledgeBase("question?"));

        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("AskKnowledgeBase")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
