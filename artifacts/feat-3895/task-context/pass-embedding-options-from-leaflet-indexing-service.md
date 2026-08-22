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
