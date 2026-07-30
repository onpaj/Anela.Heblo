# Development: Re-home KnowledgeBase's answer-enrichment middleware off the default `IChatClient`

Implemented exactly as specified in `plan-01.md` / `design-01.md` (Option A: KnowledgeBase-owned keyed decoration).

## Files changed

- **`backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicAdapterServiceCollectionExtensions.cs`**
  Removed `using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;` and the `.Use((inner, sp) => new PostAnswerEnrichmentMiddleware(...))` call from the default `AddChatClient(...)` registration. The default client is now just `.UseLogging()`. The keyed `"meeting-extractor"` client is untouched. The adapter now has zero references to any `Anela.Heblo.Application.*` namespace.

- **`backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`**
  Added a new `AddKeyedSingleton<IChatClient>(KnowledgeBaseConstants.EnrichedChatClientKey, ...)` registration (placed right after the existing `IProductEnrichmentCache` singleton), decorating the plain default `IChatClient` (now enrichment-free) with `PostAnswerEnrichmentMiddleware`. Added `using Microsoft.Extensions.AI;`.

- **`backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseConstants.cs`** (new)
  `internal static class KnowledgeBaseConstants { internal const string EnrichedChatClientKey = "knowledge-base-answer"; }` — mirrors `MeetingTasksConstants` exactly.

- **`backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/AskQuestion/AskQuestionHandler.cs`**
  Constructor's `chatClient` parameter now carries `[FromKeyedServices(KnowledgeBaseConstants.EnrichedChatClientKey)]`. Added `using Microsoft.Extensions.DependencyInjection;`. No other change — the parameter stays a plain positional `IChatClient` for direct-construction tests.

- **`backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`**
  Added two new `ModuleBoundaryRule` entries (after the ShoptetApi adapter rules): `"Anthropic Adapter -> Application"` and `"OpenAI Adapter -> Application"`, both `InspectedNamespacePrefix`/`InspectedAssembly` scoped to their respective adapter assembly, forbidding the broad `"Anela.Heblo.Application"` prefix, both with empty allowlists (confirmed both adapters have zero such references after the fix).

- **`backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`**
  Added a `ProjectReference` to `Anela.Heblo.Adapters.OpenAI` (needed so the new architecture-test rule can `Assembly.Load("Anela.Heblo.Adapters.OpenAI")`; it wasn't referenced by the test project before).

- **`backend/test/Anela.Heblo.Tests/KnowledgeBase/KnowledgeBaseChatClientWiringTests.cs`** (new)
  Builds a real `IServiceCollection` via `AddAnthropicAdapter` + `AddKnowledgeBaseModule` (empty `IConfiguration`, `services.AddLogging()`) and asserts:
  - the default unkeyed `IChatClient` is not a `PostAnswerEnrichmentMiddleware`, and `GetService(typeof(PostAnswerEnrichmentMiddleware))` on it returns `null` (proves the middleware isn't hiding deeper in the `.UseLogging()` delegating chain either);
  - the keyed `IChatClient` resolved via `KnowledgeBaseConstants.EnrichedChatClientKey` **is** a `PostAnswerEnrichmentMiddleware`.

No changes were needed to `AskQuestionHandlerTests.cs`, `PostAnswerEnrichmentMiddlewareTests.cs`, `MeetingTasksModule.cs`, or any of the six non-KB consumer files (Article, Smartsupp, Photobank, Leaflet, `RagQueryExpander`) — they keep injecting the now-genuinely-plain unkeyed `IChatClient`.

Config-key coupling (`KnowledgeBase:ChatModel`/`EmbeddingModel`/etc. read inside the adapter DI extensions) was left untouched, per plan-01.md's explicit scoping — tracked as a follow-up, not a compile-time boundary violation.

## How to verify

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet build Anela.Heblo.sln                                   # 0 errors
dotnet format Anela.Heblo.sln --verify-no-changes --no-restore --include \
  backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicAdapterServiceCollectionExtensions.cs \
  backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs \
  backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseConstants.cs \
  backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/AskQuestion/AskQuestionHandler.cs \
  backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs \
  backend/test/Anela.Heblo.Tests/KnowledgeBase/KnowledgeBaseChatClientWiringTests.cs
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ModuleBoundariesTests|FullyQualifiedName~KnowledgeBaseChatClientWiringTests|FullyQualifiedName~AskQuestionHandlerTests|FullyQualifiedName~PostAnswerEnrichmentMiddlewareTests"
```

## Validation performed this session

- `dotnet build` on the full solution: **0 errors** (pre-existing unrelated nullable warnings only).
- Targeted test run (`ModuleBoundariesTests`, `KnowledgeBaseChatClientWiringTests`, `AskQuestionHandlerTests`, `PostAnswerEnrichmentMiddlewareTests`): **53/53 passed**, including both new architecture-boundary rules and both new DI-wiring assertions.
- `dotnet format --verify-no-changes` scoped to exactly the 6 touched/new files: clean, no diagnostics.
- Broader regression sweep across `Article`, `Smartsupp`, `Photobank`, `Leaflet`, `RagQueryExpander`, and `MeetingTasks` test namespaces, plus a full-suite run: **no new failures**. The only failures observed (`MeetingTranscriptRepositorySearchIntegrationTests`, 3 tests, `column "Participants" of relation "MeetingTranscripts" does not exist`) are pre-existing Postgres-Testcontainer schema-drift failures in code this change never touches (persistence/migrations) — confirmed unrelated by inspecting `MeetingTranscript.cs` (the `Participants` property already exists in the domain model, so this is a stale test-fixture/migration mismatch, not a regression from this fix).
- This machine was under very heavy concurrent load from other agent sessions during this run (load average 25–40+), which caused several `dotnet test`/`dotnet format` invocations against the whole 50+-project solution to stall; those were re-scoped to smaller targeted invocations (which completed normally) rather than waited out indefinitely.

## Outcome

Fix implemented and verified against both requirements: the Anthropic/OpenAI adapters are now feature-agnostic (zero `Application.*` references, pinned by architecture tests), and KnowledgeBase's product-link enrichment survives unchanged for `AskQuestionHandler` via its own keyed client — while Article, Smartsupp, Photobank, Leaflet, and `RagQueryExpander` no longer silently inherit KB's product-code rewriting.

```json
{"outcome": "done", "summary": "Removed PostAnswerEnrichmentMiddleware and the KnowledgeBase.Pipeline import from the Anthropic adapter's default IChatClient; re-homed the enrichment as a new KnowledgeBaseConstants.EnrichedChatClientKey keyed client inside KnowledgeBaseModule, consumed by AskQuestionHandler via [FromKeyedServices]. Added two ModuleBoundariesTests rules (Anthropic/OpenAI adapters -> Application, both empty allowlists) and a new KnowledgeBaseChatClientWiringTests.cs proving the default client lost enrichment while the KB keyed client kept it. Build clean (0 errors), scoped dotnet format clean, and all directly-relevant tests (53) plus a broader Article/Smartsupp/Photobank/Leaflet/Rag/MeetingTasks regression sweep pass — only pre-existing, unrelated MeetingTasks Postgres schema-drift integration failures observed."}
```
