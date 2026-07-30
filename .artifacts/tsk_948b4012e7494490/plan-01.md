# Plan: Stop the Anthropic adapter from baking KnowledgeBase's answer-enrichment middleware into the default `IChatClient`

## Summary
`AnthropicAdapterServiceCollectionExtensions` (a generic, feature-agnostic adapter) imports `Anela.Heblo.Application.Features.KnowledgeBase.Pipeline` and wires `PostAnswerEnrichmentMiddleware` — a middleware whose sole job is rewriting `[Name](CODE)`/`(CODE)` tokens into KnowledgeBase product links — into the **default, unkeyed** `IChatClient`. Every non-KnowledgeBase consumer that resolves the default client (Article, Smartsupp, Photobank, Leaflet, `RagQueryExpander`) unknowingly runs KB's product-code rewriting over its own output. The fix removes the middleware and the `Application.Features.*` import from the adapter, and moves the enrichment wiring into the KnowledgeBase module itself (which already owns both the middleware and `IProductEnrichmentCache`), applied only to the client `AskQuestionHandler` uses.

## Context — verification of the raw finding
The finding is factually accurate as written; verified directly against the current code:
- `AnthropicAdapterServiceCollectionExtensions.cs:1` imports `Anela.Heblo.Application.Features.KnowledgeBase.Pipeline`; lines 30–36 apply `.Use((inner, sp) => new PostAnswerEnrichmentMiddleware(...))` to the default `AddChatClient(...)` registration.
- `PostAnswerEnrichmentMiddleware` (`Application/Features/KnowledgeBase/Pipeline/PostAnswerEnrichmentMiddleware.cs`) is explicitly KB-scoped per its own doc comment and rewrites any bare `(CODE)`/`[Name](CODE)` token whose code matches `[A-Z0-9]+` against a catalog product lookup.
- Confirmed all six non-KB consumers named in the finding (`Article`'s `PlanQueriesStep`/`AggregateFactsStep`/`ValidateFactsStep`/`WriteArticleStep`, `Smartsupp`'s `GenerateDraftReplyHandler`, `Photobank`'s `PhotobankAutoTagJob`, `Leaflet`'s `LeafletChunkSummarizer`/`GenerateLeafletHandler`, `Shared/Rag/RagQueryExpander`) inject the plain unkeyed `IChatClient`, and only `MeetingTasks` opts out via the keyed `meeting-extractor` client.
- **Important detail not spelled out in the finding**: `AskQuestionHandler` (KnowledgeBase's own consumer) *also* injects the plain unkeyed `IChatClient` today — it does not use any keyed client. This means KnowledgeBase's actual desired enrichment behavior currently depends entirely on the fact that the shared default client happens to carry KB's middleware. Simply deleting the `.Use(...)` line would silently break KB's product-link rendering; the fix must add an equivalent, KB-scoped enrichment path for `AskQuestionHandler` at the same time it removes the global one.
- **Established pattern found in the codebase for this exact situation**: `MeetingTasksModule.cs:41-45` shows the accepted way a feature opts into Anthropic-adapter-provided behavior without the adapter knowing about the feature: the adapter (`AnthropicAdapterServiceCollectionExtensions.cs:38-41`) registers a generic keyed `IChatClient` (key `"meeting-extractor"`) with **no feature-specific decoration**, and the consumer's own module (`MeetingTasksModule`) resolves it via `sp.GetRequiredKeyedService<IChatClient>(...)`, using its own locally-defined key constant (`MeetingTasksConstants.ExtractionChatClientKey`, a duplicated string literal — not a shared reference to the adapter's constant, since `Application` cannot depend on `Adapters.Anthropic`). This is the template FR-2 below follows, adapted for KB's case where the *decoration itself* (not just the raw client) is feature-specific.
- Confirmed `Microsoft.Extensions.AI` 9.5.0 (referenced by both `Anela.Heblo.Adapters.Anthropic.csproj` and `Anela.Heblo.Adapters.OpenAI.csproj`) exposes `AddKeyedChatClient` alongside `AddChatClient`, both returning a `ChatClientBuilder` — so the adapter can keep offering a fluent, decoratable keyed client without any feature awareness, if a design alternative prefers that route (see Open Questions).
- Confirmed no existing `ModuleBoundariesTests` rule inspects `Anela.Heblo.Adapters.Anthropic` or `Anela.Heblo.Adapters.OpenAI` at all (the only adapter-assembly rules today are `ShoptetApi Adapters -> Catalog` and `-> Logistics`). This coupling was never caught by architecture tests because nothing was watching that assembly.
- Confirmed `AskQuestionHandlerTests.cs` constructs `AskQuestionHandler` directly via `new(...)` (no DI container), so adding a `[FromKeyedServices(...)]` attribute to a constructor parameter — not used anywhere yet in this codebase, but a standard .NET 8 `Microsoft.Extensions.DependencyInjection` feature — will not affect that test file; only the constructor call site's argument stays a plain `IChatClient` mock.

