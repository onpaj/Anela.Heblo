## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs:40-41` — the new keyed-`IChatClient` registration is inserted immediately after the closing `}` of the `if (!useMockAuth && !bypassJwt) { ... } else { ... }` block with no blank line separating them, while the rest of the method uses a blank line between logically distinct registration groups (e.g. before `services.AddScoped<IMeetingSummaryExplainer, ...>()` at line 52, before the repository comment at line 56). A blank line before the new comment block would match the file's existing spacing convention. Purely cosmetic, does not affect behavior.

## Verification performed

- **FR-1** (`grep -ri "meeting" backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/`) → no matches. The adapter's `MeetingExtractionClientKey` constant and its `AddKeyedSingleton<IChatClient>` block are fully removed; the file now contains only `AnthropicOptions` binding, the named `"Anthropic"` `HttpClient` registration, and the single unkeyed `AddChatClient(...).UseLogging()` call.
- **FR-1** (full-solution grep for `MeetingExtractionClientKey`) → zero matches anywhere in `backend/`.
- **FR-2** — `MeetingTasksModule.cs` now contains `services.AddKeyedSingleton<IChatClient>(MeetingTasksConstants.ExtractionChatClientKey, (sp, _) => sp.GetRequiredService<IChatClient>());`, placed before the `IMeetingTaskExtractor` factory that consumes it via `GetRequiredKeyedService`. It resolves the default `IChatClient` via `sp.GetRequiredService<IChatClient>()` rather than reconstructing `AnthropicChatClient`, matching the `KnowledgeBaseModule` precedent.
- **FR-3** (full-solution grep for `"meeting-extractor"`) → exactly one match, in `MeetingTasksConstants.cs:5`; the constant stays `internal`, value unchanged.
- **FR-4 / composition order** — confirmed in `Program.cs`: `AddApplicationServices` (→ `AddMeetingTasksModule`) runs at line 104, `AddAnthropicAdapter` runs at line 120 — i.e. the keyed factory is registered before the adapter's default `IChatClient` registration exists. This is safe because `AddKeyedSingleton`'s factory delegate captures `IServiceProvider` and resolves lazily on first use, not at registration time — identical to the already-working `KnowledgeBaseModule` pattern using the same call order.
- **Build**: `dotnet build Anela.Heblo.sln` — succeeded, 0 errors (252 pre-existing warnings, none related to this change).
- **Format**: `dotnet format Anela.Heblo.sln --verify-no-changes --include <the two touched files>` — no changes needed, both files already conform.
- **Tests**: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ClaudeMeetingTaskExtractorTests|FullyQualifiedName~KnowledgeBaseChatClientWiringTests"` → 13/13 passed, matching the implementation summary's claim.
- Also ran the broader `~MeetingTasks` filter (155 tests): 148 passed, 7 failed. All 7 failures are in `MeetingTranscriptRepositorySearchIntegrationTests`, an unrelated Testcontainers/Postgres integration suite that fails with `System.ArgumentException: Docker is either not running or misconfigured` — confirmed no Docker daemon is available in this environment (`docker ps` fails the same way). These failures are pre-existing environment limitations, not caused by this diff, and are outside the two files this change touches.
- Diff scope confirmed minimal: only `AnthropicAdapterServiceCollectionExtensions.cs` and `MeetingTasksModule.cs` are modified under `backend/`; `MeetingTasksConstants.cs`, `ClaudeMeetingTaskExtractor`, `IMeetingTaskExtractor`, `KnowledgeBaseModule.cs`, and `Program.cs` are all untouched, matching the spec's "Out of Scope" and "Do not" lists.

## Conclusion
The implementation matches `spec.r1.md` and `task-plan.r1.md` exactly: the MeetingTasks-specific keyed `IChatClient` registration and its duplicate key constant are removed from the generic Anthropic adapter, and the equivalent keyed registration is added inside `MeetingTasksModule`, sourced from the single-source-of-truth `MeetingTasksConstants.ExtractionChatClientKey`, mirroring the `KnowledgeBaseModule` precedent. Build succeeds, formatting is clean, and all targeted tests pass. No correctness issues found.
