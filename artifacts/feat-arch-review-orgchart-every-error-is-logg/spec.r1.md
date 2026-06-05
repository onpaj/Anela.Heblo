# Specification: OrgChart Service — Consolidate Error Logging to Single Site

## Summary
Every failure in the OrgChart feature currently produces two `Error`-level log entries: one from `OrgChartService` and one from `OrgChartController`. This spec removes the duplicate logging by making the service throw without logging and letting the controller's catch block remain the single error-logging site, preserving error context while eliminating noise.

## Background
`OrgChartService.GetOrganizationStructureAsync` catches `HttpRequestException`, `JsonException`, and generic `Exception`, logs each at `LogLevel.Error` with infrastructure context (the data source URL), and then re-throws — wrapping the first two in `InvalidOperationException`. The downstream `OrgChartController.GetOrganizationStructure` catches the re-thrown exception and logs *another* `Error` line with a generic message.

The result:
- **Duplicate log volume**: Every failure produces two error entries with different messages and different logger categories.
- **Split context**: Neither message is self-sufficient — the service line has the URL, the controller line has the request scope, so engineers must correlate both.
- **Contradictory ownership**: The existing test `GetOrganizationStructureHandlerTests.cs:63-73` explicitly states "the controller owns failure logging," yet the service logs anyway. This makes the architectural rule ambiguous.

Consolidating to a single site fixes the noise and removes the ambiguity. Choosing the controller as the owner aligns with the existing test's assertion and with the broader pattern of HTTP-layer error observability.

## Functional Requirements

### FR-1: Remove error logging from `OrgChartService`
Remove the `_logger.LogError(...)` call from each of the three `catch` blocks in `OrgChartService.GetOrganizationStructureAsync` (lines 60–74). The service must still:
- Catch the same exception types it catches today.
- Wrap `HttpRequestException` and `JsonException` in `InvalidOperationException` with the same messages.
- Re-throw the generic `Exception` unchanged.
- Preserve the existing `LogInformation` happy-path log (no change).

**Acceptance criteria:**
- `OrgChartService.GetOrganizationStructureAsync` contains zero `LogError`, `LogWarning`, or `LogCritical` calls.
- Existing typed-exception throw behavior is preserved: `HttpRequestException` → `InvalidOperationException("Failed to fetch organizational structure: {ex.Message}", ex)`, `JsonException` → `InvalidOperationException("Failed to parse organizational structure: {ex.Message}", ex)`, other `Exception` → re-thrown.
- Inner exception is preserved on every wrap.
- Happy-path `LogInformation` line is unchanged.

### FR-2: Keep controller as the single error-logging site
`OrgChartController.GetOrganizationStructure` continues to catch the exception and produce exactly one `Error`-level log line per failure. The controller's log message must include enough context to be useful on its own.

**Acceptance criteria:**
- On any failure, exactly one `Error`-level entry is emitted for the OrgChart fetch path, originating from the `OrgChartController` logger category.
- The controller's log message contains, at minimum, the exception (so the URL and inner-exception chain are visible via `ex.ToString()`).
- HTTP response behavior of the controller is unchanged (same status code, same response body).

### FR-3: Update the handler test to reflect single-owner logging
`GetOrganizationStructureHandlerTests.cs:63-73` currently asserts that the handler/service does *not* own failure logging. Confirm this assertion remains valid and update the inline comment if it references behavior that no longer needs explaining (i.e., the contradiction is gone).

**Acceptance criteria:**
- All existing OrgChart tests pass without modification of assertion intent.
- If a test asserts the service logs an error on failure, it is updated to assert the opposite (no error log from the service).
- Comment text on `GetOrganizationStructureHandlerTests.cs:63-73` accurately describes the new single-owner design.

### FR-4: Add a regression test for "no duplicate logging"
Add (or extend) a unit test that drives `OrgChartService` into each of the three failure paths and asserts that the injected `ILogger<OrgChartService>` receives **zero** `LogLevel.Error` invocations.

**Acceptance criteria:**
- One test per failure path: `HttpRequestException`, `JsonException`, generic `Exception`.
- Each test asserts the service throws the expected typed exception.
- Each test asserts `Mock<ILogger<OrgChartService>>.Verify(...)` for `LogLevel.Error` is called `Times.Never`.

## Non-Functional Requirements

### NFR-1: Performance
No measurable impact. Removing log calls slightly reduces work on the failure path; happy path is unchanged.

### NFR-2: Security
No change to authentication, authorization, or data sensitivity. No PII is added to or removed from logs. The controller's `ex.ToString()` may include the data source URL (already logged today), which is internal configuration — same exposure as current state.

### NFR-3: Observability
- Error rate alerts based on log entries from the `OrgChart` namespace will see a ~50% reduction in count for the same number of real failures. This is the desired outcome but should be communicated to anyone relying on the current count.
- Log correlation across layers becomes simpler: one error per failure, with the full exception chain.

### NFR-4: Backwards compatibility
- No public API contracts change (exception types and messages thrown by the service are identical).
- No DTO, route, or response shape changes.

## Data Model
No data model changes.

## API / Interface Design
No API surface changes. The only behavioral change is the absence of the service-emitted error log entry. HTTP responses, status codes, and exception types are identical to today.

## Dependencies
- `Microsoft.Extensions.Logging.Abstractions` (already referenced).
- No new packages, no new external services.

## Out of Scope
- Replacing the controller's per-action `try/catch` with global exception-handling middleware. The brief mentions this as an alternative; it is a larger architectural change affecting other modules and is deferred.
- Restructuring log message templates or introducing structured-logging conventions beyond what already exists.
- Adding new typed exceptions (e.g., `OrgChartFetchException`, `OrgChartParseException`) to replace the `InvalidOperationException` wrap. Worth considering separately but not required to remove the duplicate.
- Changing the happy-path `LogInformation` line in `OrgChartService`.
- Reviewing or modifying other modules with similar duplicate-logging patterns.

## Open Questions
None.

## Status: COMPLETE