using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Embeddings;
using Polly;
using Polly.Retry;
using MeaiOptions = Microsoft.Extensions.AI.EmbeddingGenerationOptions;

namespace Anela.Heblo.Adapters.OpenAI;

public class OpenAiEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
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
    private readonly ILogger<OpenAiEmbeddingGenerator> _logger;

    public OpenAiEmbeddingGenerator(
        IOptions<OpenAiEmbeddingOptions> options,
        ILogger<OpenAiEmbeddingGenerator> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public EmbeddingGeneratorMetadata Metadata => new("OpenAiEmbeddingGenerator");

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        MeaiOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.ApiKey))
            throw new InvalidOperationException("OpenAI:ApiKey is not configured.");

        var inputList = values.ToList();
        var client = new EmbeddingClient(_options.EmbeddingModel, _options.ApiKey);

        _logger.LogDebug("Generating embeddings for {Count} inputs", inputList.Count);

        var embeddings = new GeneratedEmbeddings<Embedding<float>>();

        foreach (var input in inputList)
        {
            var result = await Pipeline.ExecuteAsync(
                async token => await client.GenerateEmbeddingAsync(input, cancellationToken: token),
                cancellationToken);

            var floats = result.Value.ToFloats();
            var embeddingVector = new ReadOnlyMemory<float>(floats.ToArray());
            embeddings.Add(new Embedding<float>(embeddingVector));
        }

        return embeddings;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
