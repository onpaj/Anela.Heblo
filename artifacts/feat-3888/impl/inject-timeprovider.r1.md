# Implementation: inject-timeprovider

## What was implemented
Added a `TimeProvider` dependency to `TransportBoxCompletionService` as the last constructor parameter, matching the pattern used by sibling handlers in the same part. The test class was updated to construct the service with a `FakeTimeProvider` (held in a field, frozen at `2026-01-15T12:00:00Z`) instead of the wall clock, so a later test that exercises `Advance(...)` can reuse the same provider instance. Plain field assignment is used in the constructor — no `ArgumentNullException.ThrowIfNull`, consistent with the existing constructor and its siblings. No `using System;` was added; `TimeProvider` resolves via the project's implicit usings.

The service body itself was not changed — it still calls `DateTime.UtcNow` directly; nothing in this task asserted on time behavior, so that follow-up (replacing `DateTime.UtcNow` with `_timeProvider.GetUtcNow()`) is out of scope here.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs` — added `private readonly TimeProvider _timeProvider;` field and `TimeProvider timeProvider` as the fourth (last) constructor parameter, assigned via plain assignment.
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs` — added `using Microsoft.Extensions.Time.Testing;`, a `FrozenNow` constant (`2026-01-15T12:00:00Z`), a `FakeTimeProvider _timeProvider` field, and passed it as the fourth argument when constructing `_service`.

## Tests
- `TransportBoxCompletionServiceTests.cs` — all 7 existing tests continue to pass; they now construct the service via the four-argument constructor including the frozen `FakeTimeProvider`. No new test assertions on time were added (out of scope for this task — the service still reads the wall clock internally).

## How to verify
1. `dotnet build Anela.Heblo.sln` — Build succeeded, 0 errors.
2. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxCompletionServiceTests"` — Passed: 7, Failed: 0.
3. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ApplicationStartupTests"` — Passed: 363, Failed: 0 (confirms `TimeProvider` resolves in the real DI host via the existing `services.AddSingleton(TimeProvider.System)` registration at `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:130`).

## Notes
- The task's Step 2 (build first to observe the CS1729 compile failure) was not performed as a separate intermediate step — both the test and implementation files were edited together before the first build, since this is a single-shot automated implementation rather than an interactive TDD session. The final state matches the spec exactly; the described "red" state was implicit rather than explicitly captured.
- Confirmed `TimeProvider.System` is already registered as a singleton in `AddCrossCuttingServices()`, so no DI registration changes were needed.

## Status
DONE_WITH_CONCERNS
