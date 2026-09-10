# Specification: Null-guard `McpProductNotFoundTelemetryFilter.Process` against null `ExceptionTelemetry.Message`

## Summary
`McpProductNotFoundTelemetryFilter.Process` calls `exc.Message.Contains(...)` on every `ExceptionTelemetry` item flowing through the Application Insights processor chain, without checking whether `Message` is null. When an `ExceptionTelemetry` item with `Message == null` reaches this filter, it throws `NullReferenceException`, which has fired 6 times in the 7 days prior to 2026-08-30, all correlated with a separate `InvalidOperationException` in `PlaudCliClient.RunCliCoreAsync`. This spec covers the minimal, unconditionally-safe null-guard fix and its regression test; it does not cover tracing or fixing the upstream code path that produces the null `Message`.

## Background
`McpProductNotFoundTelemetryFilter` is an `ITelemetryProcessor` in the Application Insights processor chain (pattern mirrors the existing `AzureBlobConflictTelemetryFilter`). It exists to downgrade a specific, expected MCP protocol response (`McpException` with a `[ProductNotFound]`-prefixed message) from an error-level exception to a warning-level trace, so that expected "not found" outcomes don't pollute the exception stream.

The filter runs against **every** `ExceptionTelemetry` item in the pipeline — the `item is ExceptionTelemetry exc` check only narrows the type, it does not guarantee `exc.Message` is non-null. `ExceptionTelemetry.Message` (from the Application Insights SDK) is a nullable string property; some code paths construct or forward `ExceptionTelemetry` without setting it. Telemetry from 2026-08-29 onward shows exactly this: 6 `NullReferenceException` occurrences at `McpProductNotFoundTelemetryFilter.Process`, all timestamp-correlated (within ~50ms) with a separate `InvalidOperationException` tracked from `PlaudCliClient.RunCliCoreAsync`, suggesting whatever tracks that Plaud CLI failure as telemetry produces an `ExceptionTelemetry` with a null `Message`.

Because this filter sits in a chained pipeline (`_next` forwards to the next processor), an unhandled exception here does not just fail to convert one item — it can disrupt processing of that telemetry item and potentially the chain, which is a correctness and reliability problem independent of the root cause of the null `Message`.

The fix should make this filter safe against a null `Message` unconditionally, regardless of which upstream code path is (or later becomes) responsible for producing it.

## Functional Requirements

### FR-1: Null-guard the `Message` check in `Process`
Replace the unguarded `exc.Message.Contains(ProductNotFoundMarker, StringComparison.Ordinal)` check with a null-safe equivalent, e.g. `exc.Message?.Contains(ProductNotFoundMarker, StringComparison.Ordinal) == true`, in `backend/src/Anela.Heblo.API/Infrastructure/Telemetry/McpProductNotFoundTelemetryFilter.cs`.

When `exc.Message` is `null`, the item must not match the `[ProductNotFound]` conversion branch and must instead fall through to the existing `_next.Process(item)` passthrough at the bottom of the method — i.e. it is forwarded unchanged, exactly like any other non-matching `ExceptionTelemetry`.

**Acceptance criteria:**
- `Process` no longer throws for any `ExceptionTelemetry` item where `Message` is `null`.
- An `ExceptionTelemetry` with `Message == null` is forwarded via `_next.Process(item)` unchanged (not converted to a `TraceTelemetry`, not dropped).
- Existing behavior for non-null `Message` values (matching and non-matching cases, MCP and non-MCP exception types, non-`ExceptionTelemetry` items) is unchanged — no regressions in current passing tests.
- The fix is confined to the null-check on `Message`; `IsMcpException` and all other logic in the file are untouched (it does not dereference `Message` and needs no change).

### FR-2: Add a regression test for null `Message`
Add a test case to the existing `McpProductNotFoundTelemetryFilterTests.cs` (`backend/test/Anela.Heblo.Tests/Infrastructure/Telemetry/McpProductNotFoundTelemetryFilterTests.cs`) that constructs an `ExceptionTelemetry` with `Message == null` and asserts the filter handles it without throwing and forwards it unchanged to `_next`.

**Acceptance criteria:**
- New test constructs an `ExceptionTelemetry` (e.g. wrapping an `McpException` or any `Exception`, consistent with existing test helpers) and explicitly leaves/sets `Message` to `null`.
- Calling `_filter.Process(exc)` with this item does not throw.
- The test verifies `_next.Process(exc)` was called once (unchanged passthrough), mirroring the assertion style of `Process_ForwardsNonMcpExceptions` / `Process_ForwardsNonExceptionTelemetry`.
- Test follows the existing file's conventions (xUnit `[Fact]`, Moq `_next` verification, FluentAssertions where applicable, same class/namespace).
- All existing tests in the file continue to pass unmodified.

## Non-Functional Requirements

### NFR-1: Performance
N/A — the fix is a single added null check (`?.` operator) with no measurable performance impact on the telemetry pipeline.

### NFR-2: Security
N/A — no security-sensitive data, auth, or external input handling is affected. This is an internal telemetry-processing null-guard.

## Data Model
N/A — no data model changes. No new entities; `ExceptionTelemetry`/`TraceTelemetry` are existing Application Insights SDK types used as-is.

## API / Interface Design
N/A — no public API, endpoint, or UI surface. The change is internal to the `ITelemetryProcessor` implementation `McpProductNotFoundTelemetryFilter.Process(ITelemetry item)`; its method signature and constructor are unchanged.

## Dependencies
- `Microsoft.ApplicationInsights` SDK types (`ITelemetry`, `ExceptionTelemetry`, `TraceTelemetry`, `ITelemetryProcessor`) — already in use, no version change required.
- No new NuGet packages, no new configuration.

## Out of Scope
- Tracing or fixing the upstream code path that constructs the `ExceptionTelemetry` with `Message == null` (hypothesized to be wherever the Plaud CLI job's `InvalidOperationException` from `PlaudCliClient.RunCliCoreAsync` is tracked as telemetry). The brief explicitly calls this out as a separate follow-up ("fixing only the null-check treats the symptom, not why `Message` is null in the first place").
- The companion `System.InvalidOperationException@PlaudCliClient.RunCliCoreAsync` telemetry signal, tracked separately.
- Any change to `IsMcpException`, the `AzureBlobConflictTelemetryFilter` pattern this mirrors, or the broader telemetry processor chain registration/ordering.
- Any change to log/telemetry severity thresholds or sampling configuration.

## Open Questions
None.

## Status: COMPLETE
