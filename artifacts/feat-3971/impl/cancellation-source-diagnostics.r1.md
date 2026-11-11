# Implementation: cancellation-source-diagnostics

## What was implemented
FR-4. The `catch` block of `HebloFeatureProvider.ResolveBooleanValueAsync` now tells apart who cancelled:

- `OperationCanceledException` while the caller's token is cancelled: logged at Debug, no exception attached, message ends with `cancelledBy=caller`. No Warning is emitted.
- Any other failure: logged at Warning as before, with the existing `dbRetryExhausted={DbRetryExhausted} attempts={Attempts}` fields unchanged and `cancelledBy={CancelledBy}` appended (`refresh` for an `OperationCanceledException` the caller did not cause, `none` otherwise).

The returned `ResolutionDetails<bool>` is identical in both branches (default value, `Reason.Error`); the method still never throws. The code is the snippet from the task context, verbatim.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs` — `catch` block of `ResolveBooleanValueAsync` only
- `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/HebloFeatureProviderTests.cs` — three new tests, verbatim from the task context

## Tests
New, in `HebloFeatureProviderTests.cs`:
- `ResolveBooleanValueAsync_CallerCancelled_LogsDebugNotWarning`
- `ResolveBooleanValueAsync_RefreshCancelledWithoutCallerCancel_LogsWarningCancelledByRefresh`
- `ResolveBooleanValueAsync_NonCancellationFailure_LogsWarningCancelledByNone`

Observed in this session:

```
# red, before the production change (tests added, test project built)
$ dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~HebloFeatureProviderTests"
Failed!  - Failed:     3, Passed:    13, Skipped:     0, Total:    16   (the three new tests)

# after the production change
$ dotnet build Anela.Heblo.sln
0 Error(s), 127 Warning(s)   (incremental build; warnings are not from the changed files)

$ dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~HebloFeatureProviderTests"
Passed!  - Failed:     0, Passed:    16, Skipped:     0, Total:    16

$ dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~FeatureFlags"
Passed!  - Failed:     0, Passed:    28, Skipped:     0, Total:    28

$ dotnet format Anela.Heblo.sln --include <the two files>
exit 0, no changes
```

## How to verify
`dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~HebloFeatureProviderTests"` — expect 16 passed.

## Notes
- The full test project was not run, only the `FeatureFlags` filter (28 tests). A first combined build+test invocation hit the tool time limit and was re-run as build, then `--no-build` test.
- The Debug entry carries `cancelledBy=caller` as literal message text, not as a structured property; the Warning carries it as the structured property `CancelledBy`. This follows the task context exactly. A log query filtering on a `CancelledBy` custom dimension will not match the Debug entries.
- If the shared refresh times out and the caller's token happens to be cancelled by the time the `catch` runs, the entry is classified as `caller` (Debug). This follows from the spec's definition (`ct.IsCancellationRequested`) and is not handled separately.
- The process doc is not touched here; that is the next task (`update-process-doc`).
- Developer context file: `.agents/developer.md` declares `~/.claude/plugins/cache/*/superpowers/*/skills/subagent-driven-development/SKILL.md`, which matches no file in this environment. On the coordinator's instruction the repo-tracked copy `.claude/skills/subagent-driven-development/SKILL.md` was used instead. `.agents/developer.md` was not edited.
- No subagent tool was available: on the coordinator's instruction the implementer, spec-compliance and code-quality passes, and the task reviewer, all ran inline in one session.

## PR Summary
Feature-flag resolution now records who cancelled a failed lookup. A request that was itself cancelled (typically an aborted HTTP request) is logged at Debug with `cancelledBy=caller` instead of a Warning with a stack trace, so it no longer shows up as a database problem. Genuine failures keep the Warning and its `dbRetryExhausted`/`attempts` fields and gain `cancelledBy=refresh` (the shared refresh timed out or was cancelled) or `cancelledBy=none`.

### Changes
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs` — cancellation-source branch in the `catch` block of `ResolveBooleanValueAsync`
- `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/HebloFeatureProviderTests.cs` — three tests covering caller, refresh and non-cancellation failures

## Status
DONE