## Functional requirements

**FR-1 — Remove the KnowledgeBase coupling from the Anthropic adapter's default client**
- Delete `using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;` from `AnthropicAdapterServiceCollectionExtensions.cs`.
- Delete `.Use((inner, sp) => new PostAnswerEnrichmentMiddleware(inner, sp.GetRequiredService<IProductEnrichmentCache>()))` from the default `AddChatClient(...)` registration, leaving `.UseLogging()` as the only decorator.
- **Acceptance criteria**: `Anela.Heblo.Adapters.Anthropic` project has zero references to any `Anela.Heblo.Application.*` namespace (verified by the new architecture-test rule in FR-3); `dotnet build` succeeds.

**FR-2 — Give KnowledgeBase its own enrichment path, owned entirely within the KnowledgeBase module**
- In `KnowledgeBaseModule.cs` (which already references `Application.Features.KnowledgeBase.Pipeline` legitimately and already registers `IProductEnrichmentCache`), add a keyed `IChatClient` registration that decorates the plain (now enrichment-free) default `IChatClient` with `PostAnswerEnrichmentMiddleware`:
  ```csharp
  services.AddKeyedSingleton<IChatClient>(KnowledgeBaseConstants.EnrichedChatClientKey, (sp, _) =>
      new PostAnswerEnrichmentMiddleware(
          sp.GetRequiredService<IChatClient>(),
          sp.GetRequiredService<IProductEnrichmentCache>()));
  ```
- Add a small `KnowledgeBaseConstants` (or similar, name TBD in design step) holding `internal const string EnrichedChatClientKey = "knowledge-base-answer";`, mirroring `MeetingTasksConstants.ExtractionChatClientKey`.
- Update `AskQuestionHandler`'s constructor to request the keyed client:
  ```csharp
  public AskQuestionHandler(
      IMediator mediator,
      [FromKeyedServices(KnowledgeBaseConstants.EnrichedChatClientKey)] IChatClient chatClient,
      ...)
  ```
- **Acceptance criteria**: `AskQuestionHandlerTests` pass unmodified (constructor still accepts a plain `IChatClient` positionally when called directly in tests — the keyed-services attribute only affects container resolution); a new/updated test (see FR-4) confirms `AskQuestionHandler`'s answer text is enriched with product links when resolved through the real DI container, proving the wiring survived the move.

**FR-3 — Pin the decoupling with an architecture test**
- Add a `ModuleBoundaryRule` to `ModuleBoundariesTests.cs` (e.g. `"Anthropic Adapter -> Application"`), `InspectedNamespacePrefix: "Anela.Heblo.Adapters.Anthropic"`, `InspectedAssembly: "Anela.Heblo.Adapters.Anthropic"`, forbidding `"Anela.Heblo.Application"` (broad — the adapter should reference no application-layer feature namespace at all, not just KnowledgeBase), with an empty allowlist.
- Optionally add the mirror rule for `Anela.Heblo.Adapters.OpenAI -> Application` (empty allowlist) — it already has no such reference today (verified: `OpenAiAdapterServiceCollectionExtensions.cs` only reads config strings, no `Application.*` import), so this rule adds regression protection at zero cost.
- **Acceptance criteria**: both new rules pass immediately after FR-1/FR-2 land (no allowlist entries needed); the test suite fails if either adapter ever re-introduces a compile-time reference into `Application.*`.

