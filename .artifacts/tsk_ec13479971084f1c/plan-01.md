# Plan: OpenAiEmbeddingGenerator negates batching fix (#3590)

## Summary
`OpenAiEmbeddingGenerator.GenerateAsync` (`backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiEmbeddingGenerator.cs:36-64`) implements the batch `IEmbeddingGenerator<string, Embedding<float>>` contract by looping over inputs and calling the OpenAI SDK's single-item `EmbeddingClient.GenerateEmbeddingAsync` once per element. This turns every batched caller back into N sequential HTTP round trips. The fix is to call the SDK's batch method `EmbeddingClient.GenerateEmbeddingsAsync(IEnumerable<string>, ...)` once per (chunked) request instead of looping per item.

## Context
Issue #3590 fixed `KnowledgeBaseDocIndexingStrategy` to collect all summaries and call `GenerateAsync(summaries)` once, following the pattern already used in `ConversationIndexingStrategy`. That fix assumed the adapter honors the batch contract. It doesn't — the adapter still fans every batch out into individual OpenAI API calls, so #3590 (and `LeafletIndexingService`, which already batches all document chunks) get zero benefit: latency and cost still scale linearly with corpus size on every OneDrive ingestion, manual upload, and conversation re-index.

Confirmed via the installed `OpenAI` 2.8.0 package (`~/.nuget/packages/openai/2.8.0/lib/net8.0/OpenAI.dll`) that `EmbeddingClient.GenerateEmbeddingsAsync` exists as a genuine batch method (multiple overloads found in the assembly, consistent with `IEnumerable<string>` input + per-call `EmbeddingGenerationOptions`).

Three call sites depend on this behaving as documented:
- `backend/src/Anela.Heblo.Application/Features/Leaflet/Services/LeafletIndexingService.cs:61`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategy.cs:44`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ConversationIndexingStrategy.cs:30`

No unit tests currently exist for `OpenAiEmbeddingGenerator` (checked — no test file references the class).

## Functional requirements

**FR-1: Single batched API call for a batch that fits in one request**
`GenerateAsync` must issue exactly one call to `EmbeddingClient.GenerateEmbeddingsAsync(IEnumerable<string>, EmbeddingGenerationOptions, CancellationToken)` for an input list within the provider's per-request limits, instead of one `GenerateEmbeddingAsync` call per item.
- Acceptance: given a list of N strings (N within the batch limit), the OpenAI SDK client is invoked exactly once; the returned `GeneratedEmbeddings<Embedding<float>>` preserves input order and has exactly N entries.

**FR-2: Chunking for oversized batches**
OpenAI's embeddings endpoint caps requests at 2048 inputs per call (and a total token budget per request — model-dependent, e.g. ~300k tokens for `text-embedding-3-*`). If `inputList.Count` exceeds the provider's per-request item cap, the adapter must split into multiple `GenerateEmbeddingsAsync` calls (chunks), issued sequentially (or with bounded concurrency — see open question), and concatenate results in original order.
- Acceptance: a list of 2500 strings results in 2 batch calls (2048 + 452), not 2500 single-item calls; output order matches input order across the chunk boundary.
- Note: token-budget-based chunking is out of scope unless a caller is known to exceed it today — flag as a follow-up if callers could plausibly hit it (large leaflet/knowledge-base documents). Confirm actual per-request limits against current OpenAI docs before hardcoding the constant.

**FR-3: Preserve existing resilience behavior**
Each batch/chunk call must still go through the existing Polly `Pipeline` (3 retries, exponential backoff from 2s, on `HttpRequestException`) exactly as before — just wrapping the batch call instead of the single-item call.
- Acceptance: a transient `HttpRequestException` on a batch call is retried up to 3 times with the same backoff; a batch call that exhausts retries still throws (no swallowed failures, no silent partial results).

**FR-4: Preserve output contract**
`Embedding<float>.ToFloats()` mapping, empty-input handling (empty list → empty `GeneratedEmbeddings`, no API call), and the `Dimensions` option must behave identically to today.
- Acceptance: existing callers (`LeafletIndexingService`, `KnowledgeBaseDocIndexingStrategy`, `ConversationIndexingStrategy`) require no changes; an empty `values` enumerable returns an empty result set without hitting the network.

**FR-5 (secondary, from finding's "while there" suggestion): Reuse the `EmbeddingClient` instance**
Stop allocating a new `EmbeddingClient` on every `GenerateAsync` call (`OpenAiEmbeddingGenerator.cs:45`). Construct it once (constructor or lazy-init) and reuse across calls, since `_options.ApiKey`/`_options.EmbeddingModel` don't change after startup.
- Acceptance: `EmbeddingClient` is constructed at most once per `OpenAiEmbeddingGenerator` instance lifetime (the generator is registered as a singleton via `AddEmbeddingGenerator`).
- Treat as secondary/optional relative to FR-1–FR-4; do it only if it doesn't complicate the primary fix (e.g., watch out for `_options.ApiKey` empty-check currently happening per-call in `GenerateAsync` — must still throw if misconfigured, ideally still checked lazily so DI registration order doesn't matter).

