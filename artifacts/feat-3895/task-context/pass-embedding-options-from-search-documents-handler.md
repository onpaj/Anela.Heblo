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