**FR-4 — Test coverage for the new wiring**
- Existing `PostAnswerEnrichmentMiddlewareTests.cs` (unit-level, constructs the middleware directly) needs no change — the middleware class itself is untouched.
- Add a focused DI-wiring test (new or extending an existing KnowledgeBase module test, exact location TBD in design step) that builds a minimal `IServiceCollection` with `AddAnthropicAdapter` + `AddKnowledgeBaseModule`, resolves `IChatClient` keyed by `KnowledgeBaseConstants.EnrichedChatClientKey`, and asserts it is (or wraps) a `PostAnswerEnrichmentMiddleware` instance — proving KB still gets enrichment post-refactor.
- Add/extend a test asserting the plain unkeyed `IChatClient` resolved from `AddAnthropicAdapter` is **not** a `PostAnswerEnrichmentMiddleware` (guards against the regression this whole task exists to fix).
- **Acceptance criteria**: both tests pass; `dotnet build` + full backend test suite green.

## Non-functional requirements
- **No behavior change for KnowledgeBase**: `AskQuestionHandler` answers must still get product-code enrichment exactly as before — this is a pure internal wiring change, not a feature change.
- **Behavior change (bug fix) for all other consumers**: Article/Smartsupp/Photobank/Leaflet/RagQueryExpander outputs stop being silently regex-rewritten. This is the intended effect of the fix; no output-shape assertions in their existing tests are expected to depend on the old (accidental) rewriting — verify this holds when running their test suites (see Rough plan step 4).
- **Module independence**: `Anela.Heblo.Adapters.Anthropic` and `Anela.Heblo.Adapters.OpenAI` must have zero compile-time dependency on any `Anela.Heblo.Application.*` namespace, enforced going forward by FR-3.
- **No performance regression**: one extra `AddKeyedSingleton` factory registration; `AskQuestionHandler`'s hot path is unchanged (still one `IChatClient.GetResponseAsync` call, now through a keyed-resolved instance instead of the default one).

## Data model
No data/domain model changes. `PostAnswerEnrichmentMiddleware`, `IProductEnrichmentCache`, `ProductEnrichmentEntry` are unchanged in shape and behavior — only *where* the middleware is registered and resolved changes.

## Interfaces (affected types)
- `Anela.Heblo.Adapters.Anthropic.AnthropicAdapterServiceCollectionExtensions` — drops the `KnowledgeBase.Pipeline` import and the `.Use(PostAnswerEnrichmentMiddleware)` call from the default `AddChatClient` registration. `MeetingExtractionClientKey` / keyed `meeting-extractor` registration untouched.
- `Anela.Heblo.Application.Features.KnowledgeBase.KnowledgeBaseModule` — gains one new `AddKeyedSingleton<IChatClient>(...)` registration.
- New: `Anela.Heblo.Application.Features.KnowledgeBase.KnowledgeBaseConstants` (or existing constants file if one already exists in that namespace — check in design step) — `EnrichedChatClientKey` string constant.
- `Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion.AskQuestionHandler` — constructor parameter gains `[FromKeyedServices(...)]`.
- `Anela.Heblo.Tests.Architecture.ModuleBoundariesTests` — one or two new `ModuleBoundaryRule` entries (Anthropic, optionally OpenAI, both `-> Application`).

## Dependencies and scope
- Depends on: `Anela.Heblo.Adapters.Anthropic`, `Anela.Heblo.Application` (KnowledgeBase feature), `Anela.Heblo.Tests` (architecture tests).
- **In scope**: removing the middleware/import from the adapter; re-homing the enrichment decoration inside the KnowledgeBase module; updating `AskQuestionHandler`'s injection; new architecture-test rule(s); DI-wiring tests proving both the fix and the preserved KB behavior.
- **Out of scope**: the `KnowledgeBase:ChatModel` / `KnowledgeBase:EmbeddingModel` / `KnowledgeBase:ChatMaxTokens` / `KnowledgeBase:EmbeddingDimensions` config-key coupling noted in the finding's "Suggested direction" — these are read as plain configuration string paths inside `AnthropicAdapterServiceCollectionExtensions` and `OpenAiAdapterServiceCollectionExtensions` respectively, which is a naming/config-ownership smell but **not** a compile-time module-boundary violation of the kind this task's rule citation (`development_guidelines.md` §Module Independence — "no direct references between feature modules") addresses. Fixing it properly means introducing adapter-owned config keys (e.g. `Anthropic:DefaultChatModel`, `OpenAI:EmbeddingModel`) and updating `appsettings.json` + `appsettings.Production.json` (the only two files carrying these keys today), which is a distinct, separately-reviewable change. Flagged as a follow-up in Open Questions rather than bundled here, per the "surgical changes" project guideline.
- **Out of scope**: any change to `PostAnswerEnrichmentMiddleware`'s regex/enrichment logic, `IProductEnrichmentCache`/`ProductEnrichmentCache`, or the `meeting-extractor` keyed client and its consumer (`MeetingTasksModule`/`ClaudeMeetingTaskExtractor`) — that path is already correctly scoped and untouched.
- **Out of scope**: introducing a shared/generic "decorate a keyed chat client from the consumer module" helper — this task establishes one instance of the pattern; generalizing it is unwarranted premature abstraction for a single consumer.

