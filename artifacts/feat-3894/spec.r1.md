# Specification: Move MeetingTasks-specific keyed `IChatClient` out of the Anthropic adapter

## Summary
`Anela.Heblo.Adapters.Anthropic`'s `AnthropicAdapterServiceCollectionExtensions` currently registers a keyed `IChatClient` (`meeting-extractor`) that exists solely to serve `MeetingTasksModule`, duplicating the key string as a second, independently-maintained constant. This refactor relocates that keyed registration into `MeetingTasksModule` (Application layer) and removes the duplicate key definition, following the provider-owns-the-key pattern already established by `KnowledgeBaseModule`. No behavior, API surface, or DI resolution outcome changes for any consumer.

## Background
GitHub architecture-review finding (brief at `artifacts/feat-3894/brief.md`) flags that the generic Anthropic adapter has compile-time knowledge of a specific downstream feature (MeetingTasks), which is exactly the coupling that a prior finding (#3770) removed for KnowledgeBase. `KnowledgeBaseModule.cs:58-64` demonstrates the intended pattern: the feature module owns its keyed `IChatClient` registration (there, a decorator around the default client) and owns the single key constant (`KnowledgeBaseConstants.EnrichedChatClientKey`), consumed in exactly one place. MeetingTasks currently violates this in two ways: (1) the keyed registration lives in the adapter instead of the feature module, and (2) the key string `"meeting-extractor"` is duplicated as two unlinked literals (`AnthropicAdapterServiceCollectionExtensions.MeetingExtractionClientKey` and `MeetingTasksConstants.ExtractionChatClientKey`), so an independent edit to either one silently breaks DI resolution at runtime (`GetRequiredKeyedService` throws `InvalidOperationException` on first meeting-transcript processing) with no compiler or test signal today.

This is a pure internal refactor with no new user-facing behavior and no new API surface. It is scoped to two files plus their registration order, and classified low-risk because it relocates and deduplicates existing, working code rather than changing what it does.

## Functional Requirements

### FR-1: Remove the MeetingTasks-specific keyed registration and key constant from the Anthropic adapter
`AnthropicAdapterServiceCollectionExtensions.cs` must no longer contain any reference to MeetingTasks. Specifically:
- Delete the `public const string MeetingExtractionClientKey = "meeting-extractor";` field.
- Delete the `services.AddKeyedSingleton<IChatClient>(MeetingExtractionClientKey, ...)` registration block (lines 36-40 in the current file).
- The adapter retains only the generic, unkeyed `services.AddChatClient(...)` registration (and its `UseLogging()` decoration), which remains available for any feature to resolve via `IServiceProvider.GetRequiredService<IChatClient>()`.

**Acceptance criteria:**
- `grep -ri "meeting" backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/` returns no matches.
- `AnthropicAdapterServiceCollectionExtensions.cs` compiles with no remaining reference to `MeetingExtractionClientKey` anywhere in the solution (a full-solution grep for `MeetingExtractionClientKey` returns zero results after the change).
- The adapter's `.csproj` project reference to `Anela.Heblo.Application` is unaffected (out of scope to remove/add project references as part of this change, unless it becomes unused — see FR-4 note).

### FR-2: Register the MeetingTasks-specific keyed `IChatClient` inside `MeetingTasksModule`
`MeetingTasksModule.AddMeetingTasksModule` must add a keyed singleton registration for `IChatClient` under the existing key value `"meeting-extractor"`, sourced from `MeetingTasksConstants.ExtractionChatClientKey`, following the `KnowledgeBaseModule` precedent of resolving the default (unkeyed) `IChatClient` from the container rather than re-instantiating `AnthropicChatClient` directly.

Because the MeetingTasks keyed client's original construction was byte-for-byte identical to the adapter's default `AddChatClient` registration (same `AnthropicOptions`, same named `HttpClient`, same logger type) — i.e., it applied no MeetingTasks-specific decoration — the moved registration should resolve and re-expose the default `IChatClient` under the MeetingTasks key, rather than reconstructing an `AnthropicChatClient` from its lower-level dependencies (`IOptions<AnthropicOptions>`, `IHttpClientFactory`, `ILogger<AnthropicChatClient>`). This mirrors `KnowledgeBaseModule`'s pattern of building its keyed client from `sp.GetRequiredService<IChatClient>()`, and avoids the module needing to know about Anthropic-specific construction details it doesn't otherwise depend on. See Open Questions for the case where this assumption is rejected.

Example shape (illustrative, not prescriptive of exact code style):
```csharp
// MeetingTasks-scoped alias for the default IChatClient, keyed for extraction use.
// Kept in this module (not the generic Anthropic adapter) so the adapter has no
// compile-time knowledge of MeetingTasks. Mirrors KnowledgeBaseModule's pattern.
services.AddKeyedSingleton<IChatClient>(MeetingTasksConstants.ExtractionChatClientKey,
    (sp, _) => sp.GetRequiredService<IChatClient>());
```

**Acceptance criteria:**
- `MeetingTasksModule.cs` contains a `services.AddKeyedSingleton<IChatClient>(MeetingTasksConstants.ExtractionChatClientKey, ...)` (or equivalent `AddKeyedSingleton` overload) registration.
- The registration resolves its underlying `IChatClient` via `sp.GetRequiredService<IChatClient>()` (the default, unkeyed registration added by `AddAnthropicAdapter`), not by re-instantiating `AnthropicChatClient` from `IOptions<AnthropicOptions>` / `IHttpClientFactory` / `ILogger<AnthropicChatClient>`.
- The existing consumer line `sp.GetRequiredKeyedService<IChatClient>(MeetingTasksConstants.ExtractionChatClientKey)` inside `AddMeetingTasksModule` (currently line 43) is unchanged — it continues to resolve the keyed client, now satisfied by the registration added in this same module.
- Registration order within `AddMeetingTasksModule` must place the new `AddKeyedSingleton<IChatClient>` call before (or in any order not depending on) the `services.AddScoped<IMeetingTaskExtractor>(...)` factory, consistent with standard `IServiceCollection` registration ordering (order does not affect resolution correctness for `Add*` calls, but should read top-to-bottom logically alongside the extractor registration it supports).

### FR-3: Single source of truth for the key string
The literal `"meeting-extractor"` must exist in exactly one place in the codebase: `MeetingTasksConstants.ExtractionChatClientKey`. No other file may declare its own copy of this string or an equivalent constant.

**Acceptance criteria:**
- A full-solution grep for `"meeting-extractor"` returns exactly one match, in `MeetingTasksConstants.cs`.
- `MeetingTasksConstants.ExtractionChatClientKey` remains `internal` (no visibility change required, since both the registration and the consumption now live within the same `Anela.Heblo.Application.Features.MeetingTasks` namespace/assembly).

### FR-4: No change to DI resolution behavior or dependent code
Composition root wiring (`AddAnthropicAdapter` then `AddMeetingTasksModule`, or whatever the current startup call order is) must continue to result in `MeetingTasksModule`'s keyed `IChatClient` resolving successfully at runtime, identical in effect to before the refactor.

**Acceptance criteria:**
- The application starts successfully (DI container builds without `InvalidOperationException`) with both `AddAnthropicAdapter` and `AddMeetingTasksModule` registered, in whatever order the composition root already calls them (verify the existing call order in the startup/composition code is compatible — `AddAnthropicAdapter` must run before or independently of `AddMeetingTasksModule`'s keyed registration attempts to resolve `IChatClient`, since `AddKeyedSingleton`'s factory delegate resolves lazily at first use, not at registration time, so exact `Add*` call order between the two modules is not itself a correctness requirement — confirm this in the actual composition root file during implementation).
- `ClaudeMeetingTaskExtractor` (the sole consumer of the keyed client) continues to receive a functioning `IChatClient` instance with no change to its own code.
- All existing backend tests pass unchanged (`dotnet build` + `dotnet test`), including any test that exercises meeting-transcript extraction or DI container validation.
- No new `MeetingExtractionClientKey`-named symbol exists anywhere post-refactor (superseded entirely by `MeetingTasksConstants.ExtractionChatClientKey`).

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected. The refactor changes only *where* an existing DI registration is declared, not its runtime cost — resolving `IChatClient` via `sp.GetRequiredService<IChatClient>()` inside the keyed factory is O(1) singleton lookup, equivalent to the previous direct `AnthropicChatClient` construction (both are singleton-cached after first resolution).

### NFR-2: Security
No security impact. No new secrets, credentials, or external calls are introduced. `AnthropicOptions` (API key, model, timeouts) continues to be bound and consumed exactly as before, solely within the adapter's own registration.

## Data Model
Not applicable — this is a DI wiring change with no data entities, persistence, or schema involved.

## API / Interface Design
Not applicable — no HTTP endpoints, MediatR requests/responses, or UI surfaces are added, removed, or changed. The only "interface" affected is internal C# DI registration:

- **Removed** from `Anela.Heblo.Adapters.Anthropic`: `public const string MeetingExtractionClientKey`; the `AddKeyedSingleton<IChatClient>("meeting-extractor", ...)` call.
- **Added** to `Anela.Heblo.Application.Features.MeetingTasks.MeetingTasksModule`: `AddKeyedSingleton<IChatClient>(MeetingTasksConstants.ExtractionChatClientKey, ...)` sourced from the default `IChatClient`.
- **Unchanged**: `MeetingTasksConstants.ExtractionChatClientKey` (value and location); the consumer call site `sp.GetRequiredKeyedService<IChatClient>(MeetingTasksConstants.ExtractionChatClientKey)` in `AddMeetingTasksModule`'s `IMeetingTaskExtractor` factory; `KnowledgeBaseModule`'s own keyed registration and constant (untouched by this change, referenced only as the precedent pattern).

## Dependencies
- `Anela.Heblo.Adapters.Anthropic.csproj` already references `Anela.Heblo.Application.csproj` (confirmed at line 19 per the brief) — this refactor requires no new project references. The existing reference direction (Adapter → Application) is unaffected since the adapter no longer needs to know about MeetingTasks at all after this change; the dependency this refactor actually needs is `MeetingTasksModule` (Application layer) depending on `Microsoft.Extensions.AI`'s `IChatClient` abstraction, which `MeetingTasksModule.cs` already imports (`using Microsoft.Extensions.AI;`, confirmed present in the current file).
- No external services, libraries, or feature flags are newly introduced.
- Depends on `AddAnthropicAdapter` having already registered the default (unkeyed) `IChatClient` in the same `IServiceCollection` before `MeetingTasksModule`'s keyed factory is first invoked (lazy resolution, not registration-time — see FR-4).

## Out of Scope
- Any change to `KnowledgeBaseModule.cs` or its existing keyed `IChatClient` pattern — it is the reference precedent only, not a target of modification.
- Any change to `ClaudeMeetingTaskExtractor`, `IMeetingTaskExtractor`, or any other MeetingTasks consumer code beyond what's needed to keep the existing `GetRequiredKeyedService` call working.
- Adding new tests specifically for DI-container validation (e.g., a "build the full service provider and resolve all keyed services" smoke test) — the brief notes this gap exists today, but closing it is a separate, broader testing concern not requested by this refactor. (Flagged for awareness; not an acceptance criterion here.)
- Any change to the `"meeting-extractor"` key's literal string value.
- Any change to `AnthropicOptions`, `AnthropicChatClient`, or other Anthropic adapter internals unrelated to the MeetingTasks keyed registration.
- Any change to composition-root call ordering of `AddAnthropicAdapter` vs. `AddMeetingTasksModule`, unless implementation discovers an actual ordering requirement (see FR-4 acceptance criteria, which asks the implementer to confirm current order is compatible, not to change it absent a discovered problem).

## Open Questions
1. FR-2 assumes the moved MeetingTasks keyed registration should resolve the default `IChatClient` via `sp.GetRequiredService<IChatClient>()` (decorator/alias style, matching `KnowledgeBaseModule`) rather than reconstructing `AnthropicChatClient` from `IOptions<AnthropicOptions>`, `IHttpClientFactory`, and `ILogger<AnthropicChatClient>` (the adapter's original approach). Since the original registration was byte-for-byte identical to the default client with no decoration applied, the alias approach is functionally equivalent and simpler, and is the assumption this spec proceeds under. If the team prefers to preserve the exact original construction style (e.g., to keep MeetingTasks decoupled from relying on the adapter's default registration being present, or to leave room for future MeetingTasks-specific decoration without touching the alias line), the implementer should instead have `MeetingTasksModule` construct `AnthropicChatClient` directly, matching `MeetingTasksModule.cs`'s existing `using Microsoft.Extensions.AI;` and adding the necessary `AnthropicOptions`/adapter-type references — note this would require `MeetingTasksModule` to reference `Anela.Heblo.Adapters.Anthropic` types directly, which is a heavier coupling than the alias approach and was not indicated as necessary by the brief. Proceeding with the lighter alias approach unless told otherwise.

## Status: HAS_QUESTIONS
