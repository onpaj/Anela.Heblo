# Architecture Review: Move MeetingTasks-specific keyed `IChatClient` out of the Anthropic adapter

## Skip Design: true

## Architectural Fit Assessment

This is a textbook application of a pattern the codebase already has exactly one instance of, and that instance already works in production: `KnowledgeBaseModule.cs:58-64` registers a KB-scoped keyed `IChatClient` — a `PostAnswerEnrichmentMiddleware` decorator wrapping `sp.GetRequiredService<IChatClient>()` — inside the Application-layer feature module that consumes it, under a single constant (`KnowledgeBaseConstants.EnrichedChatClientKey`) defined and consumed in one place. That pattern was itself the fix for finding #3770, which removed KB-specific logic from this same `AnthropicAdapterServiceCollectionExtensions.cs` file. MeetingTasks never got the same treatment and currently regresses to the pre-#3770 shape: the generic adapter holds a `MeetingTasksModule`-specific key (`MeetingExtractionClientKey = "meeting-extractor"`) and a byte-for-byte duplicate of the `AddChatClient` registration two lines above it, purely to give MeetingTasks a private name for the same client. `MeetingTasksConstants.ExtractionChatClientKey` then independently re-declares the identical string, so the two constants have no compiler-enforced link — a rename of either one is a silent runtime break (`GetRequiredKeyedService` throws `InvalidOperationException` on first meeting-transcript processing), not a build error.

