# Implementation: null-guard-telemetry-filter

## What was implemented
Fixed a `NullReferenceException` in `McpProductNotFoundTelemetryFilter.Process` that occurred when Application Insights delivers an `ExceptionTelemetry` item whose `Message` property is `null`. The filter previously called `exc.Message.Contains(...)` unconditionally; it now uses a null-conditional check (`exc.Message?.Contains(...) == true`) so telemetry items with a null `Message` are safely forwarded to the next processor instead of crashing the telemetry pipeline. A regression test reproducing the crash was added first (TDD), confirmed to fail against the buggy code, then confirmed to pass after the fix.

## Files created/modified
- `backend/src/Anela.Heblo.API/Infrastructure/Telemetry/McpProductNotFoundTelemetryFilter.cs` — line 29: changed `exc.Message.Contains(...)` to `exc.Message?.Contains(...) == true` (null-guard against `ExceptionTelemetry.Message == null`). No other lines changed.
- `backend/test/Anela.Heblo.Tests/Infrastructure/Telemetry/McpProductNotFoundTelemetryFilterTests.cs` — added `Process_ForwardsExceptionTelemetryWithNullMessage` `[Fact]` at the end of the class, asserting the filter does not throw and forwards an `ExceptionTelemetry` with `Message = null` unchanged to `_next.Process`.

## Tests
- `backend/test/Anela.Heblo.Tests/Infrastructure/Telemetry/McpProductNotFoundTelemetryFilterTests.cs` — full class (6 tests), covering: converting a matching McpException to a warning trace, copying properties onto the trace, forwarding non-matching McpException types, forwarding non-MCP exceptions, forwarding non-`ExceptionTelemetry` telemetry, and (new) forwarding `ExceptionTelemetry` with a `null` `Message` without throwing.

## How to verify
1. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~McpProductNotFoundTelemetryFilterTests"` — expect `Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6`.
2. `dotnet build Anela.Heblo.sln` from the repo root — expect `0 Error(s)`.
3. `dotnet format Anela.Heblo.sln --include backend/src/Anela.Heblo.API/Infrastructure/Telemetry/McpProductNotFoundTelemetryFilter.cs backend/test/Anela.Heblo.Tests/Infrastructure/Telemetry/McpProductNotFoundTelemetryFilterTests.cs --verify-no-changes` — expect exit code 0 (no formatting diffs).

## Notes
Reproduced the bug exactly as specified: the new test failed with `System.NullReferenceException: Object reference not set to an instance of an object.` at `McpProductNotFoundTelemetryFilter.Process` line 28 (the `if` statement) before the fix, and passed after it. No deviations from the task spec — only the single line described in the task was changed in the source file, and the test was added verbatim as specified. `artifacts/feat-4001/state.json` was modified in the working tree by pipeline tooling but was intentionally left out of this commit, per the task's explicit two-file `git add` instruction.

## PR Summary
This fixes issue #4001: `McpProductNotFoundTelemetryFilter` threw a `NullReferenceException` when Application Insights delivered an `ExceptionTelemetry` item with a `null` `Message` (e.g. telemetry constructed without a live exception object, or certain SDK-internal paths that don't populate `Message`). The filter's marker-matching condition `exc.Message.Contains(ProductNotFoundMarker, ...)` assumed `Message` was always non-null, which is not guaranteed by the Application Insights SDK. The fix null-guards the check with `exc.Message?.Contains(ProductNotFoundMarker, StringComparison.Ordinal) == true`, so telemetry items with a null `Message` simply fall through to `_next.Process(item)` unchanged, exactly like any other non-matching telemetry item — restoring the filter's intended fail-safe behavior instead of crashing the telemetry pipeline. A new xUnit fact (`Process_ForwardsExceptionTelemetryWithNullMessage`) locks in this behavior; it was verified to reproduce the original crash before the fix and to pass afterward. The full 6-test class, a full solution build (0 errors), and `dotnet format --verify-no-changes` on the two touched files all pass.

### Changes
- `backend/src/Anela.Heblo.API/Infrastructure/Telemetry/McpProductNotFoundTelemetryFilter.cs` — null-guard `exc.Message.Contains(...)` to `exc.Message?.Contains(...) == true`.
- `backend/test/Anela.Heblo.Tests/Infrastructure/Telemetry/McpProductNotFoundTelemetryFilterTests.cs` — added `Process_ForwardsExceptionTelemetryWithNullMessage` regression test.

## Status
DONE
