# Architecture review: design-02.md against the live codebase

Verdict: **design-02.md is approved for implementation as written.** Every concrete claim in
it — SDK member signatures, existing file contents, `.sln` structure, sibling-project
templates, and caller index-correlation — was independently re-verified against this
worktree (not re-trusted from architecture-01.md), and all of them hold exactly. One
non-blocking observation is added below (§4) about a latent, pre-existing Polly/exception-type
gap that this design correctly leaves untouched rather than silently "fixing" out of scope.

## 1. Alignment with existing patterns and integration points — re-verified independently

I did not take architecture-01's reflection results on faith; I re-ran them against this
worktree's actual restored `OpenAI` 2.8.0 / `System.ClientModel` 1.8.1 packages via a scratch
console app (`~/.nuget/packages/openai/2.8.0`, referenced the same way the adapter project
does) and via direct HTTP simulation:

- `EmbeddingClient` constructors confirmed exactly as design-02 §1.2/§3.2 assumes:
  `(string, string)`, `(string, ApiKeyCredential)`, `(string, ApiKeyCredential,
  OpenAIClientOptions)`, plus two `AuthenticationPolicy` overloads. **No**
  `(string, string, OpenAIClientOptions)` overload exists — confirming the test seam must go
  through `ApiKeyCredential`, exactly as design-02 §3.2 does.
- `EmbeddingClient.GenerateEmbeddingsAsync(IEnumerable<string>, EmbeddingGenerationOptions,
  CancellationToken)` confirmed present, returns
  `Task<ClientResult<OpenAIEmbeddingCollection>>`.
- `OpenAIEmbeddingCollection` confirmed to implement `IReadOnlyList<OpenAIEmbedding>` (among
  other list interfaces) — an ordered, indexable collection, consistent with `.OrderBy(e =>
  e.Index)` being a normal LINQ op over it.
- `OpenAIEmbedding` confirmed to expose `Index` (`Int32`) and `ToFloats()` (returns
  `ReadOnlyMemory<float>`) — exactly as design-02 §1.2 uses them.
- `HttpClientPipelineTransport(HttpClient)` constructor confirmed to exist on the concrete,
  public `System.ClientModel.Primitives.HttpClientPipelineTransport`, and
  `OpenAIClientOptions.Transport` confirmed typed as the abstract `PipelineTransport` base —
  exactly the shape design-02 §1.3/§3.2's fake-transport test seam depends on.
- **Live-fired the actual failure path**: built a real `EmbeddingClient` wired to a fake
  `HttpMessageHandler` returning HTTP 500, called `GenerateEmbeddingsAsync`, and observed it
  throw `System.ClientModel.ClientResultException` (message `"HTTP 500 (...)"`), **not**
  `HttpRequestException`. See §4 — this doesn't block the design but is worth recording.

Codebase-side claims re-checked directly, not re-quoted from architecture-01:

- **Current production code** (`OpenAiEmbeddingGenerator.cs`, read fresh from disk) matches
  design-02's stated starting point exactly: single-item `foreach` loop calling
  `client.GenerateEmbeddingAsync`, `EmbeddingClient` allocated fresh every `GenerateAsync`
  call, no empty-input short-circuit (implicitly empty via a no-op loop), guard-then-use
  `ApiKey` check as the first line. Design-02's diff is additive/replacing exactly what it
  claims to touch, nothing more.
- **DI registration** (`OpenAiAdapterServiceCollectionExtensions.AddOpenAiAdapter`) confirmed
  to call the 2-arg public constructor only — the new internal 3-arg overload design-02 adds
  cannot collide with or change this factory lambda.
- **`OpenAiEmbeddingOptions`** confirmed to be a `public class` with mutable
  auto-properties (`ApiKey`, `EmbeddingModel`, `EmbeddingDimensions`) — the
  `Options.Create(new OpenAiEmbeddingOptions { ... })` pattern design-02's test fixture uses
  will compile as written.
- **Flexi's `AssemblyInfo.cs`** read verbatim: single-line
  `[assembly: InternalsVisibleTo("Anela.Heblo.Adapters.Flexi.Tests")]`, no extra ceremony —
  confirms design-02 §1.3's claim that the plain Flexi form (not Plaud's commented,
  multi-target form) is the correct template to copy for a single-assembly grant.
- **`Anela.Heblo.Adapters.OpenMeteo.Tests.csproj`** read in full: package set, versions, and
  `<Using Include="Xunit" />` shape match design-02 §3.1's csproj **character for character**
  (only the `ProjectReference` path differs, as expected).
- **`Anela.Heblo.sln`** grepped directly: `Anela.Heblo.Adapters.OpenMeteo.Tests`'s
  `ProjectConfigurationPlatforms` block is exactly at lines 422–433 as design-02 §4 states, and
  its `NestedProjects` entry (`{399B6C8C-...} = {23FE24B3-...}`) is exactly at line 529 as
  stated. The `test` solution folder GUID `{23FE24B3-CD9D-4576-A7C8-85D5B012F43D}` design-02
  names is confirmed correct (line 9 declares that folder). All three `.sln` edit points are
  real and correctly located.
