# Design: Re-home KnowledgeBase's answer-enrichment middleware off the default `IChatClient`

No UI surface — this is a backend DI-wiring change. UX/UI section omitted.

## Component design

### 1. `Anela.Heblo.Adapters.Anthropic` — becomes feature-agnostic

`AnthropicAdapterServiceCollectionExtensions.cs` loses its only `Application.*` reference. It exposes exactly two chat-client shapes, neither aware of any consumer:

- The **default** `IChatClient` (`.AddChatClient(...).UseLogging()`) — a plain logged Anthropic client, no content transformation.
- The **keyed** `"meeting-extractor"` `IChatClient` — unchanged, already feature-agnostic (a raw client with no decoration; `MeetingTasksModule` does its own decoration-equivalent work by wrapping it in `ClaudeMeetingTaskExtractor`).

```csharp
// AnthropicAdapterServiceCollectionExtensions.cs — after
services.AddChatClient(sp =>
    new AnthropicChatClient(...))
    .UseLogging();                       // <-- .Use(PostAnswerEnrichmentMiddleware) removed

services.AddKeyedSingleton<IChatClient>(MeetingExtractionClientKey, (sp, _) =>
    new AnthropicChatClient(...));        // unchanged
```

`using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;` (line 1) is deleted. The `KnowledgeBase:ChatModel` / `KnowledgeBase:ChatMaxTokens` config-key reads (lines 19-20) are **left as-is** — out of scope per plan-01.md, tracked as a follow-up (see Open follow-ups below).

### 2. `KnowledgeBaseModule` — becomes the sole owner of the enrichment decoration

`KnowledgeBaseModule` already owns `PostAnswerEnrichmentMiddleware`'s only two dependencies (the inner `IChatClient` it wraps, resolved from the adapter, and `IProductEnrichmentCache`, registered on line 50 of the current file). It gains one new registration, placed immediately after the existing `IProductEnrichmentCache` singleton so the ordering visually matches the dependency:

```csharp
services.AddSingleton<IProductEnrichmentCache, ProductEnrichmentCache>();

services.AddKeyedSingleton<IChatClient>(KnowledgeBaseConstants.EnrichedChatClientKey, (sp, _) =>
    new PostAnswerEnrichmentMiddleware(
        sp.GetRequiredService<IChatClient>(),
        sp.GetRequiredService<IProductEnrichmentCache>()));
```

`sp.GetRequiredService<IChatClient>()` resolves the plain (now enrichment-free) default client registered by `AddAnthropicAdapter`. Registration order between `AddAnthropicAdapter` and `AddKnowledgeBaseModule` in `Program.cs`/composition root does not matter — `AddSingleton`/`AddKeyedSingleton` factories resolve lazily, and the existing `MeetingTasksModule`/adapter pair already relies on the same lazy-resolution guarantee.

### 3. New file: `KnowledgeBaseConstants.cs`

Mirrors `MeetingTasksConstants` exactly (same visibility, same shape):

```csharp
namespace Anela.Heblo.Application.Features.KnowledgeBase;

internal static class KnowledgeBaseConstants
{
    internal const string EnrichedChatClientKey = "knowledge-base-answer";
}
```

