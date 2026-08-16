### task: add-per-model-embedding-client-cache


Implements FR-1 and NFR-1. Replaces the single `Lazy<EmbeddingClient>` with a per-model cache, resolves `model` from `options?.ModelId`, and adds the internal factory test seam described in "Deviations from the spec" above.

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiEmbeddingGenerator.cs`
- Test: `backend/test/Anela.Heblo.Adapters.OpenAI.Tests/OpenAiEmbeddingGeneratorTests.cs`

- [ ] **Step 1: Add the recording client-factory test double and its builder helper**

In `backend/test/Anela.Heblo.Adapters.OpenAI.Tests/OpenAiEmbeddingGeneratorTests.cs`, insert these two members immediately after the existing `BuildGenerator` method:

```csharp
    // Builds a fake-transport EmbeddingClient per requested model and records which models were
    // asked for, so cache-reuse assertions can count actual client constructions (not HTTP calls).
    private sealed class RecordingClientFactory
    {
        private readonly HttpMessageHandler _handler;
        private readonly string _apiKey;

        public RecordingClientFactory(HttpMessageHandler handler, string apiKey)
        {
            _handler = handler;
            _apiKey = apiKey;
        }

        public List<string> RequestedModels { get; } = new();

        public EmbeddingClient Create(string model)
        {
            RequestedModels.Add(model);
            return new EmbeddingClient(
                model,
                new ApiKeyCredential(_apiKey),
                new OpenAIClientOptions { Transport = new HttpClientPipelineTransport(new HttpClient(_handler)) });
        }
    }

    // Unlike BuildGenerator (which injects one pre-built client bound to the default model), this
    // routes every distinct resolved model through the factory, so override paths stay on the fake
    // transport instead of constructing a real client against api.openai.com.
    private static OpenAiEmbeddingGenerator BuildGeneratorWithFactory(
        RecordingClientFactory factory,
        string defaultModel = "text-embedding-3-small",
        int dimensions = 3)
    {
        var options = Options.Create(new OpenAiEmbeddingOptions
        {
            ApiKey = "test-key",
            EmbeddingModel = defaultModel,
            EmbeddingDimensions = dimensions,
        });

        return new OpenAiEmbeddingGenerator(
            options,
            NullLogger<OpenAiEmbeddingGenerator>.Instance,
            client: null,
            clientFactory: factory.Create);
    }
```

- [ ] **Step 2: Write the failing tests**

Append these four tests inside the `OpenAiEmbeddingGeneratorTests` class, after `GenerateAsync_NoDimensionsOverride_SendsConfiguredDimensions`:

```csharp
    [Fact]
    public async Task GenerateAsync_ModelIdOverride_UsesOverriddenModel()
    {
        var models = new List<string>();
        var handler = new StatefulHandler(req => BuildEmbeddingResponse(req, capturedModels: models));
        var factory = new RecordingClientFactory(handler, "test-key");
        var generator = BuildGeneratorWithFactory(factory, defaultModel: "text-embedding-3-large");

        await generator.GenerateAsync(MakeInputs(1), new MeaiOptions { ModelId = "text-embedding-3-small" });

        models.Should().ContainSingle().Which.Should().Be("text-embedding-3-small");
        factory.RequestedModels.Should().Equal("text-embedding-3-small");
    }

    [Fact]
    public async Task GenerateAsync_NoModelIdOverride_UsesConfiguredModel()
    {
        var models = new List<string>();
        var handler = new StatefulHandler(req => BuildEmbeddingResponse(req, capturedModels: models));
        var factory = new RecordingClientFactory(handler, "test-key");
        var generator = BuildGeneratorWithFactory(factory, defaultModel: "text-embedding-3-large");

        await generator.GenerateAsync(MakeInputs(1));

        models.Should().ContainSingle().Which.Should().Be("text-embedding-3-large");
        factory.RequestedModels.Should().Equal("text-embedding-3-large");
    }

    [Fact]
    public async Task GenerateAsync_SameModelIdTwice_ConstructsClientOnce()
    {
        var handler = new StatefulHandler(req => BuildEmbeddingResponse(req));
        var factory = new RecordingClientFactory(handler, "test-key");
        var generator = BuildGeneratorWithFactory(factory, defaultModel: "text-embedding-3-large");

        await generator.GenerateAsync(MakeInputs(1), new MeaiOptions { ModelId = "text-embedding-3-small" });
        await generator.GenerateAsync(new List<string> { "text-1" }, new MeaiOptions { ModelId = "text-embedding-3-small" });

        handler.CallCount.Should().Be(2, "each call still issues its own HTTP request");
        factory.RequestedModels.Should().Equal("text-embedding-3-small");
    }

    [Fact]
    public async Task GenerateAsync_DifferentModelIds_ResolveIndependently()
    {
        var models = new List<string>();
        var handler = new StatefulHandler(req => BuildEmbeddingResponse(req, capturedModels: models));
        var factory = new RecordingClientFactory(handler, "test-key");
        var generator = BuildGeneratorWithFactory(factory, defaultModel: "text-embedding-3-large");

        await generator.GenerateAsync(MakeInputs(1), new MeaiOptions { ModelId = "text-embedding-3-small" });
        await generator.GenerateAsync(new List<string> { "text-1" }, new MeaiOptions { ModelId = "text-embedding-3-large" });
        await generator.GenerateAsync(new List<string> { "text-2" }, new MeaiOptions { ModelId = "text-embedding-3-small" });

        models.Should().Equal("text-embedding-3-small", "text-embedding-3-large", "text-embedding-3-small");
        factory.RequestedModels.Should().Equal(
            "text-embedding-3-small", "text-embedding-3-large");
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.OpenAI.Tests/Anela.Heblo.Adapters.OpenAI.Tests.csproj \
  --filter "FullyQualifiedName~ModelIdOverride|FullyQualifiedName~NoModelIdOverride|FullyQualifiedName~SameModelIdTwice|FullyQualifiedName~DifferentModelIds"
```

Expected: FAIL at build with `error CS1739: The best overload for 'OpenAiEmbeddingGenerator' does not have a parameter named 'clientFactory'` — the factory seam does not exist yet.

- [ ] **Step 4: Implement the per-model cache and model resolution**

Replace the entire contents of `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiEmbeddingGenerator.cs` with:

```csharp
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
```

- [ ] **Step 5: Run the full adapter test project**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.OpenAI.Tests/Anela.Heblo.Adapters.OpenAI.Tests.csproj
```

Expected: PASS — 13 tests. In particular the 7 original tests still pass unmodified, proving the injected client is still reached via the `_options.EmbeddingModel` cache key.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiEmbeddingGenerator.cs \
        backend/test/Anela.Heblo.Adapters.OpenAI.Tests/OpenAiEmbeddingGeneratorTests.cs
git commit -m "fix(openai): honor EmbeddingGenerationOptions.ModelId via per-model client cache"
```

---
