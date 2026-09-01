# Design: Null-guard McpProductNotFoundTelemetryFilter against null ExceptionTelemetry.Message

## Component Design

**Affected component:** `McpProductNotFoundTelemetryFilter` (`backend/src/Anela.Heblo.API/Infrastructure/Telemetry/McpProductNotFoundTelemetryFilter.cs`), an `ITelemetryProcessor` in the Application Insights processor chain. No new components are introduced; the existing chain topology is unchanged:

```
AI TelemetryProcessor chain
  ... -> McpProductNotFoundTelemetryFilter.Process(ITelemetry item) -> _next.Process(item) -> ...
```

**Responsibility (unchanged):** For every `ExceptionTelemetry` item passed through `Process`, detect the specific case of an `McpException` whose message is prefixed with the `[ProductNotFound]` marker, and downgrade only that case to a `TraceTelemetry` (Warning) before forwarding. All other items — including non-`ExceptionTelemetry` items, non-MCP exceptions, non-matching MCP exceptions, and (after this fix) `ExceptionTelemetry` items with a `null` `Message` — pass through unchanged via `_next.Process(item)`.

**Interface (unchanged):** `ITelemetryProcessor.Process(ITelemetry item)` — no signature change, no constructor change, no new dependency.

**Internal change:** the existing match condition inside `Process` is made null-safe:

- Before: `exc.Message.Contains(ProductNotFoundMarker, StringComparison.Ordinal)`
- After: `exc.Message?.Contains(ProductNotFoundMarker, StringComparison.Ordinal) == true`

This is a single-expression edit with no new branch, no new method, and no change to `IsMcpException` or any other logic in the file. When `Message` is `null`, the condition evaluates to `false`, so control falls through to the existing non-matching path (`_next.Process(item)`), exactly as it already does for any other non-matching `ExceptionTelemetry`.

**Test component:** `McpProductNotFoundTelemetryFilterTests` (`backend/test/Anela.Heblo.Tests/Infrastructure/Telemetry/McpProductNotFoundTelemetryFilterTests.cs`) gains one new `[Fact]` (e.g. `Process_ForwardsExceptionTelemetryWithNullMessage`) that builds an `ExceptionTelemetry` with `Message == null`, calls `_filter.Process(...)`, asserts no exception is thrown, and verifies `_next.Process(item)` was invoked exactly once — following the same Moq/xUnit/FluentAssertions conventions and the existing `BuildMcpExceptionTelemetry`-style helper as the file's current tests (e.g. `Process_ForwardsNonMcpExceptions`).

## Data Schemas

N/A — this fix touches no persistence schema, API contract, DTO, or event payload shape. It uses only existing Application Insights SDK types (`ITelemetry`, `ExceptionTelemetry`, `TraceTelemetry`) as-is; no properties are added, removed, or renamed on any type.
