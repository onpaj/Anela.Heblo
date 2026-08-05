# Design: OpenAiEmbeddingGenerator honours the batch contract (concrete, build-ready)

No UI — this is a backend adapter-internals fix. UX/UI section omitted.

This document turns plan-02.md's requirements into an exact file-by-file design: final
method body, exact constructor shape, exact test project contents, exact `.sln` edits, and
the exact fake-transport response format tests must produce. It does not restate the
plan's rationale (see plan-02.md / architecture-01.md) — only the concrete shape to build.

## 1. Component design

### 1.1 Boundary — unchanged

`OpenAiEmbeddingGenerator` (`backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiEmbeddingGenerator.cs`)
stays the sole `IEmbeddingGenerator<string, Embedding<float>>` implementation, registered by
`OpenAiAdapterServiceCollectionExtensions.AddOpenAiAdapter` exactly as today (line 20-24: a
factory lambda taking `IOptions<OpenAiEmbeddingOptions>` + `ILogger<OpenAiEmbeddingGenerator>`,
piped through `.UseLogging()`). No DI registration line changes. All three callers
(`LeafletIndexingService`, `KnowledgeBaseDocIndexingStrategy`, `ConversationIndexingStrategy`)
are untouched.

### 1.2 `OpenAiEmbeddingGenerator.cs` — exact shape

