# Implementation Plan: Honor per-call embedding model/dimensions overrides in `OpenAiEmbeddingGenerator`

**Feature:** feat-3895 — `OpenAiEmbeddingGenerator.GenerateAsync` must honor `EmbeddingGenerationOptions.ModelId` / `.Dimensions`

**Goal:** `OpenAiEmbeddingGenerator.GenerateAsync` currently accepts a `Microsoft.Extensions.AI.EmbeddingGenerationOptions` parameter and silently ignores it, so every embedding call in the app uses one model/dimensions pair bound at DI time from the `KnowledgeBase:*` config section. This plan makes the adapter resolve `model`/`dimensions` per call (`options?.X ?? _options.X`, exactly mirroring `AnthropicChatClient.GetResponseAsync:77`), makes every Application-layer call site pass its own feature config, and renames the adapter's fallback config binding from `KnowledgeBase:*` to the neutral `OpenAI:*` keys so the config's apparent scope matches its real scope.

**Architecture summary:**

- `OpenAiEmbeddingGenerator` (Adapters layer) stays the only component doing HTTP/SDK I/O, per `docs/architecture/filesystem.md:158`. Its single `Lazy<EmbeddingClient>` field becomes a `ConcurrentDictionary<string, Lazy<EmbeddingClient>>` keyed by resolved model id — the OpenAI SDK binds `EmbeddingClient` to one model at construction, so serving more than one model without rebuilding an HTTP pipeline per call requires a per-model cache. Each entry is wrapped in `Lazy<T>` (not a bare value from `GetOrAdd`'s factory) so the `EmbeddingClient` constructor runs at most once per model even under concurrent first use.
- Application-layer call sites gain **only** options construction (no I/O, no model-resolution logic). All of them go through a new `RagFeatureOptions.ToEmbeddingOptions()` helper, mirroring the existing `RagFeatureOptions.ToExpansionConfig()` helper on the same class (arch review, Decision 2 / Spec Amendment 2).
- `AddOpenAiAdapter` binds the adapter's fallback default from `OpenAI:EmbeddingModel` / `OpenAI:EmbeddingDimensions` (parity with the adjacent `OpenAI:ApiKey` line and with `AnthropicAdapterServiceCollectionExtensions`'s `Anthropic:*` binding). Neither key exists in `appsettings*.json`, so the class defaults `"text-embedding-3-large"` / `1536` apply — identical to what the adapter resolved before, so no re-indexing is triggered (NFR-3).

**Tech stack:** .NET 8, xUnit + Moq + FluentAssertions, `Microsoft.Extensions.AI` 9.5.0, `OpenAI` SDK 2.8.0, Polly 8.4.1.

---

## Deviations from the spec (read before starting)

Two are deliberate and both are grounded in the input artifacts or in verified repository facts:

1. **Internal test-seam extension (arch review Spec Amendment 1 / Risks row 3).** The spec's FR-1 acceptance criteria require verifying that an override actually changes which model is called and that different model ids resolve independently. The existing test seam injects exactly one pre-built `EmbeddingClient`, which after seeding is only reachable for the default model — an override would fall through to a **real** `new EmbeddingClient(...)` against `api.openai.com`. Task `add-per-model-embedding-client-cache` therefore adds an **optional `Func<string, EmbeddingClient>? clientFactory` parameter to the existing `internal` constructor**. This is internal-only (the assembly already has `[assembly: InternalsVisibleTo("Anela.Heblo.Adapters.OpenAI.Tests")]` in `AssemblyInfo.cs`), the parameter is optional so the existing `BuildGenerator` call compiles unmodified, and no public signature changes — NFR-2 holds.

2. **Two call sites the spec and arch review missed.** The spec names three consumers of `IEmbeddingGenerator`. A grep of `backend/src` for `GenerateAsync` finds **five**:
   - `KnowledgeBaseDocIndexingStrategy.CreateChunksAsync:44` (in spec, FR-4)
   - `LeafletIndexingService.IndexAsync:61` (in spec, FR-3)
   - `GenerateLeafletHandler.Handle:51` (in spec, FR-3)
   - **`ConversationIndexingStrategy.CreateChunksAsync:30` (NOT in spec)**
   - **`SearchDocumentsHandler.Handle:45` (NOT in spec)**

   Both missing call sites are KnowledgeBase-side and both rely on the adapter default today. FR-5 removes the `KnowledgeBase:*` binding on the premise that "no current caller relies on this value" — which is false for these two. Left unchanged, an operator editing `KnowledgeBase:EmbeddingDimensions` would silently stop affecting conversation indexing and KB search, and their query vectors would drift from their indexed vectors. That directly contradicts FR-5's third acceptance criterion (`KnowledgeBaseOptions.EmbeddingDimensions` remains the KnowledgeBase feature's own source of truth) and the arch review's mitigation "Land FR-3/FR-4 in the same change as FR-5 … so there is no intermediate state where a caller silently regresses to the old default". Tasks `pass-embedding-options-from-conversation-indexing-strategy` and `pass-embedding-options-from-search-documents-handler` extend FR-4's treatment to them. `ConversationIndexingStrategy` does not currently inject any options, so it gains an `IOptions<KnowledgeBaseOptions>` constructor parameter (it is registered as `services.AddScoped<IIndexingStrategy, ConversationIndexingStrategy>()` in `KnowledgeBaseModule.cs:33`, and `IOptions<KnowledgeBaseOptions>` is already resolvable — `KnowledgeBaseDocIndexingStrategy`, registered one line above, already takes it).

Task ordering matters: every call site is converted **before** the config rename lands, so there is never a commit in which a caller silently regresses to the adapter default.

All commands below are run from the repository root (the worktree root, which contains `Anela.Heblo.sln`).

---

## File Structure

**Modified — production:**