Integration points are narrow and already enumerated correctly in the spec:
- `Anela.Heblo.Adapters.Anthropic/AnthropicAdapterServiceCollectionExtensions.cs` — loses the keyed registration and the constant.
- `Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs` — gains the keyed registration (alias/decorator form).
- `MeetingTasksConstants.cs` — unchanged; becomes the sole owner of the key literal (it already is the intended owner; the adapter's copy was always the redundant one).
- Composition root (`Program.cs`) — unaffected. `AddApplicationServices` (which transitively calls `AddMeetingTasksModule` and `AddKnowledgeBaseModule`) runs at line 104, *before* `AddAnthropicAdapter` at line 120. This ordering already exists today for `KnowledgeBaseModule`'s identical keyed-decorator pattern, and it works, because `AddKeyedSingleton`'s factory delegate captures `IServiceProvider` and only resolves `sp.GetRequiredService<IChatClient>()` lazily on first use — by which point the container is fully built and `AddAnthropicAdapter`'s default registration is present regardless of `Add*` call order. No composition-root change is needed or should be made.

The `Anela.Heblo.Adapters.Anthropic.csproj` already references `Anela.Heblo.Application.csproj` (line 19) — this reference exists for other reasons (adapter needs Application-layer abstractions) and is unaffected by this change; removing MeetingTasks knowledge from the adapter doesn't make the reference unused, so no `.csproj` edit is needed or in scope.

## Proposed Architecture

### Component Overview

```
Before:
┌────────────────────────────────────────┐        ┌──────────────────────────────┐
│ Anela.Heblo.Adapters.Anthropic          │        │ Anela.Heblo.Application       │
│  AnthropicAdapterServiceCollectionExt.  │        │  Features/MeetingTasks        │
│                                          │        │                                │
│  const MeetingExtractionClientKey ──────┼───X    │  const ExtractionChatClientKey│
│    = "meeting-extractor"        (dup A) │  no    │    = "meeting-extractor" (dup B)│
│                                          │  link  │                                │
│  AddChatClient(...)  ─── default IChatClient      │  GetRequiredKeyedService<IChatClient>
│  AddKeyedSingleton<IChatClient>(         │        │    (ExtractionChatClientKey)  │
│    MeetingExtractionClientKey,           │        │    → consumed by             │
│    new AnthropicChatClient(...))  ◄── reconstructs the SAME client, keyed differently
└────────────────────────────────────────┘        └──────────────────────────────┘

After:
┌────────────────────────────────────────┐        ┌──────────────────────────────────────┐
│ Anela.Heblo.Adapters.Anthropic          │        │ Anela.Heblo.Application               │
│  AnthropicAdapterServiceCollectionExt.  │        │  Features/MeetingTasks                │
│                                          │        │                                        │
│  AddChatClient(...) ─── default IChatClient ─────┼──► MeetingTasksConstants                │
│    (unkeyed, only registration)         │  sp.GetRequiredService<IChatClient>()  │  .ExtractionChatClientKey (sole owner)
│  (no MeetingTasks knowledge at all)     │        │  AddKeyedSingleton<IChatClient>(       │
│                                          │        │    ExtractionChatClientKey,            │
│                                          │        │    (sp,_) => sp.GetRequiredService<IChatClient>())
│                                          │        │  → GetRequiredKeyedService<IChatClient>│
│                                          │        │    (ExtractionChatClientKey) [unchanged]│
│                                          │        │    → ClaudeMeetingTaskExtractor        │
└────────────────────────────────────────┘        └──────────────────────────────────────┘
```

This is structurally identical to `KnowledgeBaseModule`'s existing wiring, except MeetingTasks' keyed client is a pure alias of the default client (no decoration applied), whereas KB's wraps it in `PostAnswerEnrichmentMiddleware`. That asymmetry is expected and fine — MeetingTasks simply doesn't need decoration today; the keying exists so a future MeetingTasks-specific behavior (e.g. a different model, a transcript-specific system prompt, retry policy) can be added later by changing only `MeetingTasksModule.cs`, without ever touching the adapter again.

### Key Design Decisions

#### Decision 1: Alias the default `IChatClient` vs. reconstruct `AnthropicChatClient` from scratch

**Options considered:**
1. Move the keyed registration into `MeetingTasksModule`, but keep constructing `new AnthropicChatClient(IOptions<AnthropicOptions>, IHttpClientFactory, ILogger<AnthropicChatClient>)` directly, matching the adapter's original code verbatim.
2. Move the keyed registration into `MeetingTasksModule`, but implement it as `(sp, _) => sp.GetRequiredService<IChatClient>()` — an alias of the already-registered default client, exactly mirroring how `KnowledgeBaseModule` builds its keyed client from `sp.GetRequiredService<IChatClient>()` (there, wrapped in a decorator; here, passed through unchanged).

**Chosen approach:** Option 2.

**Rationale:** The original adapter-side registration was byte-for-byte identical to the default `AddChatClient(...)` call two lines above it — same `IOptions<AnthropicOptions>`, same named `HttpClient`, same logger type, `.UseLogging()` decoration excepted (a discrepancy worth flagging — see Specification Amendments). It applies zero MeetingTasks-specific behavior. Option 1 would force `MeetingTasksModule` (Application layer) to take a direct dependency on `Anela.Heblo.Adapters.Anthropic` types (`AnthropicChatClient`, `AnthropicOptions`) that it does not otherwise need, which is a heavier coupling in the wrong direction — Application reaching into a concrete Adapter implementation type instead of programming against the `IChatClient` abstraction it already imports (`using Microsoft.Extensions.AI;`). Option 2 needs nothing beyond `Microsoft.Extensions.AI.IChatClient` and `Microsoft.Extensions.DependencyInjection`, both already referenced by `MeetingTasksModule.cs`. It also exactly matches the KB precedent's shape (`sp.GetRequiredService<IChatClient>()` as the alias root), which is the strongest available argument for correctness in a codebase with exactly one working example of this pattern.

## Implementation Guidance

### Directory / Module Structure

No new files or directories. Two existing files change:

- `backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicAdapterServiceCollectionExtensions.cs` — delete the `MeetingExtractionClientKey` constant and the `AddKeyedSingleton<IChatClient>(MeetingExtractionClientKey, ...)` block (current lines 11, 36–40). File ends up containing only options binding, the named `HttpClient`, and the single unkeyed `AddChatClient(...).UseLogging()` registration.
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs` — add the keyed registration inside `AddMeetingTasksModule`, before or alongside the existing `services.AddScoped<IMeetingTaskExtractor>(...)` factory (that factory is the sole consumer and must be able to resolve the key at runtime — registration order relative to it doesn't matter for correctness since both are `Add*` calls, but placing the keyed registration immediately above the extractor factory keeps the "why is this here" story readable top-to-bottom, matching how `KnowledgeBaseModule` places its keyed registration directly above the pipeline behavior that implicitly relies on it).

`MeetingTasksConstants.cs` does not change — it was already correctly scoped (`internal`, single feature namespace); it simply stops having a rival definition elsewhere.

### Interfaces and Contracts

No public interface changes. The only "contract" is the DI key string itself, and its ownership:

- **Removed**, `Anela.Heblo.Adapters.Anthropic` namespace: `public const string MeetingExtractionClientKey`.
- **Unchanged in value and visibility**, `Anela.Heblo.Application.Features.MeetingTasks` namespace: `internal const string ExtractionChatClientKey = "meeting-extractor";` in `MeetingTasksConstants.cs` — remains `internal` because, post-refactor, both the registration (`MeetingTasksModule.cs`) and the consumption (`MeetingTasksModule.cs`'s `IMeetingTaskExtractor` factory) live in the same assembly and the same namespace. No reason to widen visibility.
- **Added**, `MeetingTasksModule.AddMeetingTasksModule`:
  ```csharp
  // MeetingTasks-scoped alias for the default IChatClient, keyed for extraction use.
  // Kept in this module (not the generic Anthropic adapter) so the adapter has no
  // compile-time knowledge of MeetingTasks. Mirrors KnowledgeBaseModule's keyed-client pattern.
  services.AddKeyedSingleton<IChatClient>(MeetingTasksConstants.ExtractionChatClientKey,
      (sp, _) => sp.GetRequiredService<IChatClient>());
  ```
  Place this comment/registration pair analogously to KnowledgeBaseModule.cs:58-64 — same rationale, same shape, one-line difference (alias vs. decorator).

### Data Flow

Unchanged at runtime, only the registration site moves:

1. Startup: `Program.cs` calls `AddApplicationServices` (→ `AddMeetingTasksModule`, registers keyed alias factory, not yet invoked) then later `AddAnthropicAdapter` (→ registers default unkeyed `IChatClient` factory, not yet invoked). Order between these two calls is irrelevant — both are lazy `Add*` registrations against the same `IServiceCollection`; no factory runs during either call.
2. First meeting-transcript request creates a scope, which resolves `IMeetingTaskExtractor` → its factory calls `sp.GetRequiredKeyedService<IChatClient>(MeetingTasksConstants.ExtractionChatClientKey)`.
3. That keyed singleton factory runs (once, then cached): `(sp, _) => sp.GetRequiredService<IChatClient>()` → resolves the default unkeyed singleton `IChatClient` (built by `AddAnthropicAdapter`'s `AddChatClient(...).UseLogging()`), which by now is guaranteed registered since the whole container is built.
4. `ClaudeMeetingTaskExtractor` receives that `IChatClient` instance exactly as before — same concrete type, same `.UseLogging()` decoration it previously lacked (see Specification Amendments), same underlying `AnthropicChatClient`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Silent behavior change: the new alias resolves the **`.UseLogging()`-decorated** default `IChatClient`, whereas the old adapter-side registration constructed a **raw, undecorated** `AnthropicChatClient` bypassing `UseLogging()`. This is a real (small) behavior delta the spec's "no behavior change" framing understates. | Low | Acceptable and arguably a fix, not a regression — MeetingTasks extraction calls gain the same request/response logging every other `IChatClient` consumer already has. Call this out explicitly in the PR description so it isn't mistaken for scope creep; no code change needed beyond the planned alias. |
| No DI-container-validation test exists for MeetingTasks' keyed client (unlike KB, which has `KnowledgeBaseChatClientWiringTests.cs` asserting the default client stays undecorated and the keyed client carries its decoration). Spec explicitly puts this out of scope. | Medium | Out of scope for *this* refactor per spec, but flag it as a fast-follow: an analogous `MeetingTasksChatClientWiringTests.cs` (`AddAnthropicAdapter` + `AddMeetingTasksModule` → resolve both the default and keyed `IChatClient`, assert the keyed one resolves without throwing) would have caught the original duplication bug at build time instead of first-transcript-processing time, and costs about as much as the KB one already in the test suite. |
| A future engineer adds MeetingTasks-specific decoration (e.g. a stricter timeout) directly onto the *default* `IChatClient` registration in the adapter, recreating today's coupling instead of decorating the keyed alias in `MeetingTasksModule`. | Low | Structural, not code — the adapter's `AddChatClient` call and its doc comments should stay generic; any MeetingTasks-only behavior belongs on the `(sp,_) => ...` factory inside `MeetingTasksModule`, matching how KB's factory wraps in `PostAnswerEnrichmentMiddleware`. No enforcement mechanism proposed here beyond code review — same as the KB precedent relies on today. |

## Specification Amendments

1. **NFR-1 (Performance) / "no behavior change" framing should be softened.** The spec states resolving via `sp.GetRequiredService<IChatClient>()` is "equivalent" to the previous direct construction. It is equivalent in *construction cost* (singleton, resolved once either way) but not in *decoration*: the previous adapter-side keyed registration did **not** apply `.UseLogging()`, while the new alias resolves the `.UseLogging()`-decorated default client. Functionally this only adds logging around MeetingTasks' Claude calls — not a concern — but the spec's FR-4 acceptance criteria ("identical in effect to before the refactor") is technically not met at the byte level. Recommend amending FR-4 to note this expected, benign delta explicitly rather than asserting strict identity.
2. **Recommend (non-blocking, does not change spec scope) adding the wiring test** described in the Risks table above, in the same PR or an immediate follow-up, given the precedent already exists verbatim for KnowledgeBase and the marginal cost is low. The spec's "Out of Scope" section already anticipated and consciously deferred this — no disagreement with that call, just reinforcing it's worth revisiting soon given how directly the existing KB test transfers.

No other amendments — FR-1 through FR-4, the Data Model/API sections, and the Open Question's resolution (alias over reconstruction) all hold up against the actual code and match the one existing precedent in this codebase.

## Prerequisites

None. No migrations, no new configuration, no new infrastructure. The only "precondition" is what already holds true today: `AddAnthropicAdapter` must be called somewhere in the same `IServiceCollection` as `AddMeetingTasksModule` before the container is asked to resolve `IChatClient` at request time — true today, verified in `Program.cs`, and unaffected by this refactor since call order between the two `Add*` methods does not change.
