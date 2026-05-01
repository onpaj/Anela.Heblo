using Anela.Heblo.Application.Features.Leaflet;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Domain.Features.Leaflet;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet.UseCases;

public class GenerateLeafletHandlerTests
{
    private readonly Mock<IKnowledgeBaseRepository> _kb = new();
    private readonly Mock<ILeafletRepository> _leaflets = new();
    private readonly Mock<IEmbeddingGenerator<string, Embedding<float>>> _embeddings = new();
    private readonly Mock<IChatClient> _chat = new();
    private readonly Mock<ILogger<GenerateLeafletHandler>> _logger = new();

    private static readonly float[] DefaultVector = [0.1f, 0.2f, 0.3f];

    private GenerateLeafletHandler CreateHandler(LeafletOptions? options = null)
    {
        return new GenerateLeafletHandler(
            _kb.Object,
            _leaflets.Object,
            _embeddings.Object,
            _chat.Object,
            Options.Create(options ?? new LeafletOptions()),
            _logger.Object);
    }

    private void SetupEmbeddings()
    {
        _embeddings
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(
                [new Embedding<float>(new ReadOnlyMemory<float>(DefaultVector))]));
    }

    private void SetupChatReturns(string firstResponse = "outline text", string secondResponse = "leaflet content")
    {
        var callCount = 0;
        _chat
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                var text = callCount == 1 ? firstResponse : secondResponse;
                return new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]);
            });
    }

    private static KnowledgeBaseChunk MakeKbChunk(string content = "kb chunk content") =>
        new() { Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Content = content };

    private static LeafletChunk MakeLeafletChunk(string content = "leaflet chunk content") =>
        new() { Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Content = content };

    private static (KnowledgeBaseChunk Chunk, double Score) KbHit(double score, string content = "kb content") =>
        (MakeKbChunk(content), score);

    private static (LeafletChunk Chunk, double Score) LeafletHit(double score, string content = "leaflet content") =>
        (MakeLeafletChunk(content), score);

    [Fact]
    public async Task Handle_dual_empty_retrieval_throws_EmptyRetrievalException()
    {
        // Arrange
        SetupEmbeddings();

        _kb.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _leaflets.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var handler = CreateHandler();
        var request = new GenerateLeafletRequest { Topic = "retinol", Audience = AudienceType.EndConsumer, Length = LeafletLength.Short };

        // Act
        var act = () => handler.Handle(request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<EmptyRetrievalException>();
    }

    [Fact]
    public async Task Handle_only_leaflet_empty_logs_cold_start_and_continues()
    {
        // Arrange
        SetupEmbeddings();
        SetupChatReturns("outline", "final leaflet");

        var kbChunks = new List<(KnowledgeBaseChunk Chunk, double Score)>
        {
            KbHit(0.9, "kb chunk 1"),
            KbHit(0.8, "kb chunk 2"),
            KbHit(0.7, "kb chunk 3"),
        };

        _kb.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(kbChunks);
        _leaflets.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var handler = CreateHandler();
        var request = new GenerateLeafletRequest { Topic = "hyaluronic acid", Audience = AudienceType.EndConsumer, Length = LeafletLength.Medium };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        _chat.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));

        _logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("cold-start")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        response.Content.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_only_kb_empty_continues_with_neutral_kb_context()
    {
        // Arrange
        SetupEmbeddings();

        var leafletChunks = new List<(LeafletChunk Chunk, double Score)>
        {
            LeafletHit(0.9, "leaflet chunk 1"),
            LeafletHit(0.8, "leaflet chunk 2"),
        };

        _kb.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _leaflets.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(leafletChunks);

        IEnumerable<ChatMessage>? capturedMessages = null;
        _chat
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>(
                (msgs, _, _) => capturedMessages ??= msgs)
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "outline")]));

        var handler = CreateHandler();
        var request = new GenerateLeafletRequest { Topic = "niacinamide", Audience = AudienceType.B2B, Length = LeafletLength.Long };

        // Act
        var act = () => handler.Handle(request, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();

        _chat.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));

        capturedMessages.Should().NotBeNull();
        capturedMessages!.First(m => m.Role == ChatRole.System).Text.Should().Contain("(empty)");
    }

    [Fact]
    public async Task Handle_filters_below_threshold_chunks()
    {
        // Arrange
        SetupEmbeddings();
        SetupChatReturns();

        var threshold = 0.55;
        var options = new LeafletOptions { MinSimilarityScore = threshold };

        var kbChunks = new List<(KnowledgeBaseChunk Chunk, double Score)>
        {
            KbHit(0.9, "above 1"),
            KbHit(0.8, "above 2"),
            KbHit(0.4, "below 1"),
            KbHit(0.3, "below 2"),
            KbHit(0.2, "below 3"),
        };

        _kb.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(kbChunks);
        _leaflets.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([LeafletHit(0.9)]);

        IEnumerable<ChatMessage>? capturedStage1Messages = null;
        _chat
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>(
                (msgs, _, _) => capturedStage1Messages ??= msgs)
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "response")]));

        var handler = CreateHandler(options);
        var request = new GenerateLeafletRequest { Topic = "ceramides", Audience = AudienceType.EndConsumer, Length = LeafletLength.Short };

        // Act
        await handler.Handle(request, CancellationToken.None);

        // Assert
        var systemMessage = capturedStage1Messages!.First(m => m.Role == ChatRole.System).Text!;
        systemMessage.Should().Contain("above 1");
        systemMessage.Should().Contain("above 2");
        systemMessage.Should().NotContain("below 1");
        systemMessage.Should().NotContain("below 2");
        systemMessage.Should().NotContain("below 3");
    }

    [Fact]
    public async Task Handle_filters_below_threshold_leaflet_chunks()
    {
        // Arrange
        SetupEmbeddings();

        var threshold = 0.55;
        var options = new LeafletOptions { MinSimilarityScore = threshold };

        _kb.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([KbHit(0.9, "kb above threshold")]);

        var leafletChunks = new List<(LeafletChunk Chunk, double Score)>
        {
            LeafletHit(0.9, "leaflet above threshold"),
            LeafletHit(0.4, "leaflet below 1"),
            LeafletHit(0.3, "leaflet below 2"),
            LeafletHit(0.2, "leaflet below 3"),
        };

        _leaflets.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(leafletChunks);

        IEnumerable<ChatMessage>? capturedStage2Messages = null;
        var callCount = 0;
        _chat
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>(
                (msgs, _, _) =>
                {
                    callCount++;
                    if (callCount == 2)
                    {
                        capturedStage2Messages = msgs;
                    }
                })
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "response")]));

        var handler = CreateHandler(options);
        var request = new GenerateLeafletRequest { Topic = "retinol", Audience = AudienceType.EndConsumer, Length = LeafletLength.Short };

        // Act
        await handler.Handle(request, CancellationToken.None);

        // Assert
        capturedStage2Messages.Should().NotBeNull();
        var stage2SystemMessage = capturedStage2Messages!.First(m => m.Role == ChatRole.System).Text!;
        stage2SystemMessage.Should().Contain("leaflet above threshold");
        stage2SystemMessage.Should().NotContain("leaflet below 1");
        stage2SystemMessage.Should().NotContain("leaflet below 2");
        stage2SystemMessage.Should().NotContain("leaflet below 3");
    }

    [Fact]
    public async Task Handle_uses_topic_embedding_only_once()
    {
        // Arrange
        SetupEmbeddings();
        SetupChatReturns();

        float[]? capturedKbVector = null;
        float[]? capturedLeafletVector = null;

        _kb.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<float[], int, CancellationToken>((v, _, _) => capturedKbVector = v)
            .ReturnsAsync([KbHit(0.9)]);
        _leaflets.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<float[], int, CancellationToken>((v, _, _) => capturedLeafletVector = v)
            .ReturnsAsync([LeafletHit(0.9)]);

        var handler = CreateHandler();
        var request = new GenerateLeafletRequest { Topic = "vitamin c", Audience = AudienceType.EndConsumer, Length = LeafletLength.Medium };

        // Act
        await handler.Handle(request, CancellationToken.None);

        // Assert
        _embeddings.Verify(
            e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        capturedKbVector.Should().NotBeNull();
        capturedLeafletVector.Should().NotBeNull();
        capturedKbVector.Should().BeEquivalentTo(capturedLeafletVector);
    }

    [Theory]
    [InlineData(LeafletLength.Short, 200)]
    [InlineData(LeafletLength.Medium, 400)]
    [InlineData(LeafletLength.Long, 700)]
    public async Task Handle_substitutes_length_word_target_per_LeafletLength(LeafletLength length, int expectedWordCount)
    {
        // Arrange
        SetupEmbeddings();

        _kb.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([KbHit(0.9)]);
        _leaflets.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([LeafletHit(0.9)]);

        IEnumerable<ChatMessage>? capturedMessages = null;
        _chat
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>(
                (msgs, _, _) => capturedMessages ??= msgs)
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "response")]));

        var handler = CreateHandler();
        var request = new GenerateLeafletRequest { Topic = "peptides", Audience = AudienceType.EndConsumer, Length = length };

        // Act
        await handler.Handle(request, CancellationToken.None);

        // Assert
        var systemMessage = capturedMessages!.First(m => m.Role == ChatRole.System).Text!;
        systemMessage.Should().Contain(expectedWordCount.ToString());
    }

    [Theory]
    [InlineData(AudienceType.B2B, "B2B")]
    [InlineData(AudienceType.EndConsumer, "Koncový zákazník")]
    public async Task Handle_substitutes_audience_label_to_czech(AudienceType audience, string expectedLabel)
    {
        // Arrange
        SetupEmbeddings();

        _kb.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([KbHit(0.9)]);
        _leaflets.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([LeafletHit(0.9)]);

        IEnumerable<ChatMessage>? capturedMessages = null;
        _chat
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>(
                (msgs, _, _) => capturedMessages ??= msgs)
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "response")]));

        var handler = CreateHandler();
        var request = new GenerateLeafletRequest { Topic = "squalane", Audience = audience, Length = LeafletLength.Medium };

        // Act
        await handler.Handle(request, CancellationToken.None);

        // Assert
        var systemMessage = capturedMessages!.First(m => m.Role == ChatRole.System).Text!;
        systemMessage.Should().Contain(expectedLabel);
    }
}