```csharp
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
    private readonly EmbeddingClient _client;

    public OpenAiEmbeddingGenerator(
        IOptions<OpenAiEmbeddingOptions> options,
        ILogger<OpenAiEmbeddingGenerator> logger)
        : this(options, logger, new EmbeddingClient(options.Value.EmbeddingModel, options.Value.ApiKey))
    { }

    internal OpenAiEmbeddingGenerator(
        IOptions<OpenAiEmbeddingOptions> options,
        ILogger<OpenAiEmbeddingGenerator> logger,
        EmbeddingClient client)
    {
        _options = options.Value;
        _logger = logger;
        _client = client;
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
                async token => await _client.GenerateEmbeddingsAsync(chunk, embeddingOptions, cancellationToken: token),
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

Notes tying this back to the FRs:
- **FR-1/FR-2**: `inputList.Chunk(MaxBatchSize)` — for `Count <= 2048` this is one chunk, one
  `GenerateEmbeddingsAsync` call; for larger inputs it's `ceil(N/2048)` sequential calls.
- **FR-3**: `result.Value.OrderBy(e => e.Index)` runs unconditionally, every chunk, no branch —
  matches architecture-01.md §3.1 ("always sort, not defensive").
- **FR-4**: `Pipeline.ExecuteAsync` wraps the same delegate shape as before; only the SDK call
  inside changed from `GenerateEmbeddingAsync(input, ...)` to `GenerateEmbeddingsAsync(chunk, ...)`.
- **FR-5**: empty-input short-circuit moved above client use (it already was), unchanged
  behavior; `ApiKey` guard stays the first line of `GenerateAsync`, not moved to a constructor,
  so a misconfigured app still fails at first *use* not at DI-resolution time.
- **FR-6**: `_client` is now a field, built once by the public constructor and passed through to
  the internal one — constructed exactly once per `OpenAiEmbeddingGenerator` instance regardless
  of how many times `GenerateAsync` is called.
- **FR-7**: the `internal` constructor overload takes a pre-built `EmbeddingClient` directly
  (simplest seam — no `OpenAIClientOptions` re-derivation needed in tests), mirroring
  `PlaudTokenRefresher`'s public-ctor-delegates-to-internal-ctor pattern
  (`backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenRefresher.cs:25-42`), not a new
  shape invented for this class.

`Microsoft.Extensions.AI` `Embedding<float>` and `EmbeddingGeneratedEmbeddings` types, `.ToFloats()`,
and the metadata/`GetService`/`Dispose` members are all unchanged from today.

### 1.3 `AssemblyInfo.cs` — new file

`backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Anela.Heblo.Adapters.OpenAI.Tests")]
```

Mirrors `Anela.Heblo.Adapters.Flexi/AssemblyInfo.cs` verbatim (single-target `InternalsVisibleTo`,
no extra comment needed — Plaud's version adds a comment because it grants visibility to two
assemblies; this one grants to one, so the plain Flexi form is the closer match).

## 2. Data / contracts

No schema changes — no DB, no HTTP request/response contracts consumed by other services, no
event payloads. Purely an internal refactor of one adapter method plus its constructor shape.

- **Input** (unchanged): `IEnumerable<string>`.
- **Output** (unchanged): `GeneratedEmbeddings<Embedding<float>>`, order-correlated to input.
- **Config** (unchanged): `OpenAiEmbeddingOptions { ApiKey, EmbeddingModel, EmbeddingDimensions }`.
- **New internal constant**: `MaxBatchSize = 2048`, private to `OpenAiEmbeddingGenerator`.
- **New constructor overload** (internal, test-only): `OpenAiEmbeddingGenerator(IOptions<OpenAiEmbeddingOptions>, ILogger<OpenAiEmbeddingGenerator>, EmbeddingClient)`.
- **OpenAI wire format actually produced by `EmbeddingClient.GenerateEmbeddingsAsync`** (needed
  by tests to shape fake HTTP responses — this is the standard, stable OpenAI embeddings REST
  response, unchanged by this fix, documented here so the test project doesn't have to
  rediscover it):

```json
{
  "object": "list",
  "data": [
    { "object": "embedding", "index": 0, "embedding": [0.001, 0.002, "... N floats"] },
    { "object": "embedding", "index": 1, "embedding": [0.003, 0.004, "... N floats"] }
  ],
  "model": "text-embedding-3-small",
  "usage": { "prompt_tokens": 8, "total_tokens": 8 }
}
```

  `OpenAIEmbeddingCollection.Model` binds to the `"model"` HTTP header/body pair (SDK detail,
  not consumed here); `OpenAIEmbedding.Index` binds to `"index"`; `OpenAIEmbedding.ToFloats()`
  binds to `"embedding"`. The shuffled-order test (FR-3) is a fake response whose `data` array
  lists items with `"index": 1` before `"index": 0` — the fake transport is free to emit `data`
  in any order since `OrderBy(e => e.Index)` is what makes the code correct regardless.

## 3. Test project — exact shape

### 3.1 `backend/test/Anela.Heblo.Adapters.OpenAI.Tests/Anela.Heblo.Adapters.OpenAI.Tests.csproj`

Direct copy of `Anela.Heblo.Adapters.OpenMeteo.Tests`'s csproj shape, project reference swapped:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="8.0.2" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="Moq" Version="4.20.70" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Adapters\Anela.Heblo.Adapters.OpenAI\Anela.Heblo.Adapters.OpenAI.csproj" />
  </ItemGroup>
</Project>
```

