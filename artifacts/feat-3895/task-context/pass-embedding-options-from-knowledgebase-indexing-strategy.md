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
