# Architecture review: OpenAiEmbeddingGenerator batching fix

Verdict: **design-01.md is architecturally sound and may proceed to implementation**, with one
gap closed below (the testability seam was underspecified and is now a concrete, precedent-backed
decision) and one design detail upgraded from "defensive/optional" to "required" (Index-based
ordering). Everything else in design-01.md checked out against the actual SDK and the codebase's
conventions.

## 1. Alignment with existing patterns and integration points

Verified directly against the installed `OpenAI` 2.8.0 assembly via reflection (not just
decompiled strings) — the design's SDK claims are **all correct**:

- `EmbeddingClient.GenerateEmbeddingsAsync(IEnumerable<string>, EmbeddingGenerationOptions, CancellationToken)` exists exactly as described, returns `Task<ClientResult<OpenAIEmbeddingCollection>>`.
- `OpenAIEmbedding` exposes `Index` (int) — confirmed by reflection, not assumed.
- `OpenAIEmbeddingCollection` implements `IReadOnlyList<OpenAIEmbedding>` — an ordinary ordered
  collection, no surprises.
- `EmbeddingClient` constructors: `(string, string)` [current usage], `(string, ApiKeyCredential)`,
  and — critically — `(string, ApiKeyCredential, OpenAIClientOptions)`. There is **no**
  `(string, string, OpenAIClientOptions)` overload; a caller that wants to pass
  `OpenAIClientOptions` must use the `ApiKeyCredential` overload.
- `OpenAIClientOptions.Transport` is typed `System.ClientModel.Primitives.PipelineTransport`
  (abstract). The concrete `HttpClientPipelineTransport` (public, non-abstract, in the
  `System.ClientModel` package) has a public `HttpClientPipelineTransport(HttpClient)`
  constructor — confirmed by reflection.

That last point matters: it means the "fake transport" test seam the design proposes isn't a novel
technique for this SDK — it reduces to the **exact same fake-`HttpMessageHandler` pattern already
used in this repo** (`ShoptetPriceClientHttp500Tests`, `HomeAssistantRetryPipelineTests`: a
`Mock<HttpMessageHandler>`/custom `HttpMessageHandler` subclass wrapped in an `HttpClient`), just
with one extra layer of SDK glue (`HttpClientPipelineTransport` wraps the fake `HttpClient`,
`OpenAIClientOptions.Transport` carries it in). No new testing technique enters the codebase.

Other alignment checks:

- **Resilience**: the existing static `Pipeline` (`ResiliencePipelineBuilder` + `AddRetry`, 3
  attempts, exponential backoff from 2s, on `HttpRequestException`) is untouched in shape — only
  the delegate body changes from a single-item call to a batch call. Consistent with how
  `HomeAssistantRetryPipelineTests` and Shoptet's clients wrap Polly around HTTP calls.
- **Test project shape**: `backend/test/Anela.Heblo.Adapters.OpenMeteo.Tests` is the right sibling
  template (net8.0, `IsTestProject`, xunit + FluentAssertions + Moq + `Microsoft.NET.Test.Sdk`,
  single `ProjectReference` to the adapter project). `docs/architecture/development_guidelines.md`
  states "Each module has its own test project" — a new `Anela.Heblo.Adapters.OpenAI.Tests`
  project is the correct move, not an exception.
- **`Enumerable.Chunk`**: confirmed available in .NET 8 BCL; no hand-rolled chunker needed, per
  design.
- **Callers are index-correlated**: checked `KnowledgeBaseDocIndexingStrategy.CreateChunksAsync`
  directly — it does `embeddings[i]` against `chunkTexts[i]`/`summaries[i]` by raw positional
  index, no ID/key matching. `LeafletIndexingService` and `ConversationIndexingStrategy` follow the
  same shape (per plan-01.md). This raises the stakes on order preservation — see §3.