`OpenAI` and `System.ClientModel` types (`EmbeddingClient`, `OpenAIClientOptions`,
`HttpClientPipelineTransport`, `ApiKeyCredential`) come in transitively via the
`Anela.Heblo.Adapters.OpenAI` project reference — no direct `PackageReference` to `OpenAI` or
`System.ClientModel` is needed in the test csproj (same transitive-reference pattern the sibling
test projects use for their adapter's own SDK dependency).

### 3.2 Test fixture shape — `OpenAiEmbeddingGeneratorTests.cs`

One file, following `HomeAssistantRetryPipelineTests`'s `StatefulHandler`-over-`HttpMessageHandler`
pattern, one extra layer of SDK glue per architecture-01.md §3.2:

```csharp
using System.Net;
using System.ClientModel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Embeddings;

namespace Anela.Heblo.Adapters.OpenAI.Tests;

public class OpenAiEmbeddingGeneratorTests
{
    private static OpenAiEmbeddingGenerator BuildGenerator(HttpMessageHandler handler)
    {
        var options = Options.Create(new OpenAiEmbeddingOptions
        {
            ApiKey = "test-key",
            EmbeddingModel = "text-embedding-3-small",
            EmbeddingDimensions = 1536,
        });
        var client = new EmbeddingClient(
            options.Value.EmbeddingModel,
            new ApiKeyCredential(options.Value.ApiKey),
            new OpenAIClientOptions { Transport = new HttpClientPipelineTransport(new HttpClient(handler)) });

        return new OpenAiEmbeddingGenerator(options, NullLogger<OpenAiEmbeddingGenerator>.Instance, client);
    }

    // ... StatefulHandler copied from HomeAssistantRetryPipelineTests, building
    // OpenAI-shaped JSON bodies per §2's wire format ...
}
```

`OpenAiEmbeddingOptions` must be constructible with an object initializer from the test project —
confirm at implementation time it's a `public class` with settable properties (matches the DTO
convention: "DTOs are classes, never records" per `CLAUDE.md`; `OpenAiEmbeddingOptions` is a
config POCO, not a DTO, but the settable-properties shape needed for `Options.Create(new
OpenAiEmbeddingOptions { ... })` should already hold since `OpenAiAdapterServiceCollectionExtensions`
constructs it the same way via `services.Configure<OpenAiEmbeddingOptions>(opts => opts.ApiKey = ...)`).

### 3.3 Test cases (from plan-02.md FR-1 through FR-7, restated as concrete assertions)

| # | Scenario | Fake transport behavior | Assertion |
|---|---|---|---|
| 1 | Single batch, N=5 | 1 response, `data` has 5 items, index 0-4 | `handler.CallCount == 1`; result has 5 entries; result[i] maps to input[i] |
| 2 | Oversized batch, N=2500 | 2 responses (first request body has 2048 items, second has 452) | `handler.CallCount == 2`; result has 2500 entries in original order |
| 3 | Shuffled response order | `data` array emits index 1 before index 0 | result[0] corresponds to input[0]'s embedding value, not input[1]'s (proves `OrderBy` is applied, not response-array order) |
| 4 | Empty input | handler never invoked | `handler.CallCount == 0`; result has 0 entries |
| 5 | Transient failure then success | first attempt per chunk throws `HttpRequestException`, second succeeds | result correct; `handler.CallCount == 2` for that chunk (1 failure + 1 retry) |
| 6 | Retry exhaustion | every attempt throws `HttpRequestException` | `GenerateAsync` throws after `handler.CallCount == 4` (1 + 3 retries); no partial result returned |
| 7 | Client reuse | 2 sequential `GenerateAsync` calls on the same generator instance, single-item inputs each | both calls succeed using the one `EmbeddingClient`/`HttpMessageHandler` passed into the internal ctor — proven by construction (the test only ever builds one `EmbeddingClient`), not by a runtime counter |

Distinguishing embeddings per input in tests 1-3: give each fake embedding a distinct, recognizable
float vector (e.g., `[inputIndex, inputIndex, inputIndex]`) so assertions can tell "embedding for
input 2" apart from "embedding for input 0" without relying on array position alone.

## 4. `Anela.Heblo.sln` registration

Two GUIDs needed — the project itself and no additional solution folder (it nests directly under
the existing `test` folder, GUID `{23FE24B3-CD9D-4576-A7C8-85D5B012F43D}`, same parent as
`Anela.Heblo.Adapters.OpenMeteo.Tests`). Generate a fresh GUID at implementation time (any v4 GUID
not already present in the `.sln`); shown here as `{NEWGUID-OPENAI-TESTS-0000-000000000000}`
placeholder — replace with an actual generated GUID when editing the file.

**1. `Project(...)` line** — add near the existing `Anela.Heblo.Adapters.OpenAI` and
`Anela.Heblo.Adapters.OpenMeteo.Tests` entries (exact insertion point doesn't matter, `.sln`
project order is not semantically significant):

