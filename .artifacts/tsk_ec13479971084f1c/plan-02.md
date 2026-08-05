# Plan: OpenAiEmbeddingGenerator honours the batch contract (final, post-architecture-review)

## Summary
`OpenAiEmbeddingGenerator.GenerateAsync` (`backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiEmbeddingGenerator.cs:36-64`) implements the batch `IEmbeddingGenerator<string, Embedding<float>>` contract by looping over inputs and calling the OpenAI SDK's single-item `EmbeddingClient.GenerateEmbeddingAsync` once per element. This silently defeats the batching fix already merged for issue #3590. The fix: call `EmbeddingClient.GenerateEmbeddingsAsync(IEnumerable<string>, ...)` once per chunk (chunked at 2048 items), reuse a single `EmbeddingClient`, and sort each chunk's results by `Index` before assembling output — the last point upgraded from "defensive" to **mandatory** during architecture review, since downstream callers correlate embeddings to source text by raw positional index with no other check.

This plan supersedes plan-01.md; the requirements below fold in the two architecture-review resolutions (§3.1 ordering, §3.2 testability seam) as settled decisions, not open questions.

## Context
Issue #3590 fixed `KnowledgeBaseDocIndexingStrategy` to collect all summaries and call `GenerateAsync(summaries)` once, following the pattern already used in `ConversationIndexingStrategy`. That fix assumed the adapter honors the batch contract — it doesn't. The adapter still fans every batch out into individual OpenAI API calls, so #3590 (and `LeafletIndexingService`, which already batches all document chunks) get zero benefit: latency and cost still scale linearly with corpus size on every OneDrive ingestion, manual upload, and conversation re-index.

Three prior steps (plan-01, design-01, architecture-01) already:
- Confirmed via reflection against the installed `OpenAI` 2.8.0 assembly that `EmbeddingClient.GenerateEmbeddingsAsync(IEnumerable<string>, EmbeddingGenerationOptions, CancellationToken)` exists, returns `Task<ClientResult<OpeneAIEmbeddingCollection>>` (see note below on exact type name), and that `OpenAIEmbedding` exposes an `Index` property.
- Confirmed `EmbeddingClient` has no `(string, string, OpenAIClientOptions)` constructor — a fake-transport test must go through `(string, ApiKeyCredential, OpenAIClientOptions)`, with `OpenAIClientOptions.Transport` accepting a `HttpClientPipelineTransport(HttpClient)` wrapping a fake `HttpMessageHandler`.
- Confirmed the test seam should mirror the existing `Anela.Heblo.Adapters.Flexi`/`Anela.Heblo.Adapters.Plaud` pattern: an `internal` constructor overload + `[assembly: InternalsVisibleTo("Anela.Heblo.Adapters.OpenAI.Tests")]`, not a new `IEmbeddingClientWrapper` abstraction (rejected as premature — only one SDK call site exists).
- Confirmed `KnowledgeBaseDocIndexingStrategy.CreateChunksAsync` and its siblings correlate `embeddings[i]` to `chunkTexts[i]`/`summaries[i]` by raw positional index — no ID/key matching — which is why response ordering must be guaranteed, not assumed.

Note: `OpenAIEmbeddingCollection` (not "OpeneAI...") is the correct SDK type name per architecture-01.md §1; confirm exact casing/namespace again at implementation time via IDE autocomplete, since this is the one place the three upstream docs could have a typo carried forward.

No unit tests currently exist for `OpenAiEmbeddingGenerator`.

## Functional requirements

**FR-1: Single batched API call for a batch that fits in one request**
`GenerateAsync` must issue exactly one call to `EmbeddingClient.GenerateEmbeddingsAsync(IEnumerable<string>, EmbeddingGenerationOptions, CancellationToken)` for an input list within the provider's per-request limits, instead of one `GenerateEmbeddingAsync` call per item.
- Acceptance: given a list of N strings (N ≤ 2048), the fake-transport test observes exactly 1 outbound HTTP request; the returned `GeneratedEmbeddings<Embedding<float>>` has exactly N entries in input order.

