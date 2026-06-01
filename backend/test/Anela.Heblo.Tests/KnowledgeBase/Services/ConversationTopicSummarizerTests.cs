using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class ConversationTopicSummarizerTests
{
    private readonly Mock<IChatClient> _chatClient = new();

    private ConversationTopicSummarizer Create(KnowledgeBaseOptions? options = null)
        => new(_chatClient.Object, Options.Create(options ?? new KnowledgeBaseOptions()));

    [Fact]
    public async Task SummarizeTopicsAsync_SingleTopicResponse_ReturnsOneItem()
    {
        const string llmResponse =
            "[TOPIC]\nProdukty: Sérum ABC\nProblém zákazníka: akné";

        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(
                [new ChatMessage(ChatRole.Assistant, llmResponse)]));

        var summarizer = Create();
        var result = await summarizer.SummarizeTopicsAsync("some transcript");

        Assert.Single(result);
        Assert.Contains("Sérum ABC", result[0]);
    }

    [Fact]
    public async Task SummarizeTopicsAsync_MultiTopicResponse_ReturnsMultipleItems()
    {
        const string llmResponse =
            "[TOPIC]\nProdukty: Sérum ABC\nProblém zákazníka: akné\n\n" +
            "[TOPIC]\nProdukty: Krém XYZ\nProblém zákazníka: popraskané nožky";

        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(
                [new ChatMessage(ChatRole.Assistant, llmResponse)]));

        var summarizer = Create();
        var result = await summarizer.SummarizeTopicsAsync("some transcript");

        Assert.Equal(2, result.Count);
        Assert.Contains("Sérum ABC", result[0]);
        Assert.Contains("Krém XYZ", result[1]);
    }

    [Fact]
    public async Task SummarizeTopicsAsync_EmptyBlocksDiscarded()
    {
        // LLM response starts with [TOPIC] — split produces empty string before first block
        const string llmResponse =
            "[TOPIC]\nProdukty: Sérum ABC\n\n[TOPIC]\n\n[TOPIC]\nProdukty: Krém XYZ";

        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(
                [new ChatMessage(ChatRole.Assistant, llmResponse)]));

        var summarizer = Create();
        var result = await summarizer.SummarizeTopicsAsync("transcript");

        // Empty block in the middle must be discarded
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task SummarizeTopicsAsync_PromptContainsFullText()
    {
        const string fullText = "Zákazník: Dobrý den, mám problém s akné";

        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.Is<IEnumerable<ChatMessage>>(m =>
                    m.First().Text!.Contains(fullText)),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(
                [new ChatMessage(ChatRole.Assistant, "[TOPIC]\nProblém zákazníka: akné")]));

        var summarizer = Create();
        await summarizer.SummarizeTopicsAsync(fullText);

        _chatClient.Verify(
            c => c.GetResponseAsync(
                It.Is<IEnumerable<ChatMessage>>(m => m.First().Text!.Contains(fullText)),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SummarizeTopicsAsync_WhenDisabled_ReturnsFullTextWithoutCallingLlm()
    {
        var options = new KnowledgeBaseOptions { SummarizationEnabled = false };
        var summarizer = Create(options);
        const string fullText = "Some conversation text";

        var result = await summarizer.SummarizeTopicsAsync(fullText);

        Assert.Single(result);
        Assert.Equal(fullText, result[0]);
        _chatClient.Verify(
            c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
