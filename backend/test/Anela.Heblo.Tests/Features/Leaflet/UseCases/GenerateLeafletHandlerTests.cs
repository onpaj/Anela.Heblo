using Anela.Heblo.Application.Features.Leaflet;
using Anela.Heblo.Application.Features.Leaflet.Contracts;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;
using Anela.Heblo.Application.Shared.Rag;
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
    private readonly Mock<ILeafletKnowledgeSource> _kb = new();
    private readonly Mock<ILeafletDocumentRepository> _leaflets = new();
    private readonly Mock<IEmbeddingGenerator<string, Embedding<float>>> _embeddings = new();
    private readonly Mock<IRagQueryExpander> _expander = new();
    private readonly Mock<IChatClient> _chat = new();
    private readonly Mock<ILogger<GenerateLeafletHandler>> _logger = new();

    private static readonly float[] DefaultVector = [0.1f, 0.2f, 0.3f];

    public GenerateLeafletHandlerTests()
    {
        _expander
            .Setup(e => e.ExpandAsync(
                It.IsAny<string>(),
                It.IsAny<RagQueryExpansionConfig>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string q, RagQueryExpansionConfig _, CancellationToken _) => q);
    }

    private GenerateLeafletHandler CreateHandler(LeafletOptions? options = null)
    {
        return new GenerateLeafletHandler(
            _kb.Object,
            _leaflets.Object,
            _embeddings.Object,
            _expander.Object,
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

    private static LeafletChunk MakeLeafletChunk(string content = "leaflet chunk content") =>
        new() { Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Content = content };

    private static KnowledgeSearchResult KbHit(double score, string content = "kb content") =>
        new() { Content = content, Score = score };

    private static (LeafletChunk Chunk, double Score) LeafletHit(double score, string content = "leaflet content") =>
        (MakeLeafletChunk(content), score);

    [Fact]
    public async Task Handle_dual_empty_retrieval_throws_EmptyRetrievalException()
    {
        // Arrange
        SetupEmbeddings();

        _kb.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeSearchResult>());
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

        var kbChunks = new List<KnowledgeSearchResult>
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
            .ReturnsAsync(new List<KnowledgeSearchResult>());
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
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        _chat.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));

        capturedMessages.Should().NotBeNull();
        capturedMessages!.First(m => m.Role == ChatRole.System).Text.Should().Contain("(empty)");
        result.Content.Should().NotBeEmpty("handler should produce leaflet content even when KB hits are empty");
    }

    [Fact]
    public async Task Handle_filters_below_threshold_chunks()
    {
        // Arrange
        SetupEmbeddings();
        SetupChatReturns();

        var threshold = 0.55;
        var options = new LeafletOptions { MinSimilarityScore = threshold };

        var kbChunks = new List<KnowledgeSearchResult>
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
            .ReturnsAsync(new List<KnowledgeSearchResult> { KbHit(0.9, "kb above threshold") });

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
            .ReturnsAsync(new List<KnowledgeSearchResult> { KbHit(0.9) });
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
            .ReturnsAsync(new List<KnowledgeSearchResult> { KbHit(0.9) });
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

    [Fact]
    public async Task Handle_sets_coldstart_true_in_stage2_prompt_when_leaflet_empty()
    {
        // Arrange
        SetupEmbeddings();

        _kb.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeSearchResult> { KbHit(0.9, "kb chunk above threshold") });
        _leaflets.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

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

        var options = new LeafletOptions
        {
            Stage2SystemPrompt = "coldStart={coldStart}",
        };

        var handler = CreateHandler(options);
        var request = new GenerateLeafletRequest { Topic = "retinol", Audience = AudienceType.EndConsumer, Length = LeafletLength.Short };

        // Act
        await handler.Handle(request, CancellationToken.None);

        // Assert
        capturedStage2Messages.Should().NotBeNull();
        var stage2SystemMessage = capturedStage2Messages!.First(m => m.Role == ChatRole.System).Text!;
        stage2SystemMessage.Should().Contain("true");
    }

    [Theory]
    [InlineData(AudienceType.B2B, "B2B")]
    [InlineData(AudienceType.EndConsumer, "Koncový zákazník")]
    public async Task Handle_substitutes_audience_label_to_czech(AudienceType audience, string expectedLabel)
    {
        // Arrange
        SetupEmbeddings();

        _kb.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeSearchResult> { KbHit(0.9) });
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

    [Fact]
    public async Task Handle_calls_expander_with_topic_and_uses_expanded_text_for_embedding_only()
    {
        // Arrange
        const string topic = "retinol";
        const string expandedQuery = "EXPANDED_QUERY";

        _expander
            .Setup(e => e.ExpandAsync(
                It.IsAny<string>(),
                It.IsAny<RagQueryExpansionConfig>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expandedQuery);

        IEnumerable<string>? capturedEmbeddingInput = null;
        _embeddings
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, EmbeddingGenerationOptions?, CancellationToken>(
                (inputs, _, _) => capturedEmbeddingInput = inputs.ToList())
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(
                [new Embedding<float>(new ReadOnlyMemory<float>(DefaultVector))]));

        _kb.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeSearchResult> { KbHit(0.9) });
        _leaflets.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([LeafletHit(0.9)]);

        var stage1Messages = new List<IEnumerable<ChatMessage>>();
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
                    stage1Messages.Add(msgs.ToList());
                })
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "outline text")]));

        var handler = CreateHandler();
        var request = new GenerateLeafletRequest { Topic = topic, Audience = AudienceType.EndConsumer, Length = LeafletLength.Short };

        // Act
        await handler.Handle(request, CancellationToken.None);

        // Assert
        capturedEmbeddingInput.Should().ContainSingle().Which.Should().Be(expandedQuery);

        var stage1SystemText = stage1Messages[0].First(m => m.Role == ChatRole.System).Text!;
        stage1SystemText.Should().Contain(topic);
        stage1SystemText.Should().NotContain(expandedQuery);

        var stage2UserText = stage1Messages[1].First(m => m.Role == ChatRole.User).Text!;
        stage2UserText.Should().NotBe(expandedQuery);
    }

    [Fact]
    public async Task Handle_returns_non_empty_content_when_expansion_returns_topic_unchanged()
    {
        // Arrange
        SetupEmbeddings();
        SetupChatReturns("outline", "final leaflet");

        _kb.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeSearchResult> { KbHit(0.9) });
        _leaflets.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([LeafletHit(0.9)]);

        var handler = CreateHandler();
        var request = new GenerateLeafletRequest { Topic = "niacinamide", Audience = AudienceType.EndConsumer, Length = LeafletLength.Medium };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Content.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_expander_called_once_per_generate_request()
    {
        // Arrange
        SetupEmbeddings();
        SetupChatReturns();

        _kb.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeSearchResult> { KbHit(0.9) });
        _leaflets.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([LeafletHit(0.9)]);

        var handler = CreateHandler();
        var request = new GenerateLeafletRequest { Topic = "vitamin c", Audience = AudienceType.B2B, Length = LeafletLength.Long };

        // Act
        await handler.Handle(request, CancellationToken.None);

        // Assert
        _expander.Verify(
            e => e.ExpandAsync(
                It.IsAny<string>(),
                It.IsAny<RagQueryExpansionConfig>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
