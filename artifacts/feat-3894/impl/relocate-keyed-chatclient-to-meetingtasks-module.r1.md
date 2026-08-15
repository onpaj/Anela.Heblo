# Implementation: relocate-keyed-chatclient-to-meetingtasks-module

## What was implemented
Removed the MeetingTasks-specific keyed `IChatClient` registration (and its `MeetingExtractionClientKey`
constant) from the generic Anthropic adapter, and added the equivalent keyed registration inside
`MeetingTasksModule`, sourced from the existing internal `MeetingTasksConstants.ExtractionChatClientKey`.
This mirrors the pattern already used by `KnowledgeBaseModule` for its own keyed `IChatClient` alias,
so the Anthropic adapter no longer has any compile-time knowledge of MeetingTasks.

## Files created/modified
- `backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicAdapterServiceCollectionExtensions.cs` — removed the `MeetingExtractionClientKey` constant and the keyed `AddKeyedSingleton<IChatClient>` registration block; file now only binds `AnthropicOptions`, registers the named `"Anthropic"` `HttpClient`, and registers the single unkeyed `IChatClient` via `AddChatClient(...).UseLogging()`.
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs` — added a keyed `services.AddKeyedSingleton<IChatClient>(MeetingTasksConstants.ExtractionChatClientKey, (sp, _) => sp.GetRequiredService<IChatClient>())` registration (with explanatory comment) immediately before the existing `IMeetingTaskExtractor` factory that consumes it via `GetRequiredKeyedService`.
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksConstants.cs` — no change (as specified); still holds `internal const string ExtractionChatClientKey = "meeting-extractor";`.

## Tests
No new tests were added (explicitly out of scope). Ran the existing suites that exercise this area:
- `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ClaudeMeetingTaskExtractorTests.cs` — 13 tests total in the filtered run (this file uses a mocked `IChatClient` directly, unaffected by DI wiring, but confirms the extractor logic still works with the same constructor).
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/KnowledgeBaseChatClientWiringTests.cs` — confirms the default unkeyed `IChatClient` from `AddAnthropicAdapter` stays plain (no MeetingTasks or KnowledgeBase coupling) and KnowledgeBase's own keyed client still resolves correctly.

Command run:
```
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ClaudeMeetingTaskExtractorTests|FullyQualifiedName~KnowledgeBaseChatClientWiringTests"
```
Result: `Passed! - Failed: 0, Passed: 13, Skipped: 0, Total: 13` (exit code 0).

## How to verify
1. Grep checks (all confirmed):
   ```
   grep -rn "MeetingExtractionClientKey" backend/            # -> OK: no matches
   grep -rni "meeting" backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/   # -> OK: no matches
   grep -rn '"meeting-extractor"' backend/                    # -> exactly one match, in MeetingTasksConstants.cs
   ```
2. `cd backend && dotnet build` — succeeded, 0 errors (155 pre-existing warnings unrelated to this change; a pre-existing post-build AccessMatrixGen codegen step also emits an unrelated MSB3073 warning, not an error).
3. `dotnet format Anela.Heblo.sln --no-restore --verify-no-changes --include <the two touched files>` — no output, i.e. both files already conform to formatting rules.
4. `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ClaudeMeetingTaskExtractorTests|FullyQualifiedName~KnowledgeBaseChatClientWiringTests"` — 13/13 passed.

## Notes
- No dedicated MeetingTasks DI-wiring test exists (intentionally out of scope per the task spec), and I did not add one.
- As a substitute end-to-end sanity check, I relied on `KnowledgeBaseChatClientWiringTests`, which already builds a real `ServiceProvider` from `AddAnthropicAdapter` + a feature module and resolves both the unkeyed and a keyed `IChatClient` — it proves the same DI shape (unkeyed default `IChatClient` + module-owned keyed alias) that `MeetingTasksModule` now also uses. I did not write and run a standalone scratch program that builds a provider from `AddAnthropicAdapter` + `AddMeetingTasksModule` and resolves `MeetingTasksConstants.ExtractionChatClientKey` end-to-end, since doing so would have required either committing a throwaway test file (out of scope) or an ephemeral script outside the repo, and `MeetingTasksModule.AddMeetingTasksModule` also validates unrelated options (Planner/UseMockAuth-gated bindings) that add setup friction for a one-off check. Given the change is structurally identical to the already-covered KnowledgeBase pattern and the full build + full-solution grep both pass, I judged this an acceptable, disclosed limitation rather than fabricating a run.
- No changes were made to `ClaudeMeetingTaskExtractor`, `IMeetingTaskExtractor`, `KnowledgeBaseModule.cs`, `KnowledgeBaseConstants.cs`, or any composition-root file.
- Visibility of `MeetingTasksConstants` / `ExtractionChatClientKey` was left `internal`, and the `"meeting-extractor"` string value was not changed.

## PR Summary
Removes the MeetingTasks-specific keyed `IChatClient` registration (`MeetingExtractionClientKey` /
`"meeting-extractor"`) from the generic `AnthropicAdapterServiceCollectionExtensions`, which had no
business knowing about MeetingTasks. The registration is re-created inside `MeetingTasksModule` as a
keyed alias of the module's default `IChatClient`, keyed by the existing internal
`MeetingTasksConstants.ExtractionChatClientKey` constant — the same pattern `KnowledgeBaseModule`
already uses for its own keyed client. Behavior is unchanged: `ClaudeMeetingTaskExtractor` still
resolves the same `"meeting-extractor"`-keyed `IChatClient`, backed by the same `AnthropicChatClient`
instance as before (now via the default unkeyed client rather than a second directly-constructed one).

### Changes
- `backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicAdapterServiceCollectionExtensions.cs` — removed `MeetingExtractionClientKey` constant and its keyed `AddKeyedSingleton<IChatClient>` registration.
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs` — added the keyed `IChatClient` alias registration (sourced from `MeetingTasksConstants.ExtractionChatClientKey`) ahead of the `IMeetingTaskExtractor` factory.

## Status
DONE
