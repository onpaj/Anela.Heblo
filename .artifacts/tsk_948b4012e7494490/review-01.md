# Review: Re-home KnowledgeBase's answer-enrichment middleware off the default `IChatClient`

## Verdict: done

## What I checked

Read `plan-01.md`, `design-01.md`, `development-01.md`, then independently inspected the actual diff (`git show d9fa62fd`) rather than trusting the development report, and re-ran validation myself.

### Diff vs. spec/design

- `AnthropicAdapterServiceCollectionExtensions.cs`: `using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;` removed; the default `AddChatClient(...)` chain is now just `.UseLogging()` — `PostAnswerEnrichmentMiddleware` gone. The keyed `MeetingExtractionClientKey` client is untouched. Matches FR-1 exactly.
- New `KnowledgeBaseConstants.cs` (`internal const string EnrichedChatClientKey = "knowledge-base-answer"`) mirrors the existing `MeetingTasksConstants` pattern.
- `KnowledgeBaseModule.cs`: adds `AddKeyedSingleton<IChatClient>(KnowledgeBaseConstants.EnrichedChatClientKey, ...)` decorating the plain default `IChatClient` with `PostAnswerEnrichmentMiddleware`, pulling `IProductEnrichmentCache` from DI. This re-homes the enrichment into the module that owns it (KnowledgeBase), consistent with the module's other provider-owned-DI patterns already in the same file (`ILeafletKnowledgeSource`, `IArticleStyleGuideSource` adapters). Matches FR-2.
- `AskQuestionHandler.cs`: ctor parameter now `[FromKeyedServices(KnowledgeBaseConstants.EnrichedChatClientKey)] IChatClient chatClient` — the only consumer that needs enrichment now explicitly opts in. `AskQuestionHandlerTests.cs` constructs the handler positionally (`Mock<IChatClient>` passed directly), so the attribute is inert for tests, as predicted — confirmed no test changes were needed and none were made.
- `ModuleBoundariesTests.cs`: two new rules, `Anthropic Adapter -> Application` and `OpenAI Adapter -> Application`, both `InspectedAssembly`-scoped, forbidding the `Anela.Heblo.Application` prefix, both with empty allowlists. Verified the OpenAI adapter's only `KnowledgeBase`-related references are string config-key literals (`configuration["KnowledgeBase:EmbeddingModel"]`), not a namespace import, so the empty allowlist is correct and the rule is meaningful (would catch a reintroduced `Application.*` reference in either adapter).
- `Anela.Heblo.Tests.csproj` gained a `ProjectReference` to the OpenAI adapter, needed for the new architecture rule to load that assembly — appropriately minimal.
- New `KnowledgeBaseChatClientWiringTests.cs` builds a real `IServiceCollection` via `AddAnthropicAdapter` + `AddKnowledgeBaseModule` and asserts (a) the unkeyed default `IChatClient` is not, and does not delegate to, `PostAnswerEnrichmentMiddleware`, and (b) the keyed KB client is. This is a real regression guard for the actual bug shape (a lambda-constructed decorator on a widened interface), which the prior design-review step (`0b52078d`) had already flagged as the property `ModuleBoundariesTests` alone cannot detect. Good that it wasn't dropped.

### Independent verification (not just trusting the development report)

- `dotnet build Anela.Heblo.sln`: succeeded, 0 errors (pre-existing unrelated nullable warnings only).
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests|FullyQualifiedName~KnowledgeBaseChatClientWiringTests|FullyQualifiedName~AskQuestionHandlerTests|FullyQualifiedName~PostAnswerEnrichmentMiddlewareTests"`: 53/53 passed.
- `dotnet format --verify-no-changes` scoped to the 6 touched/new source files: clean, no diagnostics.

### Non-KB consumers

Confirmed by design/inspection: Article pipeline steps, Smartsupp's `GenerateDraftReplyHandler`, `PhotobankAutoTagJob`, Leaflet's `LeafletChunkSummarizer`/`GenerateLeafletHandler`, and `RagQueryExpander` all still inject the plain unkeyed `IChatClient` — unchanged files, and now genuinely free of KB's product-link rewriting side effect, which was the entire point of the fix.

### Scope

Config-key coupling (`KnowledgeBase:ChatModel`/`EmbeddingModel`/etc. read inside adapter DI extensions) was correctly left untouched — plan-01.md explicitly scoped this out as a separate follow-up, and it's a config-string smell, not a compile-time module-boundary violation the issue asked to fix.

## Conclusion

The implementation matches the approved design precisely, follows the existing `MeetingTasksModule`/`MeetingTasksConstants` keyed-client precedent, adds both the general-purpose architecture-boundary guard (FR-3) and the specific DI-wiring regression test (FR-4) that the design-review step called out as necessary, and all verification commands I ran independently confirm the development report's claims. No functional requirement is unmet, no correctness bugs found, no missing required tests.

```json
{"outcome": "done", "summary": "Verified the diff against plan-01.md/design-01.md: adapter no longer imports or wires KnowledgeBase.Pipeline into the default IChatClient; enrichment re-homed as a KnowledgeBaseConstants.EnrichedChatClientKey keyed client inside KnowledgeBaseModule; AskQuestionHandler opts in via [FromKeyedServices]. Independently ran dotnet build (0 errors), the targeted test filter (53/53 passed), and dotnet format --verify-no-changes (clean) on the touched files. Both required tests (ModuleBoundariesTests boundary rules and the KnowledgeBaseChatClientWiringTests DI-wiring proof) are present and correct. No functional gaps or correctness issues found."}
```
