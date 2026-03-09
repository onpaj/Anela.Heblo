using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Embeddings;
using Polly;
using Polly.Retry;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class OpenAiEmbeddingService : IEmbeddingService
{
    private readonly KnowledgeBaseOptions _options;
    private readonly IConfiguration _configuration;
    private readonly ResiliencePipeline _pipeline;
    private readonly ILogger<OpenAiEmbeddingService> _logger;

    public OpenAiEmbeddingService(
        IOptions<KnowledgeBaseOptions> options,
        IConfiguration configuration,
        ILogger<OpenAiEmbeddingService> logger)
    {
        _options = options.Value;
        _configuration = configuration;
        _logger = logger;
        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>()
            })
            .Build();
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var apiKey = _configuration["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured.");

        var client = new EmbeddingClient(_options.EmbeddingModel, apiKey);

        _logger.LogDebug("Generating embedding for {CharCount} characters", text.Length);
        var result = await _pipeline.ExecuteAsync(
            async token => await client.GenerateEmbeddingAsync(text, cancellationToken: token),
            ct);
        return result.Value.ToFloats().ToArray();
    }
}
