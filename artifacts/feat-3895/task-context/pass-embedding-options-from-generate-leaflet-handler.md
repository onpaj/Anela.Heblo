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
