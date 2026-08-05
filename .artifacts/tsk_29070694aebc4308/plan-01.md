# Plan — BackgroundRefreshTaskRegistry double-registers tasks, discarding explicit RefreshTaskConfiguration

## Summary

`BackgroundRefreshTaskRegistry.InitializeTasksFromSetup` (`backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/BackgroundRefreshTaskRegistry.cs:36-43`) calls `RegisterTask` a second, unconditional time after already registering a task with its explicit `RefreshTaskConfiguration`. Because both calls write into the same `ConcurrentDictionary` key via `AddOrUpdate` with an always-overwrite update delegate, the second (app-settings-derived) call always wins — silently discarding any explicitly supplied configuration, and throwing `InvalidOperationException` during singleton construction (i.e. app startup) if no matching `BackgroundRefresh:{Owner}:{Method}` app-settings section exists. This is a one-line missing-`else` fix plus a regression test.

## Context

The explicit-configuration overloads (`RegisterRefreshTask(..., RefreshTaskConfiguration configuration)` in `BackgroundRefreshExtensions.cs:38` and its generic sibling at `:84`) exist precisely so callers can register a task without needing an app-settings section. Today no production caller uses them (`CatalogModule`, `LogisticsModule`, `FinancialOverviewModule` all use the no-config overload), so the bug is latent — but it's a crash trap for the next caller, and the explicit-config API is currently non-functional. This is flagged by architecture review (module-map part #37, Background Execution) and the file is owned by that part, so the fix must stay confined to it.

## Functional requirements

**FR-1 — Explicit configuration is honoured, not overwritten.**
When `TaskRegistrationInfo.Configuration` is non-null, `InitializeTasksFromSetup` must register the task using that configuration only, and must NOT also perform the app-settings-derived registration for the same task.
- Acceptance: given a `BackgroundRefreshTaskRegistrySetup` with one `TaskRegistrations` entry that has `Configuration` set, after construction `GetRegisteredTasks()` returns exactly one entry for that `TaskId` whose `RefreshTaskConfiguration` is reference/value-equal to the one supplied (not derived from app-settings, even if a matching app-settings section exists with different values).

**FR-2 — No-config tasks keep falling back to app-settings, unchanged.**
When `TaskRegistrationInfo.Configuration` is null, behavior is unchanged: `RegisterTask(taskId, refreshMethod)` derives configuration via `RefreshTaskConfiguration.FromAppSettings`.
- Acceptance: existing behavior for `CatalogModule`/`LogisticsModule`/`FinancialOverviewModule`-style registrations (no explicit configuration) is preserved — a task with no `Configuration` set still resolves from `IConfiguration`, and still throws `InvalidOperationException` if the app-settings section is missing (this is documented existing behavior for that path, not something to relax).

**FR-3 — No startup crash for explicit-config registrations lacking an app-settings section.**
A task registered via the explicit-configuration overload must construct successfully even when no `BackgroundRefresh:{Owner}:{Method}` section exists in configuration.
- Acceptance: a regression test constructs `BackgroundRefreshTaskRegistry` with an `IConfiguration` that has no `BackgroundRefresh` section at all, and a setup containing one task with explicit `Configuration` — construction must not throw.

## Non-functional requirements

- No behavior change for any existing production caller (all three modules use the no-config path today) — verified by running the full existing test suite, not just new tests.
- Fix must be minimal and localized to `BackgroundRefreshTaskRegistry.cs` per the "file is owned by this part" scope note; do not touch `BackgroundRefreshExtensions.cs`, `RefreshTaskConfiguration.cs`, or module registration call sites.

## Data model

No data model changes. Relevant existing types (unchanged):
- `TaskRegistrationInfo { TaskId, RefreshMethod, Configuration? }` — `Configuration` is nullable, its presence is the discriminator.
- `RegisteredTask(TaskId, RefreshMethod, Configuration)` — internal record stored in `_registeredTasks`.

## Interfaces

No public interface/API surface change. `IBackgroundRefreshTaskRegistry`, `RegisterTask` overloads, and `RegisterRefreshTask` extension methods are untouched — this is a pure bugfix inside `InitializeTasksFromSetup`'s private control flow.

## Dependencies and scope

**In scope:**
- `backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/BackgroundRefreshTaskRegistry.cs` — add the missing `else` (or equivalent early-continue) at lines 36-43.
- New/extended unit tests under `backend/test/Anela.Heblo.Tests/Xcc/BackgroundRefresh/` covering FR-1, FR-2, FR-3. No existing test file targets `BackgroundRefreshTaskRegistry` directly (only `BackgroundRefreshSchedulerServiceTests.cs` exists in that folder) — a new `BackgroundRefreshTaskRegistryTests.cs` is needed.

**Out of scope:**
- Any change to `BackgroundRefreshExtensions.cs`, `RefreshTaskConfiguration.FromAppSettings`, `BackgroundRefreshSchedulerService`, or module wiring.
- Migrating any production module to the explicit-configuration overload — not requested, and would expand blast radius beyond the owned file.
- Broader defensive hardening (e.g., logging a warning on duplicate `TaskId`, validating `TaskRegistrations` for duplicates) — not implied by this bug report.

## Rough plan

1. **Fix**: in `InitializeTasksFromSetup`, change the unconditional second `RegisterTask` call into an `else` branch (or `if (...) { ... } else { ... }` restructure) so exactly one of the two registration paths runs per `TaskRegistrationInfo`.
2. **Test — explicit config honoured (FR-1)**: construct the registry with a setup containing one task with explicit `RefreshTaskConfiguration` (e.g. custom `RefreshInterval`) and an `IConfiguration` that has a *different* value for the same task's app-settings section; assert `GetRegisteredTasks()` returns the explicit values, not the app-settings ones.
3. **Test — no-config fallback unchanged (FR-2)**: construct the registry with a setup containing one task with `Configuration == null` and a matching app-settings section; assert the registered configuration matches what `FromAppSettings` would produce.
4. **Test — no startup crash (FR-3)**: construct the registry with a setup containing one task with explicit `Configuration` and an empty `IConfiguration` (no `BackgroundRefresh` section at all); assert construction succeeds (no exception).
5. **Regression check**: run the full existing `BackgroundRefreshSchedulerServiceTests` and any other Xcc/BackgroundRefresh-related tests to confirm no unrelated breakage.
6. **Validation**: `dotnet build` + `dotnet format` on the touched project; run the affected test project (`dotnet test` filtered to `BackgroundRefresh`).

## Open questions

- None blocking. One judgment call made: the fix restructures the `if` into `if/else` rather than adding a `continue`/early-return, since that reads most clearly given the existing shape of the loop body — left to the implementer's discretion as it's a one-line-level choice with no behavioral difference.
