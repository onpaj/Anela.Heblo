# Implementation: single-flight-refresh

## What was implemented
`HebloFeatureProvider.GetOverridesAsync` now does a lock-free cache hit, and on a miss takes a `SemaphoreSlim` gate to start (or join) a single shared in-flight load. The load (`LoadOverridesAsync`) runs on its own 5s `RefreshTimeout` token, never the caller's, and populates the cache only on success (failures are not cached; the next caller retries). Callers wait with `Task.WaitAsync(ct)`, so a cancelled caller abandons only its own wait. `ResolveBooleanValueAsync` is unchanged and still never throws.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs` — single-flight refresh, `RefreshTimeout`, `_gate`, `_inflight`, `LoadOverridesAsync`
- `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/HebloFeatureProviderTests.cs` — `MakeProviderWithRepo` helper and 6 new tests

## Tests
Six new tests: concurrent cold-cache callers query DB once; concurrent callers after expiry query once more; caller cancellation cancels neither the load nor other callers; a load started by a cancelled caller still populates the cache; failed load is not cached; cache hit does not re-query.

## How to verify
`dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~HebloFeatureProviderTests"` — 13 passed (7 existing + 6 new).

## Notes
- No subagents were spawned; implementer, spec-compliance and code-quality review passes were done by me sequentially. Code matches the task snippets verbatim.
- A first combined build+test invocation exceeded the tool time limit; rerunning build (0 errors) then `dotnet test --no-build` completed normally.
- `dotnet format` made no changes.

## PR Summary
Feature-flag override refresh is now single-flight and detached from the caller's cancellation token. Previously every cache miss queried the database with the first caller's token, so a cancelled request surfaced as a TaskCanceledException in telemetry and concurrent callers stampeded the DB. Now one shared load runs on its own 5 second timeout, callers only abandon their own wait on cancellation, and failures are never cached.

### Changes
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs` — single-flight gate, caller-independent `LoadOverridesAsync`
- `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/HebloFeatureProviderTests.cs` — six tests covering single-flight, cancellation and failure semantics

## Status
DONE