| File | Responsibility after this change |
|---|---|
| `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiEmbeddingGenerator.cs` | Per-call model/dimension resolution + per-model `EmbeddingClient` cache |
| `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiAdapterServiceCollectionExtensions.cs` | Binds the adapter fallback from neutral `OpenAI:*` keys |
| `backend/src/Anela.Heblo.Application/Shared/Rag/RagFeatureOptions.cs` | Adds `ToEmbeddingOptions()`, the single place feature options become an `EmbeddingGenerationOptions` |
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategy.cs` | Passes KnowledgeBase's own model/dimensions |
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ConversationIndexingStrategy.cs` | Injects `IOptions<KnowledgeBaseOptions>`; passes KnowledgeBase's own model/dimensions |
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/SearchDocuments/SearchDocumentsHandler.cs` | Passes KnowledgeBase's own model/dimensions for the query vector |
| `backend/src/Anela.Heblo.Application/Features/Leaflet/Services/LeafletIndexingService.cs` | Passes Leaflet's own model/dimensions |
| `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs` | Passes Leaflet's own model/dimensions for the topic vector |

**Modified / created — tests:**

| File | Responsibility |
|---|---|
| `backend/test/Anela.Heblo.Adapters.OpenAI.Tests/OpenAiEmbeddingGeneratorTests.cs` | Override + cache behavior (FR-1, FR-2) |
| `backend/test/Anela.Heblo.Adapters.OpenAI.Tests/OpenAiAdapterServiceCollectionExtensionsTests.cs` *(new)* | Config binding source (FR-5) |
| `backend/test/Anela.Heblo.Adapters.OpenAI.Tests/Anela.Heblo.Adapters.OpenAI.Tests.csproj` | Adds `Microsoft.Extensions.Configuration` / `Microsoft.Extensions.DependencyInjection` for the binding test |
| `backend/test/Anela.Heblo.Tests/Shared/Rag/RagFeatureOptionsTests.cs` | `ToEmbeddingOptions()` behavior |
| `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategyTests.cs` | Options pass-through assertion |
| `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/ConversationIndexingStrategyTests.cs` | Constructor update + options pass-through assertion |
| `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/SearchDocumentsHandlerTests.cs` | Options pass-through assertion |
| `backend/test/Anela.Heblo.Tests/Features/Leaflet/Services/LeafletIndexingServiceTests.cs` | Options pass-through assertion |
| `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GenerateLeafletHandlerTests.cs` | Options pass-through assertion |

No new production files. No migrations. No `appsettings*.json` edits.

---

### task: honor-options-dimensions-in-embedding-generator

Implements FR-2. Smallest slice: no cache needed yet, only the `Dimensions` fallback. Also lands the shared test-helper change that lets later tasks read `model`/`dimensions` off the outgoing request body.

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiEmbeddingGenerator.cs:65`
- Test: `backend/test/Anela.Heblo.Adapters.OpenAI.Tests/OpenAiEmbeddingGeneratorTests.cs`

- [ ] **Step 1: Add the `MeaiOptions` alias to the test file's usings**

The test file already has `using OpenAI.Embeddings;`, whose `EmbeddingGenerationOptions` collides with `Microsoft.Extensions.AI.EmbeddingGenerationOptions`. Alias it exactly as the production file does.

In `backend/test/Anela.Heblo.Adapters.OpenAI.Tests/OpenAiEmbeddingGeneratorTests.cs`, replace lines 1-9:

```csharp
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Embeddings;
```

with:

```csharp
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Embeddings;
using MeaiOptions = Microsoft.Extensions.AI.EmbeddingGenerationOptions;
```

- [ ] **Step 2: Extend the `BuildEmbeddingResponse` helper to capture `model` and `dimensions`**

The new parameters are optional and appended, so the seven existing test methods (`BuildEmbeddingResponse(req)` / `BuildEmbeddingResponse(req, reverseOrder: true)`) stay unmodified.

In the same file, replace the whole `BuildEmbeddingResponse` method (lines 31-68 of the original file) with:

```csharp
    // Reads the "input" array off the outgoing request body and returns embeddings whose
    // vector encodes the numeric suffix of each input string (e.g. "text-7" -> [7,7,7]),
    // so assertions can tell "the embedding for input N" apart from array position alone.
    // When capturedModels/capturedDimensions are supplied, the "model"/"dimensions" fields of
    // the same request body are recorded so per-call override tests can assert on what was sent.
    private static HttpResponseMessage BuildEmbeddingResponse(
        HttpRequestMessage request,
        bool reverseOrder = false,
        List<string>? capturedModels = null,
        List<int?>? capturedDimensions = null)
    {
        var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(body);

        capturedModels?.Add(doc.RootElement.GetProperty("model").GetString()!);
        capturedDimensions?.Add(
            doc.RootElement.TryGetProperty("dimensions", out var dimensionsElement)
                ? dimensionsElement.GetInt32()
                : null);

        var inputs = doc.RootElement.GetProperty("input").EnumerateArray()
            .Select(e => e.GetString()!)
            .ToList();

        var items = Enumerable.Range(0, inputs.Count).Select(i =>
        {
            var id = double.Parse(inputs[i].Split('-').Last());
            return new { index = i, id };
        }).ToList();

        if (reverseOrder)
            items.Reverse();

        var payload = new
        {
            @object = "list",
            data = items.Select(item => new
            {
                @object = "embedding",
                index = item.index,
                embedding = new[] { item.id, item.id, item.id },
            }),
            model = "text-embedding-3-small",
            usage = new { prompt_tokens = inputs.Count, total_tokens = inputs.Count },
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json"),
        };
    }
```

- [ ] **Step 3: Write the failing tests**

Append these two tests inside the `OpenAiEmbeddingGeneratorTests` class, after `GenerateAsync_CalledTwice_ReusesSameClient`:

```csharp
    [Fact]
    public async Task GenerateAsync_DimensionsOverride_SendsOverriddenDimensions()
    {
        var dimensions = new List<int?>();
        var handler = new StatefulHandler(req => BuildEmbeddingResponse(req, capturedDimensions: dimensions));
        var generator = BuildGenerator(handler, dimensions: 3);

        await generator.GenerateAsync(MakeInputs(1), new MeaiOptions { Dimensions = 3072 });

        dimensions.Should().ContainSingle().Which.Should().Be(3072);
    }

    [Fact]
    public async Task GenerateAsync_NoDimensionsOverride_SendsConfiguredDimensions()
    {
        var dimensions = new List<int?>();
        var handler = new StatefulHandler(req => BuildEmbeddingResponse(req, capturedDimensions: dimensions));
        var generator = BuildGenerator(handler, dimensions: 7);

        await generator.GenerateAsync(MakeInputs(1));

        dimensions.Should().ContainSingle().Which.Should().Be(7);
    }
```

