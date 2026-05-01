using System.IO;
using System.Net.Http;
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
        var ct = cancellationToken;

        var topicVector = (await RetryOnceAsync(
                () => _embeddings.GenerateAsync([request.Topic], cancellationToken: ct),
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

        var kbContext = string.Join("\n\n---\n\n", kbHits.Select(h => h.Chunk.Content));
        var stage1System = _options.Stage1SystemPrompt
            .Replace("{topic}", request.Topic)
            .Replace("{audience}", audienceLabel)
            .Replace("{length}", lengthWords.ToString())
            .Replace("{kbContext}", string.IsNullOrWhiteSpace(kbContext) ? "(empty)" : kbContext);

        var chatOptions = new ChatOptions { ModelId = _options.ChatModel, MaxOutputTokens = _options.ChatMaxTokens };

        var outlineResponse = await RetryOnceAsync(
            () => _chat.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, stage1System),
                    new ChatMessage(ChatRole.User, request.Topic)
                ],
                chatOptions,
                ct),
            ct);

        var outline = outlineResponse.Text ?? string.Empty;

        var leafletContext = string.Join("\n\n---\n\n", leafletHits.Select(h => h.Chunk.Content));
        var stage2System = _options.Stage2SystemPrompt
            .Replace("{topic}", request.Topic)
            .Replace("{audience}", audienceLabel)
            .Replace("{length}", lengthWords.ToString())
            .Replace("{coldStart}", coldStart)
            .Replace("{leafletContext}", string.IsNullOrWhiteSpace(leafletContext) ? "(none)" : leafletContext);

        var leafletResponse = await RetryOnceAsync(
            () => _chat.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, stage2System),
                    new ChatMessage(ChatRole.User, outline)
                ],
                chatOptions,
                ct),
            ct);

        return new GenerateLeafletResponse { Content = leafletResponse.Text ?? string.Empty };
    }

    private async Task<T> RetryOnceAsync<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TimeoutException or TaskCanceledException)
        {
            if (ct.IsCancellationRequested)
                throw;
            _logger.LogWarning(ex, "Transient error during leaflet generation, retrying once");
            await Task.Delay(1000, ct);

            try
            {
                return await operation();
            }
            catch (Exception retryEx)
            {
                throw new InvalidOperationException("Leaflet generation failed after retry.", retryEx);
            }
        }
    }
}
