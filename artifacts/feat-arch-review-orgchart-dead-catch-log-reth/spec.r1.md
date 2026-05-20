# Specification: Remove Redundant Try-Catch in GetOrganizationStructureHandler

## Summary
Remove the redundant try-catch block in `GetOrganizationStructureHandler.Handle` that only logs and rethrows. Error handling and HTTP response shaping remain centralized in `OrgChartController`, eliminating duplicate log entries for every failure path.

## Background
Daily architecture review (2026-05-19) flagged that `GetOrganizationStructureHandler.cs` (lines 29–35) wraps a single service call in a try-catch that adds no behavior — it logs the exception and rethrows. `OrgChartController.cs` (lines 40–53) already has an identical catch block that logs and converts the exception into a 500 response. The result is duplicate log entries for every error, increased log volume, and harder-to-read traces. The handler-level catch violates KISS: code that does not earn its place should be removed.

## Functional Requirements

### FR-1: Remove handler-level exception handling
The `Handle` method in `GetOrganizationStructureHandler` must contain only the happy path: a single `LogInformation` call followed by the service invocation. The try-catch must be removed entirely so that exceptions propagate unmodified to the MediatR pipeline and ultimately to the controller.

**Acceptance criteria:**
- `GetOrganizationStructureHandler.Handle` contains no try-catch block.
- The method body is exactly: log informational message, then `return await _orgChartService.GetOrganizationStructureAsync(cancellationToken);`.
- No `using` directives become unused after removal; if any do, remove them.
- The handler still logs the informational "Handling request..." line at entry.

### FR-2: Preserve controller-level error handling
The existing try-catch in `OrgChartController` (lines 40–53) must remain unchanged. It is the authoritative point for logging the failure and translating it into a 500 response for the client.

**Acceptance criteria:**
- `OrgChartController` continues to catch exceptions from the MediatR send call.
- The 500 response shape returned to the client is unchanged.
- Error log message and log level in the controller are unchanged.

### FR-3: Preserve client-observable behavior
The change is a pure refactor. Clients calling the org-chart endpoint must observe identical responses for both success and failure paths (status codes, response bodies, headers).

**Acceptance criteria:**
- Successful requests return the same `OrgChartResponse` payload and 200 status code as before.
- Failed requests (when `_orgChartService.GetOrganizationStructureAsync` throws) return the same 500 response shape as before.
- No new exception types are introduced or swallowed.

### FR-4: Update or add tests
Any existing handler tests asserting on the redundant catch behavior must be updated. If tests verify that the handler logs an error on failure, they must be removed or rewritten to verify that exceptions now propagate. Controller tests must continue to verify the 500 response on exception.

**Acceptance criteria:**
- All existing tests for `GetOrganizationStructureHandler` pass.
- No test asserts on a handler-level error log entry.
- A test exists (new or existing) verifying that an exception thrown by `_orgChartService` propagates out of `Handle` unchanged.
- Controller tests verifying 500 response on exception continue to pass.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance impact expected. Removing one try-catch frame is negligible. Log volume on error paths decreases by roughly 50% for the org-chart endpoint.

### NFR-2: Security
No security implications. Exception messages were already being logged at the controller layer; no new information is exposed to clients.

### NFR-3: Maintainability
The change improves maintainability by removing dead code and consolidating error-handling responsibility at a single layer (controller).

### NFR-4: Observability
Each org-chart request failure must produce exactly one error log entry (from the controller). The information content of that single log line must be at least as rich as the previous handler log line (exception + contextual message). Verify that the controller log already includes the exception object; if not, this spec does not extend the controller log — note it in Open Questions instead.

## Data Model
No data model changes.

## API / Interface Design
No public API changes. The MediatR request/response contracts (`GetOrganizationStructureRequest`, `OrgChartResponse`) and the controller endpoint signature remain identical.

**Files affected:**
- `GetOrganizationStructureHandler.cs` — remove try-catch in `Handle`.
- `OrgChartController.cs` — unchanged.
- Handler unit tests — update assertions if any reference the removed log line.

## Dependencies
- MediatR (existing) — exceptions thrown from `Handle` propagate through the pipeline unchanged.
- `ILogger<GetOrganizationStructureHandler>` (existing) — still used for the informational log.
- `_orgChartService` (existing) — no change to its contract.

## Out of Scope
- Refactoring the controller's error handling (e.g., moving to a global exception filter / middleware).
- Auditing other handlers in the codebase for the same anti-pattern. (May be addressed in a follow-up arch review.)
- Changing the 500 response shape or error message format.
- Adding new structured-logging fields or correlation IDs.
- Renaming or relocating files.

## Open Questions
None.

## Status: COMPLETE