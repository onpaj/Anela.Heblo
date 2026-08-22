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
