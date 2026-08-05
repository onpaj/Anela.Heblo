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
    private const int MaxBatchSize = 2048; // OpenAI embeddings endpoint per-request item cap

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
    private readonly Lazy<EmbeddingClient> _client;

    public OpenAiEmbeddingGenerator(
        IOptions<OpenAiEmbeddingOptions> options,
        ILogger<OpenAiEmbeddingGenerator> logger)
        : this(options, logger, client: null)
    { }

    // The OpenAI SDK's EmbeddingClient constructor throws when the API key is empty, so it must not
    // run during DI construction: OpenAI:ApiKey is unset in test/local environments, and eagerly
    // building the client here would make every consumer of IEmbeddingGenerator unresolvable.
    // Construction is deferred to first use, by which point GenerateAsync has already validated the key.
    internal OpenAiEmbeddingGenerator(
        IOptions<OpenAiEmbeddingOptions> options,
        ILogger<OpenAiEmbeddingGenerator> logger,
        EmbeddingClient? client)
    {
        _options = options.Value;
        _logger = logger;
        _client = new Lazy<EmbeddingClient>(() => client ?? new EmbeddingClient(_options.EmbeddingModel, _options.ApiKey));
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
        if (inputList.Count == 0)
            return new GeneratedEmbeddings<Embedding<float>>();

        _logger.LogDebug("Generating embeddings for {Count} inputs", inputList.Count);

        var embeddingOptions = new global::OpenAI.Embeddings.EmbeddingGenerationOptions { Dimensions = _options.EmbeddingDimensions };
        var embeddings = new GeneratedEmbeddings<Embedding<float>>();

        foreach (var chunk in inputList.Chunk(MaxBatchSize))
        {
            var result = await Pipeline.ExecuteAsync(
                async token => await _client.Value.GenerateEmbeddingsAsync(chunk, embeddingOptions, cancellationToken: token),
                cancellationToken);

            foreach (var item in result.Value.OrderBy(e => e.Index))
            {
                var floats = item.ToFloats();
                embeddings.Add(new Embedding<float>(new ReadOnlyMemory<float>(floats.ToArray())));
            }
        }

        return embeddings;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
