## Problem

`backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicAdapterServiceCollectionExtensions.cs:11,36-40` registers a keyed `IChatClient` purely to serve `MeetingTasksModule`, and defines its own copy of the key string:

```csharp
public const string MeetingExtractionClientKey = "meeting-extractor";
...
services.AddKeyedSingleton<IChatClient>(MeetingExtractionClientKey, (sp, _) =>
    new AnthropicChatClient(
        sp.GetRequiredService<IOptions<AnthropicOptions>>(),
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<ILogger<AnthropicChatClient>>()));
```

`backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs:41-45` consumes it through a *second, independently-defined* constant:

```csharp
// MeetingTasksConstants.cs:5
internal const string ExtractionChatClientKey = "meeting-extractor";
...
sp.GetRequiredKeyedService<IChatClient>(MeetingTasksConstants.ExtractionChatClientKey)
```

## Rule violated

The codebase's own established convention for this exact scenario — a feature needing a differently-keyed `IChatClient` — is documented in-line in `KnowledgeBaseModule.cs:58-61`: *"KB-scoped decoration of the default `IChatClient`... Kept out of the generic Anthropic adapter so unrelated features resolving the default (unkeyed) `IChatClient` don't inherit KB's... rewriting."* KnowledgeBase's keyed client is registered inside `KnowledgeBaseModule.cs` (Application layer, owned by the feature), and its key constant (`KnowledgeBaseConstants.EnrichedChatClientKey`) is defined and consumed in exactly one place. This same pattern was the direct fix for a prior finding, #3770, which removed KB-specific logic from this very adapter file.

MeetingTasks does the opposite of the KB precedent: the keyed registration lives in the generic adapter, and the key string is duplicated rather than shared, even though `Anela.Heblo.Adapters.Anthropic.csproj:19` already references `Anela.Heblo.Application.csproj` — nothing in the dependency graph forces the duplication.

## Why it matters (concrete)

1. The generic, reusable Anthropic adapter now has compile-time knowledge of a specific downstream feature (MeetingTasks) — the exact coupling #3770 removed for KnowledgeBase.
2. The key `"meeting-extractor"` exists as two separate literals with no shared reference. If either is edited independently (a rename, a refactor pass) without updating the other, `MeetingTasksModule`'s `GetRequiredKeyedService<IChatClient>` throws `InvalidOperationException` (no registered service for that key) the first time a meeting transcript is processed — not caught by the compiler, and not exercised by any DI-container-validation test found under `backend/test/`.

Also note the keyed client's construction is byte-for-byte identical to the default `AddChatClient` registration two lines above it (same options, same named `HttpClient`, same logger type) — it exists solely to give MeetingTasks a private key, which is exactly what the KB precedent shows should be built by decorating `sp.GetRequiredService<IChatClient>()` inside `MeetingTasksModule`, not by re-registering the adapter under a second name.

## Suggested direction

Move the keyed registration (or a decorator around the default `IChatClient`) into `MeetingTasksModule.AddMeetingTasksModule`, following the provider-owns-the-key pattern already used by `KnowledgeBaseModule`, and delete `MeetingExtractionClientKey` from the adapter.