```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Anela.Heblo.Adapters.OpenAI.Tests", "backend\test\Anela.Heblo.Adapters.OpenAI.Tests\Anela.Heblo.Adapters.OpenAI.Tests.csproj", "{NEWGUID-OPENAI-TESTS-0000-000000000000}"
EndProject
```

**2. `GlobalSection(ProjectConfigurationPlatforms)` block** — add, mirroring the
`Anela.Heblo.Adapters.OpenMeteo.Tests` block (`.sln:422-433`) with the new GUID substituted:

```
		{NEWGUID-OPENAI-TESTS-0000-000000000000}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{NEWGUID-OPENAI-TESTS-0000-000000000000}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{NEWGUID-OPENAI-TESTS-0000-000000000000}.Debug|x64.ActiveCfg = Debug|Any CPU
		{NEWGUID-OPENAI-TESTS-0000-000000000000}.Debug|x64.Build.0 = Debug|Any CPU
		{NEWGUID-OPENAI-TESTS-0000-000000000000}.Debug|x86.ActiveCfg = Debug|Any CPU
		{NEWGUID-OPENAI-TESTS-0000-000000000000}.Debug|x86.Build.0 = Debug|Any CPU
		{NEWGUID-OPENAI-TESTS-0000-000000000000}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{NEWGUID-OPENAI-TESTS-0000-000000000000}.Release|Any CPU.Build.0 = Release|Any CPU
		{NEWGUID-OPENAI-TESTS-0000-000000000000}.Release|x64.ActiveCfg = Release|Any CPU
		{NEWGUID-OPENAI-TESTS-0000-000000000000}.Release|x64.Build.0 = Release|Any CPU
		{NEWGUID-OPENAI-TESTS-0000-000000000000}.Release|x86.ActiveCfg = Release|Any CPU
		{NEWGUID-OPENAI-TESTS-0000-000000000000}.Release|x86.Build.0 = Release|Any CPU
```

**3. `GlobalSection(NestedProjects)` entry** — add one line placing the new project under the
`test` solution folder, mirroring `.sln:529` (`{399B6C8C-...} = {23FE24B3-...}`):

```
		{NEWGUID-OPENAI-TESTS-0000-000000000000} = {23FE24B3-CD9D-4576-A7C8-85D5B012F43D}
```

All three edits are required — `.sln` files need the project declaration, the per-configuration
build matrix, and the solution-folder nesting entry for `dotnet build`/`dotnet test` at the
solution root to pick the project up and IDEs to show it in the right folder.

## 5. Files touched (summary)

| File | Change |
|---|---|
| `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiEmbeddingGenerator.cs` | Rewrite `GenerateAsync` body (chunk + batch call + Index-sort); add `EmbeddingClient` field + internal ctor overload; add `MaxBatchSize` constant |
| `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/AssemblyInfo.cs` | New file — `InternalsVisibleTo("Anela.Heblo.Adapters.OpenAI.Tests")` |
| `backend/test/Anela.Heblo.Adapters.OpenAI.Tests/Anela.Heblo.Adapters.OpenAI.Tests.csproj` | New test project |
| `backend/test/Anela.Heblo.Adapters.OpenAI.Tests/OpenAiEmbeddingGeneratorTests.cs` | New test file — 7 cases per §3.3 |
| `Anela.Heblo.sln` | 3 edits: `Project(...)` block, `ProjectConfigurationPlatforms` block, `NestedProjects` line |

No other files change. `LeafletIndexingService.cs`, `KnowledgeBaseDocIndexingStrategy.cs`,
`ConversationIndexingStrategy.cs`, and `OpenAiAdapterServiceCollectionExtensions.cs` are
untouched — confirmed by design: the public constructor signature `OpenAiEmbeddingGenerator(IOptions<OpenAiEmbeddingOptions>, ILogger<OpenAiEmbeddingGenerator>)` used by the DI factory lambda is preserved unchanged; only a new internal overload is added alongside it.