No existing constants file was found under `Features/KnowledgeBase/` (checked — plan-01.md Open Question #2 resolved: create new).

### 4. `AskQuestionHandler` — resolves the keyed client instead of the default one

Only the constructor's attribute changes; the parameter stays positional `IChatClient chatClient` so `AskQuestionHandlerTests.CreateHandler` (which calls `new(...)` positionally) needs no changes:

```csharp
public AskQuestionHandler(
    IMediator mediator,
    [FromKeyedServices(KnowledgeBaseConstants.EnrichedChatClientKey)] IChatClient chatClient,
    IOptions<KnowledgeBaseOptions> options,
    IProductEnrichmentCache enrichmentCache,
    IRagInteractionRecorder recorder,
    ILogger<AskQuestionHandler> logger)
```

Add `using Microsoft.Extensions.DependencyInjection;` to `AskQuestionHandler.cs` (for `FromKeyedServicesAttribute`).

### 5. Architecture test — `ModuleBoundariesTests.Rules()`

Follows the existing `InspectedAssembly`-qualified rule shape used for `ShoptetApi Adapters -> Catalog`/`-> Logistics` (lines 618-640). Two new entries, inserted after the ShoptetApi adapter rules:

```csharp
new ModuleBoundaryRule(
    Name: "Anthropic Adapter -> Application",
    InspectedNamespacePrefix: "Anela.Heblo.Adapters.Anthropic",
    ForbiddenNamespacePrefixes: new[] { "Anela.Heblo.Application" },
    Allowlist: new HashSet<string>(StringComparer.Ordinal),
    InspectedAssembly: "Anela.Heblo.Adapters.Anthropic"),

new ModuleBoundaryRule(
    Name: "OpenAI Adapter -> Application",
    InspectedNamespacePrefix: "Anela.Heblo.Adapters.OpenAI",
    ForbiddenNamespacePrefixes: new[] { "Anela.Heblo.Application" },
    Allowlist: new HashSet<string>(StringComparer.Ordinal),
    InspectedAssembly: "Anela.Heblo.Adapters.OpenAI"),
```

Both use the broad `"Anela.Heblo.Application"` prefix (not scoped to `KnowledgeBase`) per plan-01.md — the intent is these adapters carry zero application-layer awareness, not just no KnowledgeBase awareness. Both allowlists start empty and are expected to stay empty; confirmed `OpenAiAdapterServiceCollectionExtensions.cs` has no `Application.*` reference today (only reads `OpenAI:ApiKey`/`KnowledgeBase:EmbeddingModel`/`KnowledgeBase:EmbeddingDimensions` as plain config strings), so its rule passes with no code change on that side.

### 6. New DI-wiring test

New file `backend/test/Anela.Heblo.Tests/KnowledgeBase/KnowledgeBaseChatClientWiringTests.cs` (no existing test builds a real container across `AddAnthropicAdapter` + `AddKnowledgeBaseModule` together, so this is a new file rather than an extension of an existing one). Two cases:

```csharp
public class KnowledgeBaseChatClientWiringTests
{
    private static IServiceProvider BuildProvider() { /* minimal IConfiguration + AddAnthropicAdapter + AddKnowledgeBaseModule */ }

    [Fact]
    public void Default_chat_client_has_no_enrichment_middleware()
    {
        var chatClient = provider.GetRequiredService<IChatClient>();
        chatClient.Should().NotBeOfType<PostAnswerEnrichmentMiddleware>();
        // also assert no DelegatingChatClient in the chain is a PostAnswerEnrichmentMiddleware,
        // walking .GetService<PostAnswerEnrichmentMiddleware>() via IChatClient.GetService(typeof(...))
    }

    [Fact]
    public void KnowledgeBase_keyed_chat_client_wraps_enrichment_middleware()
    {
        var chatClient = provider.GetRequiredKeyedService<IChatClient>(KnowledgeBaseConstants.EnrichedChatClientKey);
        chatClient.Should().BeOfType<PostAnswerEnrichmentMiddleware>();
    }
}
```

`Microsoft.Extensions.AI`'s `IChatClient` exposes `GetService(Type, object?)` (used by `DelegatingChatClient` chains) — the "no enrichment" assertion uses that to confirm `PostAnswerEnrichmentMiddleware` is absent from the default client's chain, not just that the outermost type differs, since `.UseLogging()` also wraps the client in a delegating type.

## Data schemas

No DB, request/response, or event-payload changes. `PostAnswerEnrichmentMiddleware`, `IProductEnrichmentCache`, `ProductEnrichmentEntry`, `AskQuestionRequest`/`AskQuestionResponse` are all unchanged in shape — this is pure DI re-registration.

## File-level change list

| File | Change |
|---|---|
| `backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicAdapterServiceCollectionExtensions.cs` | remove `using Application.Features.KnowledgeBase.Pipeline`, remove `.Use(PostAnswerEnrichmentMiddleware)` |
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` | add `AddKeyedSingleton<IChatClient>(KnowledgeBaseConstants.EnrichedChatClientKey, ...)` |
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseConstants.cs` | new file, `EnrichedChatClientKey = "knowledge-base-answer"` |
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/AskQuestion/AskQuestionHandler.cs` | add `[FromKeyedServices(...)]` to the `chatClient` ctor param + DI using |
| `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` | two new `ModuleBoundaryRule` entries (Anthropic, OpenAI, both `-> Application`) |
| `backend/test/Anela.Heblo.Tests/KnowledgeBase/KnowledgeBaseChatClientWiringTests.cs` | new file, two DI-resolution tests |

No changes to `AskQuestionHandlerTests.cs`, `PostAnswerEnrichmentMiddlewareTests.cs`, `MeetingTasksModule.cs`, or any of the six non-KB consumer files — they keep injecting the plain unkeyed `IChatClient`, which now genuinely has no side effects.

## Open follow-ups (not part of this change)
- `KnowledgeBase:ChatModel` / `KnowledgeBase:EmbeddingModel` / `KnowledgeBase:ChatMaxTokens` / `KnowledgeBase:EmbeddingDimensions` config keys read inside the Anthropic/OpenAI adapter DI extensions are a naming/config-ownership smell, not a compile-time boundary violation — recommend a separate follow-up task to rename these to adapter-owned keys (e.g. `Anthropic:DefaultChatModel`) and update `appsettings.json`/`appsettings.Production.json`.
