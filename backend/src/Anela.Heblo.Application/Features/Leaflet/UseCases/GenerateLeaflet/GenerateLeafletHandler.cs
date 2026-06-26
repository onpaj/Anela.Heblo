using Anela.Heblo.Application.Features.Leaflet.Contracts;
using Anela.Heblo.Application.Shared.Http;
using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Domain.Features.Leaflet;
using MediatR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;

public class GenerateLeafletHandler : IRequestHandler<GenerateLeafletRequest, GenerateLeafletResponse>
{
    private readonly ILeafletKnowledgeSource _kb;
    private readonly ILeafletDocumentRepository _leaflets;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddings;
    private readonly IRagQueryExpander _expander;
    private readonly IChatClient _chat;
    private readonly LeafletOptions _options;
    private readonly ILogger<GenerateLeafletHandler> _logger;

    public GenerateLeafletHandler(
        ILeafletKnowledgeSource kb,
        ILeafletDocumentRepository leaflets,
        IEmbeddingGenerator<string, Embedding<float>> embeddings,
        IRagQueryExpander expander,
        IChatClient chat,
        IOptions<LeafletOptions> options,
        ILogger<GenerateLeafletHandler> logger)
    {
        _kb = kb;
        _leaflets = leaflets;
        _embeddings = embeddings;
        _expander = expander;
        _chat = chat;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<GenerateLeafletResponse> Handle(
        GenerateLeafletRequest request,
        CancellationToken cancellationToken)
    {
        var ct = cancellationToken;

        var queryToEmbed = await _expander.ExpandAsync(
            request.Topic, _options.ToExpansionConfig(), ct);

        var topicVector = (await ChatRetry.RetryOnceAsync(
                () => _embeddings.GenerateAsync([queryToEmbed], cancellationToken: ct),
                _logger,
                ct))
            .First().Vector.ToArray();

        var kbHits = (await _kb.SearchSimilarAsync(topicVector, _options.KbTopK, ct))
            .Where(x => x.Score >= _options.MinSimilarityScore)
            .ToList();

        var leafletHits = (await _leaflets.SearchSimilarAsync(topicVector, _options.LeafletTopK, ct))
            .Where(x => x.Score >= _options.MinSimilarityScore)
            .ToList();

        if (kbHits.Count == 0 && leafletHits.Count == 0)
        {
            throw new EmptyRetrievalException(
                "Knowledge Base does not yet cover this topic; try a broader phrasing");
        }

        var coldStart = leafletHits.Count == 0 ? "true" : "false";

        if (leafletHits.Count == 0)
        {
            _logger.LogWarning(
                "Leaflet cold-start: zero leaflet style references for topic '{Topic}'",
                request.Topic);
        }

        var lengthWords = request.Length switch
        {
            LeafletLength.Short => _options.ShortWordTarget,
            LeafletLength.Medium => _options.MediumWordTarget,
            LeafletLength.Long => _options.LongWordTarget,
            _ => throw new ArgumentOutOfRangeException(nameof(request.Length), request.Length, "Unknown leaflet length"),
        };

        var audienceLabel = request.Audience switch
        {
            AudienceType.B2B => "B2B",
            AudienceType.EndConsumer => "Koncový zákazník",
            _ => throw new ArgumentOutOfRangeException(nameof(request.Audience), request.Audience, "Unknown audience type"),
        };

        var kbContext = string.Join("\n\n---\n\n", kbHits.Select(h => h.Content));
        var stage1System = _options.Stage1SystemPrompt
            .Replace("{topic}", request.Topic)
            .Replace("{audience}", audienceLabel)
            .Replace("{length}", lengthWords.ToString())
            .Replace("{kbContext}", string.IsNullOrWhiteSpace(kbContext) ? "(empty)" : kbContext);

        var chatOptions = new ChatOptions { ModelId = _options.ChatModel, MaxOutputTokens = _options.ChatMaxTokens };

        var outlineResponse = await ChatRetry.RetryOnceAsync(
            () => _chat.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, stage1System),
                    new ChatMessage(ChatRole.User, request.Topic)
                ],
                chatOptions,
                ct),
            _logger,
            ct);

        var outline = outlineResponse.Text ?? string.Empty;

        var leafletContext = string.Join("\n\n---\n\n", leafletHits.Select(h => h.Chunk.Content));
        var stage2System = _options.Stage2SystemPrompt
            .Replace("{topic}", request.Topic)
            .Replace("{audience}", audienceLabel)
            .Replace("{length}", lengthWords.ToString())
            .Replace("{coldStart}", coldStart)
            .Replace("{leafletContext}", string.IsNullOrWhiteSpace(leafletContext) ? "(none)" : leafletContext);

        var leafletResponse = await ChatRetry.RetryOnceAsync(
            () => _chat.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, stage2System),
                    new ChatMessage(ChatRole.User, outline)
                ],
                chatOptions,
                ct),
            _logger,
            ct);

        return new GenerateLeafletResponse
        {
            Content = leafletResponse.Text ?? string.Empty,
            KbSourceCount = kbHits.Count,
            LeafletSourceCount = leafletHits.Count,
        };
    }
}
