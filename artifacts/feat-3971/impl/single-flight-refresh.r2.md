# Implementation: single-flight-refresh (revision 2)

## What was implemented
Revision round addressing `review/single-flight-refresh.r1.md`. Production code (`HebloFeatureProvider.cs`) is unchanged from r1 (the review judged it compliant). Only the test `ResolveBooleanValueAsync_LoadStartedByCancelledCaller_StillPopulatesCache` changed:

- The `cache.TryGetValue(...)` assertion no longer runs synchronously right after `pending.SetResult(...)`. It now runs after awaiting a second `ResolveBooleanValueAsync` call, which either hits the cache or joins the still-running shared load. The test therefore no longer depends on the load's continuation running inline inside `SetResult`.
- The incorrect comment ("the load's continuation (cache.Set) runs inline inside SetResult") was replaced with one describing the actual reasoning.
- Added `next.Reason.Should().Be(Reason.TargetingMatch)`. `repo.Verify(..., Times.Once)` still proves the cache was populated by the load the cancelled caller started.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/HebloFeatureProviderTests.cs` — reordered assertions and corrected comment in one test

## Tests
Observed in this session (after `dotnet build Anela.Heblo.sln`, then `--no-build`):

```
$ dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~HebloFeatureProviderTests"
Passed!  - Failed:     0, Passed:    13, Skipped:     0, Total:    13, Duration: 63 ms - Anela.Heblo.Tests.dll (net8.0)
```

Run three times after the change, 13/13 each time (63 ms, 63 ms, 69 ms). Per-test output (`-v n`):

```
Passed ...ResolveBooleanValueAsync_ConcurrentCallersOnColdCache_QueryDbOnce [81 ms]
Passed ...ResolveBooleanValueAsync_LogsDbRetryNotExhausted_WhenExceptionHasNoAttemptTag [30 ms]
Passed ...ResolveBooleanValueAsync_DbOverrideCanDisable_WhenConfigEnables [2 ms]
Passed ...ResolveBooleanValueAsync_CallerCancellation_DoesNotCancelLoadOrOtherCallers [5 ms]
Passed ...ResolveBooleanValueAsync_ConcurrentCallersAfterCacheExpiry_QueryDbOnceMore [7 ms]
Passed ...ResolveBooleanValueAsync_LogsDbRetryExhausted_WhenExceptionCarriesMultipleAttempts [2 ms]
Passed ...ResolveBooleanValueAsync_FailsOpenToDefault_WhenDbThrows [< 1 ms]
Passed ...ResolveBooleanValueAsync_DbOverrideWins_OverConfigAndDefault [< 1 ms]
Passed ...ResolveBooleanValueAsync_CacheHit_DoesNotQueryDbAgain [< 1 ms]
Passed ...ResolveBooleanValueAsync_FailedLoadIsNotCached_NextCallReloads [2 ms]
Passed ...ResolveBooleanValueAsync_FallsBackToConfig_WhenNoDbOverride [< 1 ms]
Passed ...ResolveBooleanValueAsync_FallsBackToDefault_WhenNoOverrideOrConfig [1 ms]
Passed ...ResolveBooleanValueAsync_LoadStartedByCancelledCaller_StillPopulatesCache [3 ms]
```

Wider filter `FullyQualifiedName~FeatureFlags`: `Passed! - Failed: 0, Passed: 25, Skipped: 0, Total: 25`.

Build: `dotnet build Anela.Heblo.sln` — 0 Error(s), 185 Warning(s) (pre-existing warnings). `dotnet format Anela.Heblo.sln --include <the two files>` — exit 0, no changes.

## How to verify
`dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~HebloFeatureProviderTests"` — expect 13 passed.

## Notes
- The r1 test, before this change, was also run three times in this session and passed 13/13 each time. So the r1 reviewer's prediction that the synchronous assertion would fail did not reproduce here; the continuation does run inline under the current xUnit setup. The change was made anyway because the assertion relied on that scheduling detail, as the review asked.
- The test now deviates from the verbatim snippet in the task context (Step 2), deliberately, per the r1 review.
- The full test project was not run, only the `FeatureFlags` filter. A first combined build+test invocation hit the tool time limit and was re-run as build then `--no-build` test.
- Developer context file: `.agents/developer.md` declares `~/.claude/plugins/cache/*/superpowers/*/skills/subagent-driven-development/SKILL.md`, which matches no file in this environment. On the coordinator's instruction the repo-tracked copy `.claude/skills/subagent-driven-development/SKILL.md` was used instead. Content parity with the plugin version could not be checked, since the plugin is not installed.
- No subagent tool was available: implementer, spec-compliance and code-quality passes, and the task reviewer, all ran inline in one session.

## PR Summary
Feature-flag override refresh is now single-flight and detached from the caller's cancellation token. Previously every cache miss queried the database with the first caller's token, so a cancelled request surfaced as a TaskCanceledException in telemetry and concurrent callers stampeded the DB. Now one shared load runs on its own 5 second timeout, callers only abandon their own wait on cancellation, and failures are never cached.

### Changes
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs` — single-flight gate, caller-independent `LoadOverridesAsync`
- `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/HebloFeatureProviderTests.cs` — six tests covering single-flight, cancellation and failure semantics

## Status
DONE