- [ ] **Step 4: Run the tests to verify the override test fails**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.OpenAI.Tests/Anela.Heblo.Adapters.OpenAI.Tests.csproj \
  --filter "FullyQualifiedName~GenerateAsync_DimensionsOverride_SendsOverriddenDimensions|FullyQualifiedName~GenerateAsync_NoDimensionsOverride_SendsConfiguredDimensions"
```

Expected: `GenerateAsync_NoDimensionsOverride_SendsConfiguredDimensions` PASSES (current behavior already sends `_options.EmbeddingDimensions`), `GenerateAsync_DimensionsOverride_SendsOverriddenDimensions` FAILS with `Expected value to be 3072, but found 3.`

- [ ] **Step 5: Implement the `Dimensions` fallback**

In `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiEmbeddingGenerator.cs`, replace line 65:

```csharp
        var embeddingOptions = new global::OpenAI.Embeddings.EmbeddingGenerationOptions { Dimensions = _options.EmbeddingDimensions };
```

with:

```csharp
        var dimensions = options?.Dimensions ?? _options.EmbeddingDimensions;
        var embeddingOptions = new global::OpenAI.Embeddings.EmbeddingGenerationOptions { Dimensions = dimensions };
```

- [ ] **Step 6: Run the full adapter test project**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.OpenAI.Tests/Anela.Heblo.Adapters.OpenAI.Tests.csproj
```

Expected: PASS — 9 tests (the 7 pre-existing ones plus the 2 new ones).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiEmbeddingGenerator.cs \
        backend/test/Anela.Heblo.Adapters.OpenAI.Tests/OpenAiEmbeddingGeneratorTests.cs
git commit -m "fix(openai): honor EmbeddingGenerationOptions.Dimensions per call"
```

---

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

### task: add-ragfeatureoptions-toembeddingoptions-helper

Implements arch review Decision 2 / Spec Amendment 2 — the single place per-feature options become an `EmbeddingGenerationOptions`, mirroring the existing `ToExpansionConfig()` helper on the same class. Every call-site task below consumes it.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/Rag/RagFeatureOptions.cs`
- Test: `backend/test/Anela.Heblo.Tests/Shared/Rag/RagFeatureOptionsTests.cs`

- [ ] **Step 1: Write the failing tests**

