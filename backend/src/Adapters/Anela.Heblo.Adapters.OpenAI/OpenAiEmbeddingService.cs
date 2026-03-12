using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Embeddings;
using Polly;
using Polly.Retry;

namespace Anela.Heblo.Adapters.OpenAI;

public class OpenAiEmbeddingService : IEmbeddingService
{
    private static readonly ResiliencePipeline Pipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(2),
            BackoffType = DelayBackoffType.Exponential,
            ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>()
        })
        .Build();

    private readonly OpenAiEmbeddingOptions _options;
    private readonly ILogger<OpenAiEmbeddingService> _logger;

    public OpenAiEmbeddingService(
        IOptions<OpenAiEmbeddingOptions> options,
        ILogger<OpenAiEmbeddingService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_options.ApiKey))
            throw new InvalidOperationException("OpenAI:ApiKey is not configured.");

        var client = new EmbeddingClient(_options.EmbeddingModel, _options.ApiKey);

        _logger.LogDebug("Generating embedding for {CharCount} characters", text.Length);
        var result = await Pipeline.ExecuteAsync(
            async token => await client.GenerateEmbeddingAsync(text, cancellationToken: token),
            ct);
        return result.Value.ToFloats().ToArray();
    }
}