No boundary, DI shape, or contract changes are introduced anywhere in the design — confirmed
against `OpenAiAdapterServiceCollectionExtensions.AddOpenAiAdapter`, which registers via a factory
lambda taking only `IOptions<OpenAiEmbeddingOptions>` and `ILogger`. That stays unchanged.

## 2. Proposed architecture — decisions and rationale

No structural changes: one class (`OpenAiEmbeddingGenerator`), same public contract, same DI
registration. The internal restructure is:

1. Guard clauses unchanged (empty-`ApiKey` throw, empty-input short-circuit).
2. `EmbeddingClient` constructed once (constructor field) instead of per-call — FR-5, secondary.
3. `inputList.Chunk(MaxBatchSize)` (`MaxBatchSize = 2048`, OpenAI's documented per-request cap —
   confirm against current docs at implementation time, it's a hardcoded provider limit, not
   config, correctly per design §2).
4. Each chunk goes through the *same* `Pipeline.ExecuteAsync`, now wrapping
   `client.GenerateEmbeddingsAsync(chunk, ...)` instead of the single-item call.
5. Results are assembled by concatenating chunks in chunk order, with per-chunk elements ordered
   — see §3 for the one change I'm making here.

This is the right shape. No alternative was seriously in play (looping was the bug, not a
legitimate alternative), and no new abstraction is warranted — there is exactly one call site of
the SDK client, so a wrapper interface would be premature abstraction for a single consumer, which
the project's own guidance forbids. Agreed with design-01.md's decision **not** to introduce
`IEmbeddingClientWrapper`.

## 3. Implementation guidance

### 3.1 Ordering: upgrade from "defensive, if discrepancy observed" to "always sort by Index"