## Rough plan
1. **Design step**: confirm the exact name/location of the new key constant (`KnowledgeBaseConstants.EnrichedChatClientKey` vs. an existing constants file in `Features/KnowledgeBase/`), confirm the keyed-client string literal (`"knowledge-base-answer"` or similar, must not collide with `"meeting-extractor"`), and settle the Option A vs. B question below (recommend Option A).
2. **Architecture step**: confirm placement of the new DI registration inside `KnowledgeBaseModule.AddKnowledgeBaseModule` (ordering relative to `IProductEnrichmentCache` registration — the factory closure needs `IProductEnrichmentCache` resolvable, which it already is, registered earlier in the same method), and confirm the new `ModuleBoundaryRule` entries' exact `Name`/prefixes match the existing rule-table conventions.
3. **Development step**:
   a. Strip the import and `.Use(...)` call from `AnthropicAdapterServiceCollectionExtensions.cs` (FR-1).
   b. Add the key constant and the new `AddKeyedSingleton<IChatClient>(...)` registration in `KnowledgeBaseModule.cs` (FR-2).
   c. Add `[FromKeyedServices(...)]` to `AskQuestionHandler`'s constructor parameter; add the `Microsoft.Extensions.DependencyInjection` using if not already present (FR-2).
   d. Add the new `ModuleBoundaryRule`(s) to `ModuleBoundariesTests.cs` (FR-3).
   e. Add the DI-wiring tests proving KB still gets enrichment and the default client no longer does (FR-4).
   f. Run `dotnet build`, `dotnet format`, and the full backend test suite — pay particular attention to any Article/Smartsupp/Photobank/Leaflet/Rag tests that might (incorrectly) assert on the old accidental product-link rewriting; if any do, that is itself evidence of the bug this task fixes, not a reason to keep the coupling.

## Open questions
1. **Where should the KB-specific decoration be registered — KnowledgeBaseModule (recommended) or the Anthropic adapter via a KB-named keyed client?** Recommending the former (FR-2 as written) because it keeps `Adapters.Anthropic` fully generic (zero `Application.*` awareness, matching the letter of the finding's suggested direction) and mirrors the fact that `KnowledgeBaseModule` already owns both `PostAnswerEnrichmentMiddleware` and `IProductEnrichmentCache`. The alternative — adding a KB-named keyed raw client in the adapter (`AddKeyedChatClient("knowledge-base", ...)`) and decorating it in `KnowledgeBaseModule` — is roughly equivalent in effort but leaves a KB-shaped key string sitting in the generic adapter file, which is a smaller but analogous smell to the one being fixed. Flagging for explicit sign-off since it's a legitimate judgment call either way.
2. **Naming**: is `EnrichedChatClientKey` / `"knowledge-base-answer"` acceptable, or does the design step prefer something else (e.g. matching a `KnowledgeBaseConstants` file that may already exist under a different name)? Needs a quick check for an existing constants file in `Features/KnowledgeBase/` before creating a new one.
3. **Config-key coupling follow-up** (noted in the finding, out of scope here per above): should a separate task be opened to rename `KnowledgeBase:ChatModel`/`EmbeddingModel`/etc. to adapter-owned config keys? Recommend yes, as a distinct follow-up item, not bundled into this fix.
4. **OpenAI adapter architecture-test rule**: since `Anela.Heblo.Adapters.OpenAI` has no `Application.*` reference today, adding its boundary rule alongside the Anthropic one (FR-3) is free regression protection — recommend including it, but flagging in case the design step considers it out of this task's named scope (the finding only cites the Anthropic adapter by name).
