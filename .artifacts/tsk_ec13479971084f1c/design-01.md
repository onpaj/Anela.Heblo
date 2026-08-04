# Design: OpenAiEmbeddingGenerator honours the batch contract

No UI — this is a backend adapter-internals fix. UX/UI section omitted.

## 1. Component design

### 1.1 Boundary (unchanged)

`OpenAiEmbeddingGenerator` remains the sole implementation of
`IEmbeddingGenerator<string, Embedding<float>>` registered in
`OpenAiAdapterServiceCollectionExtensions.AddOpenAiAdapter`. No new public
type, no DI shape change, no caller-visible change. All three callers
(`LeafletIndexingService`, `KnowledgeBaseDocIndexingStrategy`,
`ConversationIndexingStrategy`) keep calling
`GenerateAsync(IEnumerable<string>)` exactly as today.

### 1.2 Internal restructure

Confirmed against the installed `OpenAI` 2.8.0 assembly
(`~/.nuget/packages/openai/2.8.0/lib/net8.0/OpenAI.dll` — verified by
symbol inspection, no public docs needed):

- `EmbeddingClient.GenerateEmbeddingsAsync(IEnumerable<string> inputs, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)` returns `Task<ClientResult<OpenAIEmbeddingCollection>>`.
- `OpenAIEmbeddingCollection` is an ordered collection of `OpenAIEmbedding`; each element exposes `Index` (its position in the request) and `ToFloats()` (same shape used today for the single-item result).

Responsibility split inside `GenerateAsync`, in call order:

1. **Guard clause** (unchanged) — throw `InvalidOperationException` if `ApiKey` is empty; short-circuit to an empty `GeneratedEmbeddings<Embedding<float>>` if `values` is empty, without touching the client.
2. **Client acquisition** — reuse a single `EmbeddingClient` per `OpenAiEmbeddingGenerator` instance (FR-5) instead of `new EmbeddingClient(...)` per call. Since the generator is registered via a factory lambda in `AddOpenAiAdapter` and resolved as a singleton by `AddEmbeddingGenerator`, constructing the client once in the constructor is safe — `ApiKey`/`EmbeddingModel` are read once from `IOptions<OpenAiEmbeddingOptions>` at startup and don't change afterward. The empty-`ApiKey` guard stays a per-call check at the top of `GenerateAsync` (not moved to the constructor) so a misconfigured app still fails with the same exception at first use rather than at DI-resolution time.
3. **Chunker** — a private helper that splits `inputList` into chunks of at most `MaxBatchSize` (constant, `2048` — OpenAI's documented per-request item cap for the embeddings endpoint; re-confirm against current OpenAI docs at implementation time before hardcoding). For the common case (`inputList.Count <= MaxBatchSize`), this yields exactly one chunk — no behavior change from "just call once."
4. **Batch call per chunk** — for each chunk, call `Pipeline.ExecuteAsync(async token => await client.GenerateEmbeddingsAsync(chunk, embeddingOptions, cancellationToken: token), cancellationToken)`. This is the same Polly `Pipeline` instance and policy as today (3 retries, exponential backoff from 2s, on `HttpRequestException`) — only the delegate body changes from a single-item call to a batch call. Chunks are processed **sequentially** (matches current sequential semantics, avoids concurrent-request complexity for a case with no known caller today — see plan's open question, resolved as sequential-by-default).
5. **Result assembly** — for each chunk's `OpenAIEmbeddingCollection`, map every `OpenAIEmbedding` to `Embedding<float>` via `.ToFloats()` (same conversion as today) and append to the running `GeneratedEmbeddings<Embedding<float>>` in the collection's own order (the SDK returns embeddings in request order per the `Index` field — no re-sorting needed unless a discrepancy is observed at implementation time, in which case sort by `Index` before mapping as a defensive measure). Chunk results are concatenated in chunk order, so the final list matches `inputList`'s original order end-to-end.

No new abstractions are introduced beyond a private, file-scoped chunking helper (e.g., a local `static IEnumerable<List<string>> Chunk(List<string> source, int size)` or `inputList.Chunk(MaxBatchSize)` using the built-in `Enumerable.Chunk` — .NET 8 has this in the BCL, so no hand-rolled chunker is needed).

### 1.3 Testability decision

The plan flagged an open question: is `EmbeddingClient` mockable enough to
assert call counts? Resolution for this design: **don't introduce a new
`IEmbeddingClientWrapper` seam.** `EmbeddingClient` accepts an
`OpenAIClientOptions` with a custom `Transport`, which lets a test supply a
fake `PipelineTransport`/`HttpMessageHandler` that records the number and
bodies of outbound HTTP requests without hitting the network — this is
the standard test seam for `System.ClientModel`-based SDK clients
(OpenAI's SDK is built on `System.ClientModel`, same as Azure SDKs, which
use exactly this pattern for their own unit tests). Introducing a
hand-rolled wrapper interface purely to dodge a well-supported SDK test
seam would add an abstraction with no other consumer — against the
project's "no premature abstraction" rule. If, at implementation time,
`EmbeddingClient` genuinely cannot be constructed with a fake transport
in this SDK version, fall back to asserting behavior indirectly (e.g., a
subclass-free integration-style test hitting a local
`HttpListener`/`WireMock`-style stub bound via `OpenAIClientOptions.Transport`)
rather than adding a production-only test seam.

## 2. Data / contracts

No schema changes — no DB, no HTTP request/response contracts, no event
payloads. This is a pure internal refactor of one adapter method.

- **Input** (unchanged): `IEnumerable<string>` — raw text chunks/summaries/topics.
- **Output** (unchanged): `GeneratedEmbeddings<Embedding<float>>`, order-correlated to input, same `Embedding<float>` construction (`ReadOnlyMemory<float>` from `ToFloats()`).
- **Config** (unchanged): `OpenAiEmbeddingOptions.{ApiKey, EmbeddingModel, EmbeddingDimensions}`.
- **New internal constant**: `MaxBatchSize = 2048` (or the value confirmed against current OpenAI docs), private to `OpenAiEmbeddingGenerator` — not configuration, since it's a hard provider limit, not a tunable.

## 3. Test plan (design-level — no task breakdown, just what "correct" looks like)

New test project `backend/test/Anela.Heblo.Adapters.OpenAI.Tests`
(following the sibling adapter test projects' shape — xunit + FluentAssertions
+ Moq, `ProjectReference` to the adapter project), asserting against a fake
transport:

1. N inputs (N ≤ 2048) → exactly 1 HTTP request to the embeddings endpoint, body contains all N inputs, result has N entries in input order.
2. N inputs (N > 2048, e.g. 2500) → exactly `ceil(N/2048)` requests (2), each within the cap; concatenated result has N entries in original order.
3. Empty input → zero HTTP requests, empty result.
4. Simulated transient failure (`HttpRequestException`) on a chunk request → retried per existing Polly policy (3 attempts, exponential backoff from 2s); exhausting retries still throws, no partial/silent result.
5. `EmbeddingClient` is constructed once per `OpenAiEmbeddingGenerator` instance across multiple `GenerateAsync` calls (verifies FR-5 without over-specifying *how* — e.g. assert the same transport/connection is reused, or expose via a testable seam that counts client constructions).

This directly encodes the plan's FR-1 through FR-5 acceptance criteria; no further design-level detail needed since implementation specifics (exact fake-transport plumbing) are a development-step concern.