- **Caller index-correlation** (the load-bearing justification for FR-3's mandatory sort) —
  grepped all three call sites fresh:
  - `KnowledgeBaseDocIndexingStrategy.cs:44,57` — `embeddings[i].Vector` against `summaries`
    built at the same index.
  - `ConversationIndexingStrategy.cs:30,43` — same pattern against `topics`.
  - `LeafletIndexingService.cs:61` — `GenerateAsync(inputs)` (batched call site; downstream
    indexing confirmed positional in the surrounding method).
  
  Confirmed: all three are raw positional index correlation with no ID/key matching. This
  make-or-break assumption behind FR-3 is real, not inherited-and-unchecked.

No boundary, DI shape, or public contract changes are introduced anywhere in design-02 —
confirmed directly against the current `AddOpenAiAdapter` and the current
`IEmbeddingGenerator<string, Embedding<float>>` implementation. Nothing here conflicts with
`docs/architecture/development_guidelines.md`'s module-boundary rules or the DTO-is-a-class
rule (`OpenAiEmbeddingOptions` is an options POCO, not a DTO crossing the OpenAPI boundary, so
the "DTOs are classes not records" rule doesn't even apply here — it was already a class before
this change).

## 2. Proposed architecture — no changes from architecture-01's decisions

Nothing in design-02 alters the structural decisions already settled and verified in
architecture-01.md: one class, unchanged public contract, unchanged DI registration, no new
production abstraction (`IEmbeddingClientWrapper` correctly rejected, and re-confirmed correct
here — there is still exactly one SDK call site). Design-02 is purely the concrete
implementation of those already-approved decisions; I re-derived the same conclusion
independently rather than rubber-stamping it.

## 3. Implementation guidance — confirmed sound, no corrections needed

- **FR-1–FR-3 (batch call, chunking, mandatory Index-sort)**: the exact code in design-02 §1.2
  compiles against the real SDK surface as verified in §1 above. `OrderBy(e => e.Index)` on an
  `IReadOnlyList<OpenAIEmbedding>` is unremarkable LINQ; no surprises.
- **FR-4 (resilience)**: the `Pipeline.ExecuteAsync` wrapper is structurally unchanged — same
  delegate shape, same `Pipeline` static field, only the SDK call inside changed. This is a
  faithful "preserve" as claimed. See §4 for a scope-adjacent observation, not a defect in this
  design.
- **FR-5 (unchanged output contract)**: verified — no caller requires modification; verified
  directly by reading all three call sites (§1) rather than trusting the plan's claim.
- **FR-6/FR-7 (client reuse + testability seam)**: the internal-ctor-delegates-to-internal
  pattern in design-02 §1.2 is structurally identical to what's really in
  `PlaudTokenRefresher.cs` (public ctor at line 25, internal ctor at line 32) — confirmed by
  reading the file, not just trusting the citation.
- **`.sln` registration (§4)**: all three edit points (Project block, ProjectConfigurationPlatforms
  block, NestedProjects line) verified present and correctly targeted in the current `.sln`, as
  detailed in §1.

## 4. Risks and mitigations

| Risk | Status |
|---|---|
| Silent embedding/text misalignment if SDK response order isn't request order | Mitigated — unconditional `OrderBy(e => e.Index)`, confirmed correct against real `OpenAIEmbeddingCollection` shape (§1). |
| New test project silently excluded from `dotnet build`/CI | Mitigated — all three required `.sln` edits identified at the correct, verified line numbers (§1). |
| `MaxBatchSize = 2048` becomes stale if OpenAI changes the limit | Unchanged from architecture-01: dev-time doc check flagged, no architectural fix needed. |
| **(New, non-blocking) Polly `ShouldHandle<HttpRequestException>()` does not catch `ClientResultException`** | **Observation, not a defect in this design.** Live-tested (§1): a real HTTP 500 from the OpenAI API surfaces through `EmbeddingClient.GenerateEmbeddingsAsync` as `System.ClientModel.ClientResultException`, not `HttpRequestException`. The existing `Pipeline`'s retry predicate only handles `HttpRequestException` — so today, and still after this fix, a real OpenAI-side error response (500, 429 rate-limit, etc.) will **not** be retried by Polly; only true transport-level failures (DNS, connection refused, which `HttpClient` itself throws as `HttpRequestException` before any response is received) are retried. **This is pre-existing behavior, not introduced or worsened by design-02** — the single-item code being replaced has the exact same gap. FR-4 correctly scopes itself to "preserve," not "fix," and plan-02.md's out-of-scope section correctly excludes "broader resilience/observability changes." Design-02's test cases 5/6 (§3.3) are internally consistent with this: they simulate failure by having the fake handler throw `HttpRequestException` directly (not by returning a 500 status), which is the only failure mode the current Polly config actually retries on — so the tests validate real, current behavior rather than a fictional one. No action required for this task; worth a follow-up issue if the team wants Polly to also retry on `ClientResultException` with retryable status codes (429, 500, 502, 503), but that is out of scope here. |
| Chunking changes behavior for the (currently nonexistent) >2048-item caller | Unchanged from architecture-01: no current caller approaches this size; sequential chunking remains correct default. |

## Prerequisites before implementation begins

None blocking. Every concrete file path, SDK signature, `.sln` line range, and caller
assumption design-02 relies on has now been independently re-verified against this worktree's
actual state (not re-derived from architecture-01's prior verification). Implementation may
proceed exactly per design-02.md §1–§5. The one new observation (§4, Polly exception-type gap)
is informational and does not change design-02's scope, FR list, or acceptance criteria.
