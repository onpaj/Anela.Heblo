# Development: OpenAiEmbeddingGenerator honours the batch contract

Implemented exactly per design-02.md / architecture-02.md, with one necessary deviation
(package version bump) discovered during build verification.

## Files changed

1. **`backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiEmbeddingGenerator.cs`** (rewritten)
   - `GenerateAsync` now chunks the input list (`inputList.Chunk(MaxBatchSize)`, `MaxBatchSize = 2048`)
     and issues **one** `EmbeddingClient.GenerateEmbeddingsAsync` call per chunk instead of one
     `GenerateEmbeddingAsync` call per input item — the fix for the finding (batched callers no
     longer get fanned out into N sequential round trips).
   - Added an empty-input short-circuit (`inputList.Count == 0` → return immediately, no API call).
   - Results are reordered unconditionally via `.OrderBy(e => e.Index)` per chunk before being
     appended, so output stays positionally correlated to input even if the API ever returns items
     out of order — all three callers (`LeafletIndexingService`, `KnowledgeBaseDocIndexingStrategy`,
     `ConversationIndexingStrategy`) rely on strict positional correlation.
   - `EmbeddingClient` is now a field built once (in the public constructor) and reused across all
     `GenerateAsync` calls on the same generator instance, instead of being allocated fresh on every
     call.
   - Added an `internal` constructor overload `(IOptions<OpenAiEmbeddingOptions>, ILogger<...>, EmbeddingClient)`
     so tests can inject a client wired to a fake `HttpMessageHandler`. The public 2-arg constructor
     is preserved unchanged and still delegates to it — the DI registration in
     `OpenAiAdapterServiceCollectionExtensions` required no changes.
   - The Polly `Pipeline` (retry policy) wraps the same delegate shape as before; only the SDK call
     inside changed.

2. **`backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/AssemblyInfo.cs`** (new)
   - `[assembly: InternalsVisibleTo("Anela.Heblo.Adapters.OpenAI.Tests")]`, copied verbatim from the
     `Anela.Heblo.Adapters.Flexi` pattern, to expose the new internal constructor to the test project.

3. **`backend/test/Anela.Heblo.Adapters.OpenAI.Tests/Anela.Heblo.Adapters.OpenAI.Tests.csproj`** (new)
   - New xunit test project, project-referencing the adapter project (no direct `OpenAI`/
     `System.ClientModel` package references needed — they flow in transitively).
   - One deviation from design-02 §3.1: `Microsoft.Extensions.Logging` is pinned to **8.0.1**, not
     8.0.0. The adapter project transitively depends on `Anela.Heblo.Application` →
     `Microsoft.FeatureManagement 4.5.0`, which requires `Microsoft.Extensions.Logging >= 8.0.1`;
     8.0.0 caused a `NU1605` package-downgrade restore error (treated as an error in this repo).
     `Anela.Heblo.Adapters.HomeAssistant.Tests` — the other test project that also references
     `Anela.Heblo.Application` transitively — already uses 8.0.1 for the same reason, so this matches
     established repo convention rather than diverging from it.

