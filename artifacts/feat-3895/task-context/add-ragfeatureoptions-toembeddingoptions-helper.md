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