## Non-functional requirements
- **Performance**: batched indexing of a 20-chunk document must issue 1 OpenAI round trip (or `ceil(N/2048)`), not N. This is the entire point of the fix — must be verifiable via a test that asserts call count on a mocked/fake HTTP handler or client.
- **No behavior change for callers**: none of the 3 call sites should need modification; this is purely an adapter-internal fix.
- **Backward compatibility of error semantics**: callers currently only handle `GenerateAsync` throwing after retry exhaustion; that must remain true (no new exception types leak through).

## Data model
No new entities. Existing types involved:
- Input: `IEnumerable<string>` (raw text chunks/summaries/topics from callers).
- Output: `GeneratedEmbeddings<Embedding<float>>` (MEAI contract type, order-correlated to input).
- Config: `OpenAiEmbeddingOptions` (`ApiKey`, `EmbeddingModel`, `EmbeddingDimensions`) — unchanged.

## Interfaces
No public interface/contract changes. `IEmbeddingGenerator<string, Embedding<float>>.GenerateAsync` signature is unchanged; this is purely an internal implementation fix in `OpenAiEmbeddingGenerator`. No DI registration changes expected beyond possibly moving `EmbeddingClient` construction into the constructor (FR-5).

## Dependencies and scope
**In scope:**
- `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiEmbeddingGenerator.cs` — the actual fix.
- New/updated unit tests for `OpenAiEmbeddingGenerator` covering FR-1 through FR-4 (mocking or faking the OpenAI SDK call boundary — likely via a fake `HttpClient`/`HttpMessageHandler` injected into `EmbeddingClient`, since `EmbeddingClient` itself isn't easily mockable — investigate at dev time whether `EmbeddingClient` accepts an `OpenAIClientOptions` with a custom `Transport`/`HttpClient` for testability).

**Out of scope:**
- Any change to the 3 caller call sites — they already batch correctly.
- Token-budget-aware chunking beyond the item-count cap, unless investigation shows a current caller can exceed it.
- Broader resilience/observability changes (e.g., per-chunk metrics, structured logging beyond what exists).
- Any other adapter in `Anela.Heblo.Adapters.OpenAI` not touched by this finding.

**Dependencies:**
- `OpenAI` NuGet package 2.8.0 already provides `EmbeddingClient.GenerateEmbeddingsAsync`; no package upgrade needed.
- Depends on confirming the exact overload signature/max-batch-size at dev time (via IntelliSense/decompilation or OpenAI docs), since this plan only confirmed the method exists, not its exact overloads/limits.

## Rough plan
1. Inspect `EmbeddingClient.GenerateEmbeddingsAsync` overloads (via IDE/decompile or OpenAI SDK source) to confirm exact signature and any documented max-batch-size (item count and/or token count) behavior/errors it throws on overflow.
2. Refactor `OpenAiEmbeddingGenerator.GenerateAsync`:
   - Replace the per-item `foreach` + `GenerateEmbeddingAsync` with chunking `inputList` into batches (constant chunk size, e.g. 2048, named clearly) and calling `GenerateEmbeddingsAsync` once per chunk through the existing Polly `Pipeline`.
   - Concatenate chunk results into a single `GeneratedEmbeddings<Embedding<float>>` preserving order.
   - Keep the `ApiKey` empty-check and empty-input short-circuit behavior.
3. (Secondary) Move `EmbeddingClient` construction out of the per-call path into the constructor/lazy field, reusing it across `GenerateAsync` invocations.
4. Add/extend unit tests in the adapter's test project (create one under `backend/test/` following existing adapter test conventions if none exists) asserting: single call for small batch, chunked calls for oversized batch, retry-on-transient-failure still works, empty input short-circuits, order preservation.
5. Run `dotnet build` and the full backend test suite for the touched project; run `dotnet format`.
6. Manually sanity-check with a real (or recorded) OpenAI call if feasible in dev environment — otherwise rely on unit tests since there's no sandbox/staging OpenAI key policy documented for this adapter.

## Open questions
- **Exact max-batch-size to hardcode**: assuming OpenAI's documented 2048-items-per-request cap for embeddings; needs confirmation against current OpenAI API docs at implementation time rather than trusting this plan's number verbatim.
- **Chunk concurrency**: sequential chunk calls (simple, matches current retry/logging pattern) vs. bounded-parallel chunk calls (faster for very large batches, more complex). Default to sequential per-chunk calls unless a real caller regularly exceeds 2048 items (none currently do — Leaflet/KnowledgeBase documents are unlikely to hit this in practice) — note as a possible future optimization, not needed now.
- **Testability of `EmbeddingClient`**: the class is a concrete SDK type; need to confirm at dev time whether it can be constructed with a fake `HttpClient`/`Transport` for true call-count assertions, or whether the test should instead assert via a thin seam/wrapper interface introduced for testability. If SDK is fully unmockable, consider whether this task should introduce a minimal internal abstraction (`IEmbeddingClientWrapper`) purely to make FR-1/FR-2 verifiable — flag this as a judgment call for the architecture step.