Design §1.2 step 5 treats sorting by `OpenAIEmbedding.Index` as optional/defensive ("no
re-sorting needed unless a discrepancy is observed... in which case sort by Index as a defensive
measure"). Given the caller audit in §1 — `KnowledgeBaseDocIndexingStrategy` and its siblings
correlate embeddings to source text by **raw positional index with no other check** — an
out-of-order response would silently pair the wrong embedding with the wrong chunk. That's not a
crash, not a retry-visible failure, not a logged warning — it's a **silent semantic corruption**
of the knowledge base / leaflet search index that would only surface as "search returns weird
results" days later, with no stack trace pointing back here.

Sorting by `Index` before mapping costs one `OrderBy` per chunk (chunk size ≤ 2048, negligible)
and removes this failure mode entirely regardless of whether OpenAI's API ever actually
reorders results. **Make it unconditional, not defensive**: within each chunk's
`OpenAIEmbeddingCollection`, sort by `.Index` before calling `.ToFloats()` and appending. Do not
gate this behind "if a discrepancy is observed" — there is no way to observe the discrepancy
except by already having produced a corrupted index.

### 3.2 Testability seam: concrete resolution, following existing precedent

Design §1.3 correctly rules out a new `IEmbeddingClientWrapper` interface but leaves *how* a test
actually reaches into `OpenAiEmbeddingGenerator.GenerateAsync` unresolved ("investigate at
implementation time"). I resolved this by reflection (§1) and by finding the pattern already used
for the same problem elsewhere in this codebase:

- **`Anela.Heblo.Adapters.Flexi`** and **`Anela.Heblo.Adapters.Plaud`** both expose an `internal`
  constructor overload for exactly this reason — Plaud's `AssemblyAttributes.cs` says verbatim:
  *"Expose internal test seams (e.g. `PlaudCliClient`/`PlaudTokenRefresher` constructors that
  accept an overridable... path) to the adapter test projects"* — and both wire it up with
  `[assembly: InternalsVisibleTo("Anela.Heblo.Adapters.<X>.Tests")]`.

Apply the same pattern here, not a new one:

- Add an `internal` constructor overload to `OpenAiEmbeddingGenerator` that accepts a pre-built
  `EmbeddingClient` (or, more minimally, an `OpenAIClientOptions`) directly, bypassing the
  production `new EmbeddingClient(_options.EmbeddingModel, _options.ApiKey)` path.
- Add `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/AssemblyInfo.cs` with
  `[assembly: InternalsVisibleTo("Anela.Heblo.Adapters.OpenAI.Tests")]` (mirrors Flexi's file
  exactly).
- In the new test project, build the fake client as:
  `new EmbeddingClient(model, new ApiKeyCredential("test"), new OpenAIClientOptions { Transport = new HttpClientPipelineTransport(fakeHttpClient) })`
  where `fakeHttpClient` wraps a `Mock<HttpMessageHandler>` / custom `HttpMessageHandler` subclass
  — the same object shape as `ShoptetPriceClientHttp500Tests`, just one layer deeper.
- Assert call count via the fake handler's own counter (as `HomeAssistantRetryPipelineTests` does
  with its `StatefulHandler.CallCount`), not via mocking `EmbeddingClient` itself (it's a
  concrete SDK type — don't try to `Mock<EmbeddingClient>`).

This closes the plan's open question ("is `EmbeddingClient` mockable enough...") with a verified
yes, via a pattern this codebase already has precedent for — no new testing technique, no new
production abstraction beyond one `internal` constructor overload, which is the minimum seam
possible.

### 3.3 New test project registration

`Anela.Heblo.Adapters.OpenAI.Tests` needs a project GUID entry and build-configuration section in
`Anela.Heblo.sln` (confirmed by inspecting the sln directly — `Anela.Heblo.Adapters.OpenMeteo.Tests`
has both a `Project(...)` line and a `GlobalSection(ProjectConfigurationPlatforms)` block). Adding
only the `.csproj` file without registering it in the `.sln` will make it invisible to
`dotnet build`/`dotnet test` run at the solution level — a common miss worth calling out explicitly
since the validation step (`dotnet build`, full test suite) runs at the solution/directory level
per `CLAUDE.md`.

### 3.4 FR-5 (client reuse) — no blocking concern, one unverified but harmless assumption

The design assumes `AddEmbeddingGenerator` registers the generator as a singleton (justifying
"construct `EmbeddingClient` once, in the constructor"). I did not verify the DI lifetime in
`Microsoft.Extensions.AI` 9.5.0's `AddEmbeddingGenerator` extension. It doesn't matter either way:
even if the registration were scoped/transient, constructing the client in the constructor is
never worse than constructing it per-`GenerateAsync`-call (which is what happens today) — it's a
strict improvement or a no-op, not a risk. No action needed beyond what design-01.md already
specifies.

## 4. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Silent embedding/text misalignment if SDK response order isn't request order | §3.1 — sort by `Index` unconditionally, not defensively |
| Test seam ends up more invasive than a wrapper interface would have been | §3.2 — verified the minimal seam (one internal ctor + `InternalsVisibleTo`) is sufficient; SDK constructors and `HttpClientPipelineTransport` confirmed by reflection, not guessed |
| New test project silently excluded from `dotnet build`/CI | §3.3 — register in `Anela.Heblo.sln`, not just the filesystem |
| `MaxBatchSize = 2048` becomes stale if OpenAI changes the limit | Already flagged in plan/design as "confirm against current docs at implementation time" — no architectural fix needed, just a dev-time doc check before hardcoding |
| Chunking changes behavior for the (currently nonexistent) >2048-item caller | No current caller approaches this size (plan-01.md confirmed); sequential chunking is the correct default, no concurrency needed now |

## Prerequisites before implementation begins

None blocking. All SDK surface referenced by the design has now been verified to exist with the
exact signatures assumed (constructors, `GenerateEmbeddingsAsync` overload, `Index` property,
`HttpClientPipelineTransport`). The only design-level change required going into implementation is
§3.1 (unconditional Index sort) and §3.2 (concrete internal-ctor + `InternalsVisibleTo` seam,
mirroring Flexi/Plaud) — both are small, precedent-backed, and don't change the plan's scope or
FR/AC list.