4. **`backend/test/Anela.Heblo.Adapters.OpenAI.Tests/OpenAiEmbeddingGeneratorTests.cs`** (new)
   - 7 test cases, one `StatefulHandler` fake `HttpMessageHandler` (same shape as
     `HomeAssistantRetryPipelineTests`'s), and a `BuildEmbeddingResponse` helper that parses the
     outgoing request's `"input"` array and echoes back OpenAI-shaped JSON (`{"object":"list",
     "data":[{"object":"embedding","index":N,"embedding":[...]}], "model":"...", "usage":{...}}`)
     whose embedding vector encodes each input's numeric suffix, so assertions can tell "the
     embedding for input N" apart from array/response position:
     - `GenerateAsync_SingleBatch_IssuesOneCallAndReturnsResultsInInputOrder` — N=5, 1 HTTP call, order preserved.
     - `GenerateAsync_OversizedBatch_ChunksAndPreservesOrder` — N=2500, exactly 2 HTTP calls (2048 + 452), order preserved across the chunk boundary.
     - `GenerateAsync_ResponseItemsOutOfOrder_AreReorderedByIndex` — fake response emits `index:1` before `index:0`; result is corrected by the mandatory `OrderBy`.
     - `GenerateAsync_EmptyInput_ReturnsEmptyWithoutCallingApi` — 0 HTTP calls for an empty input list.
     - `GenerateAsync_TransientFailureThenSuccess_RecoversAndReturnsCorrectResult` — first attempt throws `HttpRequestException`, second succeeds; result is correct.
     - `GenerateAsync_RetriesExhausted_ThrowsWithoutPartialResult` — every attempt throws; `GenerateAsync` throws and no partial result is returned; asserts exactly 4 HTTP calls.
     - `GenerateAsync_CalledTwice_ReusesSameClient` — two sequential `GenerateAsync` calls on one generator instance both succeed via the single injected `EmbeddingClient`/handler.

5. **`Anela.Heblo.sln`** — 3 edits registering the new test project under the existing `test` solution
   folder (GUID `{23FE24B3-CD9D-4576-A7C8-85D5B012F43D}`), using a freshly generated project GUID
   `{8379D1C5-B2F5-40D9-A2A1-47E073B53E7A}` (verified not already present in the file):
   - `Project(...)`/`EndProject` declaration, inserted after the `Anela.Heblo.Adapters.OpenMeteo.Tests` entry.
   - `ProjectConfigurationPlatforms` block (Debug/Release × Any CPU/x64/x86, ActiveCfg + Build.0 = 12 lines).
   - `NestedProjects` entry mapping the new project GUID to the `test` folder GUID.

## One implementation-time finding not surfaced in architecture-02.md

Verified via a scratch console app against the real `OpenAI` 2.8.0 package: the SDK's own
`ClientPipeline` already retries transport-level exceptions like `HttpRequestException`
internally (up to 3 retries / 4 total attempts) *before* the failure can reach our outer Polly
`Pipeline`. When retries are exhausted, the SDK throws `AggregateException`, not
`HttpRequestException` — so Polly's `ShouldHandle<HttpRequestException>()` never actually
intercepts it (dead code path, consistent with the pre-existing `ClientResultException` gap
architecture-02.md §4 already flagged as out-of-scope). This doesn't change any FR or the
production code — the observable behavior (transient failures retried, permanent failures
surface as a thrown exception with no partial results, in exactly 4 HTTP attempts) is identical
to what the design specified — but it did change how the two resilience tests had to be written:
they assert on the actual call count (4) and that *some* exception propagates, rather than
asserting the exception is specifically `HttpRequestException` coming from Polly's own retry
loop. No production code change is needed or was made for this; it's the same latent gap noted
in architecture-02.md, just one layer more precisely characterized.

## Verification performed

- `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/Anela.Heblo.Adapters.OpenAI.csproj` — 0 errors.
- `dotnet restore Anela.Heblo.sln` — succeeds, confirms the new test project's dependency graph resolves cleanly alongside the rest of the solution (this is what surfaced the `Microsoft.Extensions.Logging` version conflict, fixed as noted above).
- `dotnet build Anela.Heblo.sln --no-restore` — 0 errors, 116 pre-existing warnings (none in touched files).
- `dotnet test backend/test/Anela.Heblo.Adapters.OpenAI.Tests/Anela.Heblo.Adapters.OpenAI.Tests.csproj` — **7/7 passed**.
- `dotnet format Anela.Heblo.sln --include <the 3 changed/added .cs files> --verify-no-changes` — clean, no formatting diffs.
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~KnowledgeBase|FullyQualifiedName~Leaflet"` — 377 passed, 3 skipped, 30 failed. All 30 failures are pre-existing EF Core `ApplicationDbContext`/`ManyServiceProvidersCreatedWarning` integration-test infrastructure failures in `*RepositoryIntegrationTests`/`*PagedTests` (unrelated to embeddings, untouched by this change, confirmed via `git log` that those test files weren't modified in this task's history). All the actual embedding-consumer tests relevant to this change — `KnowledgeBaseDocIndexingStrategyTests`, `ConversationIndexingStrategyTests`, `LeafletIndexingServiceTests`, `SearchDocumentsHandlerTests`, `GenerateLeafletHandlerTests` — passed; these all mock `IEmbeddingGenerator<string, Embedding<float>>` directly (confirmed via grep) and are structurally unaffected by internal changes to the concrete `OpenAiEmbeddingGenerator` class.

## How to verify

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Adapters.OpenAI.Tests/Anela.Heblo.Adapters.OpenAI.Tests.csproj
dotnet format Anela.Heblo.sln --verify-no-changes
```

No other files needed to change — `LeafletIndexingService.cs`, `KnowledgeBaseDocIndexingStrategy.cs`,
`ConversationIndexingStrategy.cs`, and `OpenAiAdapterServiceCollectionExtensions.cs` are untouched,
as designed.
