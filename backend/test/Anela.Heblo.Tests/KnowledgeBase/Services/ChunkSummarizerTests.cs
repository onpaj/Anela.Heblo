using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class ChunkSummarizerTests
{
    private readonly Mock<IChatClient> _chatClient = new();

    private ChunkSummarizer Create(KnowledgeBaseOptions? options = null)
        => new(_chatClient.Object, Options.Create(options ?? new KnowledgeBaseOptions()));

    [Fact]
    public async Task SummarizeAsync_CallsChatClient_WithPromptContainingChunkText()
    {
        const string chunkText = "Zákazník: Mám problém s akné na čele";
        const string expectedSummary = "Problém zákazníka: akné";

        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.Is<IEnumerable<ChatMessage>>(m =>
                    m.First().Text!.Contains(chunkText)),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(
                [new ChatMessage(ChatRole.Assistant, expectedSummary)]));

        var summarizer = Create();
        var result = await summarizer.SummarizeAsync(chunkText);

        Assert.Equal(expectedSummary, result);
    }

    [Fact]
    public async Task SummarizeAsync_WhenDisabled_ReturnsChunkTextWithoutCallingLlm()
    {
        var options = new KnowledgeBaseOptions { SummarizationEnabled = false };
        var summarizer = Create(options);
        const string chunkText = "Some chunk content";

        var result = await summarizer.SummarizeAsync(chunkText);

        Assert.Equal(chunkText, result);
        _chatClient.Verify(
            c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