**FR-2: Chunking for oversized batches**
If `inputList.Count` exceeds `MaxBatchSize` (2048 — OpenAI's documented per-request item cap for embeddings; re-confirm against current OpenAI docs before hardcoding), split into multiple `GenerateEmbeddingsAsync` calls via `inputList.Chunk(MaxBatchSize)` (built-in .NET 8 `Enumerable.Chunk`, no hand-rolled chunker), issued **sequentially**, and concatenate results in original order.
- Acceptance: a list of 2500 strings results in exactly 2 batch calls (2048 + 452), not 2500 single-item calls; output order matches input order across the chunk boundary.
- Token-budget-based chunking (as opposed to item-count) is out of scope — no current caller is known to approach it.

**FR-3: Deterministic output ordering (mandatory, not defensive)**
Within each chunk's `OpenAIEmbeddingCollection`, sort by `.Index` before mapping to `Embedding<float>` and appending to the result — unconditionally, regardless of whether the SDK is ever observed to reorder results.
- Acceptance: a test that returns a deliberately shuffled response ordering from the fake transport still yields a `GeneratedEmbeddings<Embedding<float>>` whose Nth entry corresponds to the Nth input string.
- Rationale (from architecture-01.md §3.1): callers index-correlate embeddings to source text with no other check; an unsorted response would silently corrupt the knowledge base / leaflet search index with no crash, no log, and no stack trace pointing back here. This is upgraded from design-01's "defensive, if discrepancy observed" to mandatory and unconditional.

**FR-4: Preserve existing resilience behavior**
Each chunk call must still go through the existing Polly `Pipeline` (3 retries, exponential backoff from 2s, on `HttpRequestException`) exactly as before — only the delegate body changes from a single-item call to a batch call.
- Acceptance: a transient `HttpRequestException` on a chunk call is retried up to 3 times with the same backoff; a chunk call that exhausts retries still throws (no swallowed failures, no silent partial results).

**FR-5: Preserve output contract and callers**
`Embedding<float>.ToFloats()` mapping, empty-input handling (empty list → empty `GeneratedEmbeddings`, no API call), the `Dimensions` option, and the `ApiKey` empty-check must behave identically to today. None of the 3 callers need any change.
- Acceptance: `LeafletIndexingService`, `KnowledgeBaseDocIndexingStrategy`, `ConversationIndexingStrategy` require zero code changes; an empty `values` enumerable returns an empty result set without hitting the network; a missing `ApiKey` still throws `InvalidOperationException` at first use of `GenerateAsync` (guard stays per-call, not moved to the constructor, so DI-resolution order doesn't matter).

**FR-6 (secondary): Reuse the `EmbeddingClient` instance**
Construct `EmbeddingClient` once (constructor field) instead of allocating a new one on every `GenerateAsync` call.
- Acceptance: across multiple `GenerateAsync` calls on the same `OpenAiEmbeddingGenerator` instance, only one `EmbeddingClient` is constructed (verifiable via the internal-ctor test seam, e.g. asserting the injected fake client/transport is the one actually used on every call).
- Treat as secondary to FR-1–FR-5; safe regardless of the generator's DI lifetime (singleton or not) since constructing once is never worse than constructing per-call.

**FR-7: Testability seam**
Add an `internal` constructor overload to `OpenAiEmbeddingGenerator` accepting a pre-built `EmbeddingClient`, bypassing the production `new EmbeddingClient(_options.EmbeddingModel, _options.ApiKey)` path, plus `[assembly: InternalsVisibleTo("Anela.Heblo.Adapters.OpenAI.Tests")]` in a new `AssemblyInfo.cs` (mirrors `Anela.Heblo.Adapters.Flexi`/`Anela.Heblo.Adapters.Plaud`).
- Acceptance: the new test project constructs `OpenAiEmbeddingGenerator` via the internal constructor, injecting an `EmbeddingClient` built with `OpenAIClientOptions { Transport = new HttpClientPipelineTransport(fakeHttpClient) }`, and asserts call counts via the fake handler — without mocking `EmbeddingClient` itself (a concrete SDK type) and without introducing an `IEmbeddingClientWrapper` abstraction.

## Non-functional requirements
- **Performance**: batched indexing of a 20-chunk document must issue 1 OpenAI round trip (or `ceil(N/2048)` for larger batches), not N. This is the entire point of the fix and must be verifiable via a test asserting call count on the fake transport.
- **No behavior change for callers**: none of the 3 call sites require modification; this is purely an adapter-internal fix.
- **Backward-compatible error semantics**: callers currently only handle `GenerateAsync` throwing after retry exhaustion; no new exception types leak through.
- **Correctness over defensiveness**: the Index-sort (FR-3) must be unconditional code, not a conditional/defensive branch — there is no runtime signal that could detect misordering after the fact, so "only sort if a discrepancy is observed" is not achievable and must not be implemented that way.

## Data model
No new entities. Existing types involved, unchanged shape:
- Input: `IEnumerable<string>` (raw text chunks/summaries/topics from callers).
- Output: `GeneratedEmbeddings<Embedding<float>>` (MEAI contract type, order-correlated to input).
- Config: `OpenAiEmbeddingOptions` (`ApiKey`, `EmbeddingModel`, `EmbeddingDimensions`) — unchanged.
- New internal constant: `MaxBatchSize = 2048`, private to `OpenAiEmbeddingGenerator` — a hardcoded provider limit, not configuration.
- SDK types used internally (not exposed): `OpenAIEmbeddingCollection` (`IReadOnlyList<OpenAIEmbedding>`), `OpenAIEmbedding.Index` (int), `OpenAIEmbedding.ToFloats()`.

## Interfaces
No public interface/contract changes. `IEmbeddingGenerator<string, Embedding<float>>.GenerateAsync` signature is unchanged. No DI registration changes in `OpenAiAdapterServiceCollectionExtensions.AddOpenAiAdapter` beyond what's needed to support the constructor field for the reused `EmbeddingClient` (still resolved from `IOptions<OpenAiEmbeddingOptions>` + `ILogger` only). The only new "surface" is the `internal` test-only constructor overload (FR-7), visible solely to `Anela.Heblo.Adapters.OpenAI.Tests` via `InternalsVisibleTo`.

## Dependencies and scope

**In scope:**
- `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiEmbeddingGenerator.cs` — the core fix (chunking, batch call, Index-sort, client reuse, internal test ctor).
- `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/AssemblyInfo.cs` — new file, `InternalsVisibleTo("Anela.Heblo.Adapters.OpenAI.Tests")`, mirroring Flexi/Plaud.
- New test project `backend/test/Anela.Heblo.Adapters.OpenAI.Tests` (net8.0, xunit + FluentAssertions + Moq, `Microsoft.NET.Test.Sdk`, single `ProjectReference` to the adapter project — same shape as `Anela.Heblo.Adapters.OpenMeteo.Tests`).
- Registration of the new test project in `Anela.Heblo.sln` — both the `Project(...)` line and the `GlobalSection(ProjectConfigurationPlatforms)` block. Architecture review flagged this explicitly: a `.csproj` not registered in the `.sln` is invisible to solution-level `dotnet build`/`dotnet test`, which is how this repo's validation step runs.

**Out of scope:**
- Any change to the 3 caller call sites — they already batch correctly.
- Token-budget-aware chunking beyond the item-count cap.
- Bounded-concurrency/parallel chunk calls — sequential is the confirmed default; no caller today approaches >2048 items.
- Broader resilience/observability changes (per-chunk metrics, structured logging beyond what exists).
- Any other adapter in `Anela.Heblo.Adapters.OpenAI` not touched by this finding.
- Introducing `IEmbeddingClientWrapper` or any other new production abstraction — explicitly rejected in both design and architecture review as premature for a single call site.

**Dependencies:**
- `OpenAI` NuGet package 2.8.0 (already referenced) — no upgrade needed; `GenerateEmbeddingsAsync` confirmed present by reflection.
- `System.ClientModel` (transitive via `OpenAI` package) for `HttpClientPipelineTransport`, used only in tests.

## Rough plan
1. In `OpenAiEmbeddingGenerator.cs`: add a private `MaxBatchSize = 2048` constant; add a constructor field for `EmbeddingClient`, built once from `IOptions<OpenAiEmbeddingOptions>` in the public constructor (FR-6); add an `internal` constructor overload accepting a pre-built `EmbeddingClient` directly for test injection (FR-7).
2. Replace the `foreach` single-item loop with: guard clauses unchanged → empty-input short-circuit unchanged → `inputList.Chunk(MaxBatchSize)` → for each chunk, `Pipeline.ExecuteAsync(... client.GenerateEmbeddingsAsync(chunk, embeddingOptions, cancellationToken: token) ...)` → sort the returned `OpenAIEmbeddingCollection` by `.Index` (unconditionally) → map each to `Embedding<float>` via `.ToFloats()` → append to the running `GeneratedEmbeddings<Embedding<float>>` in chunk order.
3. Add `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/AssemblyInfo.cs` with `[assembly: InternalsVisibleTo("Anela.Heblo.Adapters.OpenAI.Tests")]`.
4. Create `backend/test/Anela.Heblo.Adapters.OpenAI.Tests` (csproj mirroring `Anela.Heblo.Adapters.OpenMeteo.Tests`), and register it in `Anela.Heblo.sln` (Project entry + ProjectConfigurationPlatforms block).
5. Write tests per FR-1 through FR-7: single batch (1 call), oversized batch (2 calls, 2048+452), shuffled-response ordering (Index-sort correctness), empty input (0 calls), transient failure + retry (Polly still applies), client-reuse (one `EmbeddingClient` across multiple `GenerateAsync` calls) — using the fake-transport seam (`HttpClientPipelineTransport` wrapping a fake `HttpMessageHandler`, same pattern as `ShoptetPriceClientHttp500Tests`/`HomeAssistantRetryPipelineTests`).
6. Run `dotnet build` and `dotnet format` on the solution; run the full backend test suite (or at minimum the new test project plus any project referencing `Anela.Heblo.Adapters.OpenAI`) and confirm green.
7. Sanity-check the 3 caller call sites still compile and behave identically (no code changes expected, but confirm no implicit API surface break).

## Open questions
- **Exact `MaxBatchSize` value**: this plan and all prior steps assume 2048 (OpenAI's documented embeddings-endpoint item cap). Confirm against current OpenAI API docs at implementation time rather than trusting this number verbatim — if changed, only the constant's value changes, not the design.
- **Exact SDK type name/casing** (`OpenAIEmbeddingCollection` vs. any typo carried through prior docs): verify via IDE autocomplete against the actually-referenced `OpenAI` 2.8.0 package when writing the code, since this has been transcribed by hand across three prior documents.
- Everything else flagged as open in plan-01.md (chunk concurrency, testability approach) was resolved during design/architecture review and is now a settled decision reflected in the FRs above, not an open question.
