using System.Collections.Concurrent;
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
    private readonly Func<string, EmbeddingClient> _clientFactory;

    // The SDK's EmbeddingClient is bound to a single model at construction, so serving per-call
    // ModelId overrides without rebuilding an HTTP pipeline every call requires one cached client
    // per distinct model. Entries are Lazy<> rather than bare values because ConcurrentDictionary's
    // GetOrAdd factory delegate can run more than once under contention — Lazy<T> (default
    // ExecutionAndPublication mode) guarantees the EmbeddingClient constructor runs at most once
    // per model, matching the single-client guarantee this replaced.
    // No eviction: keys come only from operator-controlled config (RagFeatureOptions.EmbeddingModel),
    // never from user input, so the set of distinct models seen per process is small and bounded.
    private readonly ConcurrentDictionary<string, Lazy<EmbeddingClient>> _clients = new();

    public OpenAiEmbeddingGenerator(
        IOptions<OpenAiEmbeddingOptions> options,
        ILogger<OpenAiEmbeddingGenerator> logger)
        : this(options, logger, client: null)
    { }

    // The OpenAI SDK's EmbeddingClient constructor throws when the API key is empty, so it must not
    // run during DI construction: OpenAI:ApiKey is unset in test/local environments, and eagerly
    // building the client here would make every consumer of IEmbeddingGenerator unresolvable.
    // Construction is deferred to first use, by which point GenerateAsync has already validated the key.
    //
    // Test seam: `client` injects one pre-built client for the default model (seeded under
    // _options.EmbeddingModel, so no-override calls resolve to it exactly as before);
    // `clientFactory` overrides construction for every model, which is what override-path tests need.
    internal OpenAiEmbeddingGenerator(
        IOptions<OpenAiEmbeddingOptions> options,
        ILogger<OpenAiEmbeddingGenerator> logger,
        EmbeddingClient? client,
        Func<string, EmbeddingClient>? clientFactory = null)
    {
        _options = options.Value;
        _logger = logger;
        _clientFactory = clientFactory ?? (model => new EmbeddingClient(model, _options.ApiKey));

        if (client != null)
            _clients[_options.EmbeddingModel] = new Lazy<EmbeddingClient>(() => client);
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

        var model = options?.ModelId ?? _options.EmbeddingModel;
        var dimensions = options?.Dimensions ?? _options.EmbeddingDimensions;
        var client = _clients.GetOrAdd(model, m => new Lazy<EmbeddingClient>(() => _clientFactory(m))).Value;

        var embeddingOptions = new global::OpenAI.Embeddings.EmbeddingGenerationOptions { Dimensions = dimensions };
        var embeddings = new GeneratedEmbeddings<Embedding<float>>();

        foreach (var chunk in inputList.Chunk(MaxBatchSize))
        {
            var result = await Pipeline.ExecuteAsync(
                async token => await client.GenerateEmbeddingsAsync(chunk, embeddingOptions, cancellationToken: token),
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
