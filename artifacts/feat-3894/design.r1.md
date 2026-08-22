# Design: Move MeetingTasks-specific keyed `IChatClient` out of the Anthropic adapter

## Component Design

### `Anela.Heblo.Adapters.Anthropic.AnthropicAdapterServiceCollectionExtensions`
**Responsibility (after change):** Register only the generic, feature-agnostic Anthropic chat client. No knowledge of any downstream feature module.

**Removed:**
- `public const string MeetingExtractionClientKey = "meeting-extractor";`
- The `services.AddKeyedSingleton<IChatClient>(MeetingExtractionClientKey, ...)` block that reconstructed an `AnthropicChatClient` from `IOptions<AnthropicOptions>` / `IHttpClientFactory` / `ILogger<AnthropicChatClient>`.

**Retained/unchanged:** `AnthropicOptions` binding, the named `HttpClient` registration, and the single unkeyed `services.AddChatClient(...).UseLogging()` registration, resolvable by any consumer via `IServiceProvider.GetRequiredService<IChatClient>()`.

A full-solution grep for `meeting` (case-insensitive) under this project must return no matches after the change.

### `Anela.Heblo.Application.Features.MeetingTasks.MeetingTasksModule`
**Responsibility (added):** Own the MeetingTasks-specific keyed `IChatClient` registration, following the precedent already established by `KnowledgeBaseModule` (`KnowledgeBaseModule.cs:58-64`) — the feature module that consumes a keyed client is the one that registers it, built from the default `IChatClient` already present in the container.

**Added to `AddMeetingTasksModule`**, placed immediately above the existing `services.AddScoped<IMeetingTaskExtractor>(...)` factory it supports:

```csharp
// MeetingTasks-scoped alias for the default IChatClient, keyed for extraction use.
// Kept in this module (not the generic Anthropic adapter) so the adapter has no
// compile-time knowledge of MeetingTasks. Mirrors KnowledgeBaseModule's keyed-client pattern.
services.AddKeyedSingleton<IChatClient>(MeetingTasksConstants.ExtractionChatClientKey,
    (sp, _) => sp.GetRequiredService<IChatClient>());
```

Unlike `KnowledgeBaseModule`'s keyed registration — which wraps `sp.GetRequiredService<IChatClient>()` in a `PostAnswerEnrichmentMiddleware` decorator — the MeetingTasks keyed client is a pure alias with no additional decoration, because the original registration applied none. The keying exists so any future MeetingTasks-specific behavior (different model, transcript-specific system prompt, retry policy) can be added later by editing only this factory delegate, without the adapter ever needing to know about MeetingTasks again.

**Unchanged:** the consumer call site `sp.GetRequiredKeyedService<IChatClient>(MeetingTasksConstants.ExtractionChatClientKey)` inside the `IMeetingTaskExtractor` factory, and `ClaudeMeetingTaskExtractor` itself.

Registration order relative to `AddAnthropicAdapter` in the composition root does not matter: `AddKeyedSingleton`'s factory delegate resolves `IServiceProvider` lazily on first use, not at registration time, so it is immaterial whether `AddApplicationServices` (which calls `AddMeetingTasksModule`) runs before or after `AddAnthropicAdapter`, as already proven by the identical `KnowledgeBaseModule` pattern in production today.

### `Anela.Heblo.Application.Features.MeetingTasks.MeetingTasksConstants`
**Responsibility:** Sole owner of the key literal `"meeting-extractor"`.

No code change required — this type was already correctly scoped (`internal const string ExtractionChatClientKey = "meeting-extractor";`, single feature namespace). It simply stops having a rival, independently-maintained copy of the same string in the Anthropic adapter. After the refactor, a full-solution grep for `"meeting-extractor"` must return exactly one match, in `MeetingTasksConstants.cs`. Visibility stays `internal`: both the registration (`MeetingTasksModule.cs`) and the consumption (`MeetingTasksModule.cs`'s extractor factory) live in the same assembly and namespace, so there is no reason to widen it.

## Data Schemas

Not applicable. This is a pure dependency-injection wiring change — no database schema, API request/response shape, or event payload is introduced, removed, or modified. The only artifact relocated is an internal DI key constant (a compile-time string), not a data schema.