In `backend/test/Anela.Heblo.Tests/Shared/Rag/RagFeatureOptionsTests.cs`, add `using Microsoft.Extensions.AI;` to the usings block so it reads:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.Leaflet;
using Anela.Heblo.Application.Shared.Rag;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Xunit;
```

Then add these two tests inside the `RagFeatureOptionsTests` class, after `RagFeatureOptions_BaseDefault_HasEmptyPrompt` and before the `ConcreteRagOptions` nested class:

```csharp
    [Fact]
    public void ToEmbeddingOptions_CarriesConfiguredModelAndDimensions()
    {
        var options = new LeafletOptions
        {
            EmbeddingModel = "text-embedding-3-small",
            EmbeddingDimensions = 3072,
        };

        var embeddingOptions = options.ToEmbeddingOptions();

        embeddingOptions.ModelId.Should().Be("text-embedding-3-small");
        embeddingOptions.Dimensions.Should().Be(3072);
    }

    [Fact]
    public void ToEmbeddingOptions_UnsetValues_FallBackToClassDefaults()
    {
        var options = new KnowledgeBaseOptions();

        var embeddingOptions = options.ToEmbeddingOptions();

        embeddingOptions.ModelId.Should().Be("text-embedding-3-large");
        embeddingOptions.Dimensions.Should().Be(1536);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ToEmbeddingOptions"
```

Expected: FAIL at build with `error CS1061: 'LeafletOptions' does not contain a definition for 'ToEmbeddingOptions'`.

- [ ] **Step 3: Implement the helper**

In `backend/src/Anela.Heblo.Application/Shared/Rag/RagFeatureOptions.cs`, replace line 1:

```csharp
using System.ComponentModel.DataAnnotations;
```

with:

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.AI;
```

and replace lines 35-36:

```csharp
    public RagQueryExpansionConfig ToExpansionConfig() =>
        new(QueryExpansionEnabled, QueryExpansionModel, QueryExpansionPrompt);
```

with:

```csharp
    public RagQueryExpansionConfig ToExpansionConfig() =>
        new(QueryExpansionEnabled, QueryExpansionModel, QueryExpansionPrompt);

    /// <summary>
    /// Turns this feature's embedding config into the per-call options every
    /// <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> call site must pass, so a feature's
    /// own EmbeddingModel/EmbeddingDimensions reach the API instead of the adapter-wide fallback.
    /// </summary>
    public EmbeddingGenerationOptions ToEmbeddingOptions() =>
        new() { ModelId = EmbeddingModel, Dimensions = EmbeddingDimensions };
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~RagFeatureOptionsTests"
```

Expected: PASS — 5 tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/Rag/RagFeatureOptions.cs \
        backend/test/Anela.Heblo.Tests/Shared/Rag/RagFeatureOptionsTests.cs
git commit -m "feat(rag): add RagFeatureOptions.ToEmbeddingOptions helper"
```

---

### task: pass-embedding-options-from-knowledgebase-indexing-strategy

Implements FR-4.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategy.cs:44`
- Test: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategyTests.cs`

- [ ] **Step 1: Write the failing test**

Append this test inside the `KnowledgeBaseDocIndexingStrategyTests` class, after `CreateChunksAsync_NoChunksProduced_ReturnsEmptyListAndDoesNotCallEmbeddingGenerator`:

```csharp
    [Fact]
    public async Task CreateChunksAsync_PassesKnowledgeBaseModelAndDimensionsToEmbeddingGenerator()
    {
        EmbeddingGenerationOptions? capturedOptions = null;
        _embeddingGenerator
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, EmbeddingGenerationOptions?, CancellationToken>(
                (_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(_generatedEmbeddings);

        var options = Options.Create(new KnowledgeBaseOptions
        {
            ChunkSize = 512,
            ChunkOverlap = 50,
            EmbeddingModel = "text-embedding-3-small",
            EmbeddingDimensions = 3072,
        });
        var strategy = new KnowledgeBaseDocIndexingStrategy(
            new WordWindowChunker(),
            _summarizer.Object,
            _embeddingGenerator.Object,
            options);

        await strategy.CreateChunksAsync("word1 word2 word3", Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(capturedOptions);
        Assert.Equal("text-embedding-3-small", capturedOptions!.ModelId);
        Assert.Equal(3072, capturedOptions.Dimensions);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CreateChunksAsync_PassesKnowledgeBaseModelAndDimensionsToEmbeddingGenerator"
```

Expected: FAIL with `Assert.NotNull() Failure: Value is null` — no options are passed today.

- [ ] **Step 3: Pass the feature's options**

In `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategy.cs`, replace line 44:

```csharp
        var embeddings = await _embeddingGenerator.GenerateAsync(summaries, cancellationToken: ct);
```

with:

```csharp
        var embeddings = await _embeddingGenerator.GenerateAsync(summaries, _options.ToEmbeddingOptions(), ct);
```

- [ ] **Step 4: Run the strategy's tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~KnowledgeBaseDocIndexingStrategyTests"
```

Expected: PASS — 7 tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategy.cs \
        backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategyTests.cs
git commit -m "fix(knowledgebase): pass feature embedding model/dimensions when indexing docs"
```

---

### task: pass-embedding-options-from-conversation-indexing-strategy

Extends FR-4 to the fourth call site (`ConversationIndexingStrategy.CreateChunksAsync:30`), which the spec missed. Without this, the `KnowledgeBase:*` config rename in the last task would silently detach conversation indexing from KnowledgeBase's own embedding settings. This class has no options today, so it gains an `IOptions<KnowledgeBaseOptions>` constructor parameter — resolvable in DI already (`KnowledgeBaseModule.cs:32` registers `KnowledgeBaseDocIndexingStrategy`, which takes the same dependency, one line above `ConversationIndexingStrategy`).

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ConversationIndexingStrategy.cs`
- Test: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/ConversationIndexingStrategyTests.cs`

- [ ] **Step 1: Update the test fixture to construct the strategy with options, and write the failing test**

In `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/ConversationIndexingStrategyTests.cs`, replace the usings block (lines 1-6):

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Domain.Shared.Rag;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;
```

with:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Domain.Shared.Rag;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
```

Then replace the strategy construction at the end of the test-class constructor:

```csharp
        _strategy = new ConversationIndexingStrategy(
            _summarizer.Object,
            _embeddingGenerator.Object);
```

with:

```csharp
        _strategy = new ConversationIndexingStrategy(
            _summarizer.Object,
            _embeddingGenerator.Object,
            Options.Create(new KnowledgeBaseOptions()));
```

Then append this test inside the `ConversationIndexingStrategyTests` class, after `CreateChunksAsync_EmbeddingInputIsTopicSummary_NotFullText`:

```csharp
    [Fact]
    public async Task CreateChunksAsync_PassesKnowledgeBaseModelAndDimensionsToEmbeddingGenerator()
    {
        _summarizer
            .Setup(s => s.SummarizeTopicsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "Problém zákazníka: akné" });

        EmbeddingGenerationOptions? capturedOptions = null;
        _embeddingGenerator
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, EmbeddingGenerationOptions?, CancellationToken>(
                (_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(_generatedEmbeddings);

        var strategy = new ConversationIndexingStrategy(
            _summarizer.Object,
            _embeddingGenerator.Object,
            Options.Create(new KnowledgeBaseOptions
            {
                EmbeddingModel = "text-embedding-3-small",
                EmbeddingDimensions = 3072,
            }));

        await strategy.CreateChunksAsync("transcript", Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(capturedOptions);
        Assert.Equal("text-embedding-3-small", capturedOptions!.ModelId);
        Assert.Equal(3072, capturedOptions.Dimensions);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ConversationIndexingStrategyTests"
```

Expected: FAIL at build with `error CS1729: 'ConversationIndexingStrategy' does not contain a constructor that takes 3 arguments`.

- [ ] **Step 3: Inject the options and pass them through**

Replace the entire contents of `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ConversationIndexingStrategy.cs` with:

```csharp
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Domain.Shared.Rag;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class ConversationIndexingStrategy : IIndexingStrategy
{
    private readonly IConversationTopicSummarizer _summarizer;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly KnowledgeBaseOptions _options;

    public ConversationIndexingStrategy(
        IConversationTopicSummarizer summarizer,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IOptions<KnowledgeBaseOptions> options)
    {
        _summarizer = summarizer;
        _embeddingGenerator = embeddingGenerator;
        _options = options.Value;
    }

    public bool Supports(DocumentType documentType) =>
        documentType == DocumentType.Conversation;

    public async Task<IReadOnlyList<KnowledgeBaseChunk>> CreateChunksAsync(
        string cleanText, Guid documentId, CancellationToken ct)
    {
        var topics = await _summarizer.SummarizeTopicsAsync(cleanText, ct);
        if (topics.Count == 0)
            return [];

        var embeddings = await _embeddingGenerator.GenerateAsync(topics, _options.ToEmbeddingOptions(), ct);
        var chunks = new List<KnowledgeBaseChunk>(topics.Count);

        for (var i = 0; i < topics.Count; i++)
        {
            chunks.Add(new KnowledgeBaseChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                ChunkIndex = i,
                Content = cleanText,
                Summary = topics[i],
                DocumentType = DocumentType.Conversation,
                Embedding = embeddings[i].Vector.ToArray(),
            });
        }

        return chunks;
    }
}
```

Note: `ConversationIndexingStrategy` lives in namespace `Anela.Heblo.Application.Features.KnowledgeBase.Services`, so `KnowledgeBaseOptions` (namespace `Anela.Heblo.Application.Features.KnowledgeBase`) resolves without an extra `using`, and `ToEmbeddingOptions()` is inherited from `RagFeatureOptions`.

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ConversationIndexingStrategyTests"
```

Expected: PASS — 7 tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ConversationIndexingStrategy.cs \
        backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/ConversationIndexingStrategyTests.cs
git commit -m "fix(knowledgebase): pass feature embedding model/dimensions when indexing conversations"
```

---

### task: pass-embedding-options-from-search-documents-handler

Extends FR-4 to the fifth call site (`SearchDocumentsHandler.Handle:45`), which the spec missed. This is the KnowledgeBase query-time embedding — the vector it produces is compared against `KnowledgeBaseChunks.Embedding`, so it must be generated with the same model/dimensions the indexing strategies now use. `KnowledgeBaseOptions` is already injected here, so this is a one-line change.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/SearchDocuments/SearchDocumentsHandler.cs:45-47`
- Test: `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/SearchDocumentsHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

Append this test inside the `SearchDocumentsHandlerTests` class (at the end of the class):

```csharp
    [Fact]
    public async Task Handle_PassesKnowledgeBaseModelAndDimensionsToEmbeddingGenerator()
    {
        var vector = new ReadOnlyMemory<float>(new float[] { 0.1f, 0.2f, 0.3f });
        var generated = new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(vector)]);

        EmbeddingGenerationOptions? capturedOptions = null;
        _embeddingGenerator
            .Setup(s => s.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, EmbeddingGenerationOptions?, CancellationToken>(
                (_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(generated);

        _expander
            .Setup(e => e.ExpandAsync(It.IsAny<string>(), It.IsAny<RagQueryExpansionConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string q, RagQueryExpansionConfig _, CancellationToken _) => q);

        _repository
            .Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var options = Options.Create(new KnowledgeBaseOptions
        {
            QueryExpansionPrompt = "Expand:",
            EmbeddingModel = "text-embedding-3-small",
            EmbeddingDimensions = 3072,
        });
        var handler = new SearchDocumentsHandler(
            _embeddingGenerator.Object, _repository.Object, options, _expander.Object, _recorder, _logger.Object);

        await handler.Handle(new SearchDocumentsRequest { Query = "phenoxyethanol", TopK = 5 }, default);

        Assert.NotNull(capturedOptions);
        Assert.Equal("text-embedding-3-small", capturedOptions!.ModelId);
        Assert.Equal(3072, capturedOptions.Dimensions);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Handle_PassesKnowledgeBaseModelAndDimensionsToEmbeddingGenerator"
```

Expected: FAIL with `Assert.NotNull() Failure: Value is null`.

- [ ] **Step 3: Pass the feature's options**

In `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/SearchDocuments/SearchDocumentsHandler.cs`, replace lines 45-47:

```csharp
            var embeddings = await _embeddingGenerator.GenerateAsync(
                [queryToEmbed],
                cancellationToken: cancellationToken);
```

with:

```csharp
            var embeddings = await _embeddingGenerator.GenerateAsync(
                [queryToEmbed],
                _options.ToEmbeddingOptions(),
                cancellationToken);
```

- [ ] **Step 4: Run the handler's tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~SearchDocumentsHandlerTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/SearchDocuments/SearchDocumentsHandler.cs \
        backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/SearchDocumentsHandlerTests.cs
git commit -m "fix(knowledgebase): pass feature embedding model/dimensions for search query vector"
```

---

### task: pass-embedding-options-from-leaflet-indexing-service

Implements the indexing half of FR-3. This is what makes `Leaflet:EmbeddingModel` (already `"text-embedding-3-large"` in both `appsettings.json:212` and `appsettings.Production.json:109`) a live setting for the first time.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/Services/LeafletIndexingService.cs:61`
- Test: `backend/test/Anela.Heblo.Tests/Features/Leaflet/Services/LeafletIndexingServiceTests.cs`

- [ ] **Step 1: Write the failing test**

Append this test inside the `LeafletIndexingServiceTests` class, after `IndexAsync_sets_Summary_from_summarizer`:

```csharp
    [Fact]
    public async Task IndexAsync_passes_leaflet_model_and_dimensions_to_embedding_generator()
    {
        // Arrange
        var document = CreateDocument();
        var options = new LeafletOptions
        {
            ChunkSize = 800,
            ChunkOverlap = 80,
            EmbeddingModel = "text-embedding-3-small",
            EmbeddingDimensions = 3072,
        };
        var service = new LeafletIndexingService(
            _chunker.Object,
            _embeddings.Object,
            _summarizer.Object,
            _repo.Object,
            _logger.Object,
            Options.Create(options));

        _chunker
            .Setup(c => c.Chunk(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new[] { "chunk content 0" });

        EmbeddingGenerationOptions? capturedOptions = null;
        _embeddings
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, EmbeddingGenerationOptions?, CancellationToken>(
                (_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(CreateEmbeddings(1));

        _repo
            .Setup(r => r.AddChunksAsync(It.IsAny<IEnumerable<LeafletChunk>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await service.IndexAsync("some text content", document);

        // Assert
        capturedOptions.Should().NotBeNull();
        capturedOptions!.ModelId.Should().Be("text-embedding-3-small");
        capturedOptions.Dimensions.Should().Be(3072);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~IndexAsync_passes_leaflet_model_and_dimensions_to_embedding_generator"
```

Expected: FAIL with `Expected capturedOptions not to be <null>.`

- [ ] **Step 3: Pass the feature's options**

In `backend/src/Anela.Heblo.Application/Features/Leaflet/Services/LeafletIndexingService.cs`, replace line 61:

```csharp
        var generated = await _embeddings.GenerateAsync(inputs, cancellationToken: ct);
```

with:

```csharp
        var generated = await _embeddings.GenerateAsync(inputs, _options.ToEmbeddingOptions(), ct);
```

- [ ] **Step 4: Run the service's tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~LeafletIndexingServiceTests"
```

Expected: PASS — 6 tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Leaflet/Services/LeafletIndexingService.cs \
        backend/test/Anela.Heblo.Tests/Features/Leaflet/Services/LeafletIndexingServiceTests.cs
git commit -m "fix(leaflet): pass Leaflet embedding model/dimensions when indexing leaflets"
```

---

### task: pass-embedding-options-from-generate-leaflet-handler

Implements the query-time half of FR-3, so the topic vector is produced with the same model/dimensions that `LeafletChunks` were indexed with — matching what `ChatOptions { ModelId = _options.ChatModel }` already does for chat on line 102 of the same file.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs:50-54`
- Test: `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GenerateLeafletHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

Append this test inside the `GenerateLeafletHandlerTests` class (at the end of the class):

```csharp
    [Fact]
    public async Task Handle_passes_leaflet_model_and_dimensions_to_topic_embedding()
    {
        // Arrange
        EmbeddingGenerationOptions? capturedOptions = null;
        _embeddings
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, EmbeddingGenerationOptions?, CancellationToken>(
                (_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(
                [new Embedding<float>(new ReadOnlyMemory<float>(DefaultVector))]));
        SetupChatReturns();

        _kb.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeSearchResult> { KbHit(0.9) });
        _leaflets.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([LeafletHit(0.9)]);

        var handler = CreateHandler(new LeafletOptions
        {
            EmbeddingModel = "text-embedding-3-small",
            EmbeddingDimensions = 3072,
        });
        var request = new GenerateLeafletRequest { Topic = "retinol", Audience = AudienceType.EndConsumer, Length = LeafletLength.Short };

        // Act
        await handler.Handle(request, CancellationToken.None);

        // Assert
        capturedOptions.Should().NotBeNull();
        capturedOptions!.ModelId.Should().Be("text-embedding-3-small");
        capturedOptions.Dimensions.Should().Be(3072);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Handle_passes_leaflet_model_and_dimensions_to_topic_embedding"
```

Expected: FAIL with `Expected capturedOptions not to be <null>.`

- [ ] **Step 3: Pass the feature's options**

In `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs`, replace lines 50-54:

```csharp
        var topicVector = (await ChatRetry.RetryOnceAsync(
                () => _embeddings.GenerateAsync([queryToEmbed], cancellationToken: ct),
                _logger,
                ct))
            .First().Vector.ToArray();
```

with:

```csharp
        var embeddingOptions = _options.ToEmbeddingOptions();

        var topicVector = (await ChatRetry.RetryOnceAsync(
                () => _embeddings.GenerateAsync([queryToEmbed], embeddingOptions, ct),
                _logger,
                ct))
            .First().Vector.ToArray();
```

- [ ] **Step 4: Run the handler's tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GenerateLeafletHandlerTests"
```

Expected: PASS — 14 tests (the 11 pre-existing facts/theory cases plus the new one; theory cases count individually).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GenerateLeafletHandlerTests.cs
git commit -m "fix(leaflet): pass Leaflet embedding model/dimensions for topic query vector"
```

---

### task: rebind-adapter-embedding-defaults-to-openai-config-keys

Implements FR-5. Every call site now passes its own options, so the adapter's DI-time binding is a pure fallback for future consumers and must stop being named after the KnowledgeBase feature. Binds from `OpenAI:EmbeddingModel` / `OpenAI:EmbeddingDimensions`, matching the adjacent `OpenAI:ApiKey` line and `AnthropicAdapterServiceCollectionExtensions`'s `Anthropic:*` convention. Neither key exists in `appsettings*.json`, so the class defaults (`"text-embedding-3-large"` / `1536`) apply — the same values the old `KnowledgeBase:*` binding resolved to.

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiAdapterServiceCollectionExtensions.cs:16-17`
- Modify: `backend/test/Anela.Heblo.Adapters.OpenAI.Tests/Anela.Heblo.Adapters.OpenAI.Tests.csproj`
- Create: `backend/test/Anela.Heblo.Adapters.OpenAI.Tests/OpenAiAdapterServiceCollectionExtensionsTests.cs`

- [ ] **Step 1: Add the two packages the binding test needs**

The test project transitively has `Microsoft.Extensions.Configuration.Abstractions`/`.Binder` and `Microsoft.Extensions.DependencyInjection.Abstractions`, but not the concrete `ConfigurationBuilder` / `ServiceCollection` types. Versions match those already used elsewhere in `backend/`.

In `backend/test/Anela.Heblo.Adapters.OpenAI.Tests/Anela.Heblo.Adapters.OpenAI.Tests.csproj`, replace:

```xml
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.1" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="8.0.2" />
```

with:

```xml
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.1" />
    <PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.1" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="8.0.2" />
```

- [ ] **Step 2: Write the failing tests**

Create `backend/test/Anela.Heblo.Adapters.OpenAI.Tests/OpenAiAdapterServiceCollectionExtensionsTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.OpenAI.Tests;

public class OpenAiAdapterServiceCollectionExtensionsTests
{
    // Resolves only IOptions<OpenAiEmbeddingOptions>; the IEmbeddingGenerator registration is left
    // unresolved on purpose so no ILoggerFactory/ILogger registration is needed here.
    private static OpenAiEmbeddingOptions BindOptions(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

        var services = new ServiceCollection();
        services.AddOpenAiAdapter(configuration);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<OpenAiEmbeddingOptions>>().Value;
    }

    [Fact]
    public void AddOpenAiAdapter_NoEmbeddingKeys_UsesClassDefaults()
    {
        var options = BindOptions(("OpenAI:ApiKey", "test-key"));

        options.ApiKey.Should().Be("test-key");
        options.EmbeddingModel.Should().Be("text-embedding-3-large");
        options.EmbeddingDimensions.Should().Be(1536);
    }

    [Fact]
    public void AddOpenAiAdapter_OpenAiEmbeddingKeys_OverrideClassDefaults()
    {
        var options = BindOptions(
            ("OpenAI:ApiKey", "test-key"),
            ("OpenAI:EmbeddingModel", "text-embedding-3-small"),
            ("OpenAI:EmbeddingDimensions", "512"));

        options.EmbeddingModel.Should().Be("text-embedding-3-small");
        options.EmbeddingDimensions.Should().Be(512);
    }

    [Fact]
    public void AddOpenAiAdapter_KnowledgeBaseEmbeddingKeys_AreIgnored()
    {
        var options = BindOptions(
            ("OpenAI:ApiKey", "test-key"),
            ("KnowledgeBase:EmbeddingModel", "text-embedding-3-small"),
            ("KnowledgeBase:EmbeddingDimensions", "512"));

        options.EmbeddingModel.Should().Be(
            "text-embedding-3-large",
            "the adapter fallback must no longer be scoped to the KnowledgeBase feature's config");
        options.EmbeddingDimensions.Should().Be(1536);
    }
}
```

- [ ] **Step 3: Run the tests to verify the `KnowledgeBase` test fails**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.OpenAI.Tests/Anela.Heblo.Adapters.OpenAI.Tests.csproj \
  --filter "FullyQualifiedName~OpenAiAdapterServiceCollectionExtensionsTests"
```

Expected: `AddOpenAiAdapter_NoEmbeddingKeys_UsesClassDefaults` PASSES, `AddOpenAiAdapter_OpenAiEmbeddingKeys_OverrideClassDefaults` FAILS (`Expected options.EmbeddingModel to be "text-embedding-3-small", but found "text-embedding-3-large".`), `AddOpenAiAdapter_KnowledgeBaseEmbeddingKeys_AreIgnored` FAILS (`Expected options.EmbeddingModel to be "text-embedding-3-large" … but found "text-embedding-3-small".`).

- [ ] **Step 4: Change the binding source**

In `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiAdapterServiceCollectionExtensions.cs`, replace lines 16-17:

```csharp
            opts.EmbeddingModel = configuration["KnowledgeBase:EmbeddingModel"] ?? opts.EmbeddingModel;
            opts.EmbeddingDimensions = configuration.GetValue("KnowledgeBase:EmbeddingDimensions", opts.EmbeddingDimensions);
```

with:

```csharp
            // Adapter-wide fallback only — every current call site passes its own per-feature
            // EmbeddingGenerationOptions (see RagFeatureOptions.ToEmbeddingOptions). Keys are
            // deliberately neutral (OpenAI:*, like OpenAI:ApiKey above) rather than named after
            // any one feature. Absent from appsettings*.json, so the class defaults apply.
            opts.EmbeddingModel = configuration["OpenAI:EmbeddingModel"] ?? opts.EmbeddingModel;
            opts.EmbeddingDimensions = configuration.GetValue("OpenAI:EmbeddingDimensions", opts.EmbeddingDimensions);
```

- [ ] **Step 5: Run the adapter test project to verify everything passes**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.OpenAI.Tests/Anela.Heblo.Adapters.OpenAI.Tests.csproj
```

Expected: PASS — 16 tests.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiAdapterServiceCollectionExtensions.cs \
        backend/test/Anela.Heblo.Adapters.OpenAI.Tests/Anela.Heblo.Adapters.OpenAI.Tests.csproj \
        backend/test/Anela.Heblo.Adapters.OpenAI.Tests/OpenAiAdapterServiceCollectionExtensionsTests.cs
git commit -m "refactor(openai): bind adapter embedding fallback from OpenAI:* instead of KnowledgeBase:*"
```

---

### task: verify-config-parity-and-run-full-validation

Runs NFR-3's required manual verification (the spec calls for it and no automated test covers it) plus the repo's completion gate from `CLAUDE.md`: `dotnet build`, `dotnet format`, full backend test run.

**Files:**
- Read-only: `backend/src/Anela.Heblo.API/appsettings.json`, `backend/src/Anela.Heblo.API/appsettings.Production.json`
- Possibly modified by `dotnet format`: any file touched by earlier tasks

- [ ] **Step 1: Confirm no `IEmbeddingGenerator` call site was missed**

```bash
grep -rn "GenerateAsync" --include=*.cs backend/src/ | grep -i "embedding"
```

Expected: exactly five Application-layer hits, each now passing an options argument, plus the adapter's own definition:

```
backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/SearchDocuments/SearchDocumentsHandler.cs:  var embeddings = await _embeddingGenerator.GenerateAsync(
backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ConversationIndexingStrategy.cs:  ... _embeddingGenerator.GenerateAsync(topics, _options.ToEmbeddingOptions(), ct)
backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategy.cs:  ... _embeddingGenerator.GenerateAsync(summaries, _options.ToEmbeddingOptions(), ct)
backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs:  ... _embeddings.GenerateAsync([queryToEmbed], embeddingOptions, ct)
backend/src/Anela.Heblo.Application/Features/Leaflet/Services/LeafletIndexingService.cs:  ... _embeddings.GenerateAsync(inputs, _options.ToEmbeddingOptions(), ct)
```

If any hit still uses `cancellationToken: ct` with no options argument, add the missing pass-through before continuing.

- [ ] **Step 2: Confirm the `KnowledgeBase:*` embedding binding is gone from the adapter**

```bash
grep -rn "KnowledgeBase:Embedding" --include=*.cs backend/src/
```

Expected: no output (FR-5 acceptance criterion 1).

- [ ] **Step 3: Run the NFR-3 config-parity check**

```bash
grep -n "EmbeddingModel\|EmbeddingDimensions" backend/src/Anela.Heblo.API/appsettings.json \
                                               backend/src/Anela.Heblo.API/appsettings.Production.json
grep -rn "\"OpenAI\"" backend/src/Anela.Heblo.API/appsettings.json \
                      backend/src/Anela.Heblo.API/appsettings.Production.json
```

Expected output and the reasoning it must confirm:

- `appsettings.json:212` → `Leaflet.EmbeddingModel = "text-embedding-3-large"`; no `Leaflet.EmbeddingDimensions` key, so `RagFeatureOptions.EmbeddingDimensions = 1536` applies.
- `appsettings.json:239-240` → `KnowledgeBase.EmbeddingModel = "text-embedding-3-large"`, `KnowledgeBase.EmbeddingDimensions = 1536`.
- `appsettings.Production.json:109` → `Leaflet.EmbeddingModel = "text-embedding-3-large"`; no `Leaflet.EmbeddingDimensions`, no `KnowledgeBase` embedding overrides, so both fall through to `appsettings.json`.
- No `OpenAI:EmbeddingModel` / `OpenAI:EmbeddingDimensions` keys exist, so `OpenAiEmbeddingOptions` keeps its class defaults `"text-embedding-3-large"` / `1536`.

Conclusion to confirm explicitly: every feature now resolves `text-embedding-3-large` / `1536` — byte-identical to what the adapter resolved before this change from `KnowledgeBase:*`. No re-embedding, no pgvector dimension migration, and `KnowledgeBaseChunks.Embedding` / `LeafletChunks.Embedding` stay `vector(1536)`. **If any of these values differs from the above, stop and report it — that would mean this change alters production embeddings, which is explicitly out of scope.**

- [ ] **Step 4: Build the solution**

```bash
dotnet build
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (or no new warnings relative to the pre-change baseline).

- [ ] **Step 5: Format**

```bash
dotnet format
dotnet format --verify-no-changes
```

Expected: the second command exits 0 with no output.

- [ ] **Step 6: Run the full backend test suite**

```bash
dotnet test
```

Expected: PASS — all test projects green, including `Anela.Heblo.Adapters.OpenAI.Tests` (16 tests) and `Anela.Heblo.Tests`.

- [ ] **Step 7: Commit any formatting changes**

Only if `dotnet format` modified files:

```bash
git add -A
git commit -m "style: apply dotnet format"
```

If `dotnet format` changed nothing, skip this step — there is nothing to commit.

---

## Self-Review

**1. Spec coverage**

| Spec item | Task |
|---|---|
| FR-1 (`options.ModelId` honored, per-model client cache, test seam preserved, 7 existing tests unmodified) | `add-per-model-embedding-client-cache` |
| FR-2 (`options.Dimensions` honored) | `honor-options-dimensions-in-embedding-generator` |
| FR-3 (Leaflet indexing + query call sites pass `LeafletOptions`) | `pass-embedding-options-from-leaflet-indexing-service`, `pass-embedding-options-from-generate-leaflet-handler` |
| FR-4 (KnowledgeBase call site passes `KnowledgeBaseOptions`) | `pass-embedding-options-from-knowledgebase-indexing-strategy` (+ `…-conversation-indexing-strategy`, `…-search-documents-handler` for the two call sites the spec missed) |
| FR-5 (`KnowledgeBase:*` → `OpenAI:*` fallback binding, all three acceptance criteria) | `rebind-adapter-embedding-defaults-to-openai-config-keys` |
| NFR-1 (at most one client construction per model, O(1) lookup) | `add-per-model-embedding-client-cache` steps 2 & 4 (`GenerateAsync_SameModelIdTwice_ConstructsClientOnce`, `Lazy<T>`-wrapped `GetOrAdd`) |
| NFR-2 (no public signature changes) | Only the `internal` constructor gains an optional parameter; `GenerateAsync`'s signature is untouched — see "Deviations" note 1 |
| NFR-3 (no config edits, no re-indexing) | `verify-config-parity-and-run-full-validation` step 3 |
| Arch review Amendment 1 (assert `model` from the request body) | `honor-options-dimensions-in-embedding-generator` step 2 extends `BuildEmbeddingResponse`; `add-per-model-embedding-client-cache` asserts on `capturedModels` |
| Arch review Amendment 2 (`ToEmbeddingOptions()` helper) | `add-ragfeatureoptions-toembeddingoptions-helper` |
| Arch review Amendment 3 (seed cache under `_options.EmbeddingModel`) | `add-per-model-embedding-client-cache` step 4 constructor body |
| Arch review risk "land FR-3/FR-4 with FR-5, no intermediate regression" | Task ordering — the config rename is the last production change |
| Out of scope: model/dimension value changes, backfilling, `vector(N)` guardrails, `AnthropicChatClient` changes, shared chat/embedding resolve helper | No task touches any of these |

Design doc coverage: component responsibilities, the `ConcurrentDictionary<string, Lazy<EmbeddingClient>>` shape, the deferred-construction rule, the test-seam seeding rule, and the unchanged persistence schema are all reflected in the tasks above. Open Questions: none in the spec.

**2. Placeholder scan** — every code step contains complete, compilable code copied against the actual current file contents; every test step shows the full test body; every run step gives an exact command and its expected pass/fail outcome. No "TBD", no "add error handling", no "similar to earlier task".

**3. Type consistency** — `RagFeatureOptions.ToEmbeddingOptions()` returns `Microsoft.Extensions.AI.EmbeddingGenerationOptions`, which is exactly the second parameter type of `IEmbeddingGenerator<string, Embedding<float>>.GenerateAsync`, and exactly the `MeaiOptions? options` parameter the adapter now reads. `OpenAiEmbeddingOptions.EmbeddingModel` (`string`) / `.EmbeddingDimensions` (`int`) match `RagFeatureOptions.EmbeddingModel` (`string`) / `.EmbeddingDimensions` (`int`), so `options?.ModelId ?? _options.EmbeddingModel` yields `string` and `options?.Dimensions ?? _options.EmbeddingDimensions` yields `int` — both non-nullable, as `global::OpenAI.Embeddings.EmbeddingGenerationOptions.Dimensions` (`int?`) accepts. The internal constructor's new parameter `Func<string, EmbeddingClient>? clientFactory = null` is satisfied by `RecordingClientFactory.Create(string model)` returning `EmbeddingClient`. `ConversationIndexingStrategy`'s new `IOptions<KnowledgeBaseOptions>` parameter matches what `KnowledgeBaseDocIndexingStrategy` already takes and what `KnowledgeBaseModule` already registers.
