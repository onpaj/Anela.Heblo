using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Domain.Features.Leaflet;
using MediatR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;

public class GenerateLeafletHandler : IRequestHandler<GenerateLeafletRequest, GenerateLeafletResponse>
{
    private readonly IKnowledgeBaseRepository _kb;
    private readonly ILeafletRepository _leaflets;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddings;
    private readonly IChatClient _chat;
    private readonly LeafletOptions _options;
    private readonly ILogger<GenerateLeafletHandler> _logger;

    public GenerateLeafletHandler(
        IKnowledgeBaseRepository kb,
        ILeafletRepository leaflets,
        IEmbeddingGenerator<string, Embedding<float>> embeddings,
        IChatClient chat,
        IOptions<LeafletOptions> options,
        ILogger<GenerateLeafletHandler> logger)
    {
        _kb = kb;
        _leaflets = leaflets;
        _embeddings = embeddings;
        _chat = chat;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<GenerateLeafletResponse> Handle(
        GenerateLeafletRequest request,
        CancellationToken cancellationToken)
    {
        var topicVector = (await _embeddings.GenerateAsync([request.Topic], cancellationToken: cancellationToken))
            .First().Vector.ToArray();

        var kbHits = (await _kb.SearchSimilarAsync(topicVector, _options.KbTopK, cancellationToken))
            .Where(x => x.Score >= _options.MinSimilarityScore)
            .ToList();

        var leafletHits = (await _leaflets.SearchSimilarAsync(topicVector, _options.LeafletTopK, cancellationToken))
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
            LeafletLength.Long => _options.LongWordTarget,
            _ => _options.MediumWordTarget
        };

        var audienceLabel = request.Audience == AudienceType.B2B ? "B2B" : "Koncový zákazník";

        var kbContext = string.Join("\n\n---\n\n", kbHits.Select(h => h.Chunk.Content));
        var stage1System = _options.Stage1SystemPrompt
            .Replace("{topic}", request.Topic)
            .Replace("{audience}", audienceLabel)
            .Replace("{length}", lengthWords.ToString())
            .Replace("{kbContext}", string.IsNullOrWhiteSpace(kbContext) ? "(empty)" : kbContext);

        var outlineResponse = await _chat.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, stage1System),
                new ChatMessage(ChatRole.User, request.Topic)
            ],
            new ChatOptions { ModelId = _options.ChatModel, MaxOutputTokens = _options.ChatMaxTokens },
            cancellationToken);

        var outline = outlineResponse.Text ?? string.Empty;

        var leafletContext = string.Join("\n\n---\n\n", leafletHits.Select(h => h.Chunk.Content));
        var stage2System = _options.Stage2SystemPrompt
            .Replace("{topic}", request.Topic)
            .Replace("{audience}", audienceLabel)
            .Replace("{length}", lengthWords.ToString())
            .Replace("{coldStart}", coldStart)
            .Replace("{leafletContext}", string.IsNullOrWhiteSpace(leafletContext) ? "(none)" : leafletContext);

        var leafletResponse = await _chat.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, stage2System),
                new ChatMessage(ChatRole.User, outline)
            ],
            new ChatOptions { ModelId = _options.ChatModel, MaxOutputTokens = _options.ChatMaxTokens },
            cancellationToken);

        return new GenerateLeafletResponse { Content = leafletResponse.Text ?? string.Empty };
    }
}
