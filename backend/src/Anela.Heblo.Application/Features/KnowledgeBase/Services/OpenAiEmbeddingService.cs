using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Embeddings;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class OpenAiEmbeddingService : IEmbeddingService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAiEmbeddingService> _logger;

    public OpenAiEmbeddingService(IConfiguration configuration, ILogger<OpenAiEmbeddingService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var apiKey = _configuration["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured.");
        var model = _configuration["KnowledgeBase:EmbeddingModel"] ?? "text-embedding-3-small";
        var client = new EmbeddingClient(model, apiKey);

        _logger.LogDebug("Generating embedding for {CharCount} characters", text.Length);
        var result = await client.GenerateEmbeddingAsync(text, cancellationToken: ct);
        return result.Value.ToFloats().ToArray();
    }
}
