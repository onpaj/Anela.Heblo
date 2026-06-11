```markdown
# Specification: Unit tests for ProcessDailyConsumptionHandler

## Summary
Add focused unit-test coverage for `ProcessDailyConsumptionHandler`, the MediatR handler that drives daily packing-material consumption processing. The handler currently has zero tests; this spec defines the four behavioral paths that must be locked in so the idempotency guard and response-shape contract cannot regress silently.

## Background
`ProcessDailyConsumptionHandler` orchestrates `IConsumptionCalculationService.ProcessDailyConsumptionAsync` and translates its result into a `ProcessDailyConsumptionResponse`. It is the entry point for the daily packing-material consumption job, and its `result.WasRun == false` branch is the **idempotency gate** that prevents the same date from being processed twice. A regression here would inflate packing-material deductions for every day the job re-runs.

The handler is not referenced by any existing test file. CI run #27104028537 (commit 6568feba) flagged the gap on 2026-06-08 via the weekly coverage-gap routine.

The handler currently exhibits four observable behaviors:

1. **Already processed** — service returns `WasRun = false`; handler returns `Success = false`, `MaterialsProcessed = 0`, message stating the date was already processed.
2. **Ran, no invoices** — service returns `WasRun = true`, `MaterialsProcessed = 0`; handler returns `Success = true`, `MaterialsProcessed = 0`, "no invoices found" message.
3. **Ran, materials updated** — service returns `WasRun = true`, `MaterialsProcessed > 0`; handler returns `Success = true`, materials count propagated, "successfully processed … N materials updated" message.
4. **Service throws** — handler logs the exception and returns `Success = false`, `MaterialsProcessed = 0`, a generic error message that **does not leak the exception detail**.

## Functional Requirements

### FR-1: Idempotency-gate test (`WasRun == false`)
A unit test must assert that when `IConsumptionCalculationService.ProcessDailyConsumptionAsync` returns a result with `WasRun = false`, the handler produces a response with `Success = false`, `MaterialsProcessed = 0`, `ProcessedDate` equal to the request date, and a `Message` that explicitly identifies the date as already processed.

**Acceptance criteria:**
- Mocked service returns a result with `WasRun = false` (any `MaterialsProcessed` value, including non-zero, to prove the handler does not trust that field when `WasRun = false`).
- Response `Success` is `false`.
- Response `MaterialsProcessed` is `0`.
- Response `ProcessedDate` equals the input `ProcessingDate`.
- Response `Message` contains the processing date and indicates idempotency (e.g. contains "already processed").
- The mock is invoked exactly once with the request's `ProcessingDate` and the supplied `CancellationToken`.

### FR-2: Successful run with materials updated (`WasRun == true`, `MaterialsProcessed > 0`)
A unit test must assert the normal success path where the service reports a positive materials-processed count.

**Acceptance criteria:**
- Mocked service returns `WasRun = true`, `MaterialsProcessed = N` where `N > 0` (use a non-trivial value such as 5).
- Response `Success` is `true`.
- Response `MaterialsProcessed` equals `N`.
- Response `ProcessedDate` equals the input `ProcessingDate`.
- Response `Message` references both the date and the `N` materials updated.

### FR-3: Successful run with no invoices (`WasRun == true`, `MaterialsProcessed == 0`)
A unit test must assert the success path where processing executed but found nothing to update.

**Acceptance criteria:**
- Mocked service returns `WasRun = true`, `MaterialsProcessed = 0`.
- Response `Success` is `true` (the run is considered successful even with zero materials).
- Response `MaterialsProcessed` is `0`.
- Response `ProcessedDate` equals the input `ProcessingDate`.
- Response `Message` indicates "no invoices" / "no materials were updated" semantics and includes the date.

### FR-4: Exception handling test
A unit test must assert that exceptions thrown by the service are caught, logged, and converted into a non-leaking error response.

**Acceptance criteria:**
- Mocked service throws an exception with an identifiable inner message (e.g. `InvalidOperationException("secret database connection string")`).
- The handler does **not** propagate the exception (the test does not expect a thrown exception).
- Response `Success` is `false`.
- Response `MaterialsProcessed` is `0`.
- Response `ProcessedDate` equals the input `ProcessingDate`.
- Response `Message` is generic and does **not** contain the exception's message text or stack trace.
- An error-level log entry is emitted (verified via a mocked/captured `ILogger<ProcessDailyConsumptionHandler>`).

### FR-5: Test file placement and naming
The new tests must live alongside other backend unit tests for the PackingMaterials feature, following the existing test-project conventions.

**Acceptance criteria:**
- A new test file `ProcessDailyConsumptionHandlerTests.cs` is added under the backend unit-test project's `Features/PackingMaterials/UseCases/ProcessDailyConsumption/` folder (mirroring the source structure).
- Test class name: `ProcessDailyConsumptionHandlerTests`.
- Each test method names the behavior under test (e.g. `Handle_ReturnsFailure_WhenAlreadyProcessed`, `Handle_ReturnsSuccess_WhenMaterialsUpdated`, `Handle_ReturnsSuccessWithZeroCount_WhenNoInvoicesFound`, `Handle_ReturnsGenericError_WhenServiceThrows`).
- Tests follow AAA (Arrange-Act-Assert) structure.

### FR-6: No production code changes
This task is test-only. The handler's behavior must not change.

**Acceptance criteria:**
- No edits to `ProcessDailyConsumptionHandler.cs`, `ProcessDailyConsumptionRequest`, `ProcessDailyConsumptionResponse`, `IConsumptionCalculationService`, or any other production file.
- Only test-project files and (if absolutely required) test-project package references are modified.

## Non-Functional Requirements

### NFR-1: Performance
- Each test runs in under 100 ms; full new test class runs in under 1 second.
- Tests must be fully in-memory — no database, no HTTP, no file I/O.

### NFR-2: Security
- No real credentials, connection strings, or tokens appear in test fixtures.
- The exception-handling test must affirmatively assert the response message does **not** leak exception details (defense in depth against information disclosure).

### NFR-3: Reliability and isolation
- Tests must be deterministic and independent: no shared mutable state, no ordering dependencies, no reliance on wall-clock time beyond a fixed `DateTime` value passed into the request.
- Tests must pass under `dotnet test` both locally and in CI.

### NFR-4: Maintainability
- Use the test stack already established in the repository's backend unit-test project (xUnit + FluentAssertions + the project's existing mocking library — Moq or NSubstitute, whichever the surrounding tests use).
- No new mocking library introduced for this task.
- Test names describe behavior, not implementation, per `csharp-testing.md`.

### NFR-5: Coverage contribution
- The four tests must cover all four branches of `ProcessDailyConsumptionHandler.Handle`: the `!result.WasRun` early return, the `MaterialsProcessed > 0` message arm, the `MaterialsProcessed == 0` message arm, and the `catch` block.
- After the change, branch coverage of `ProcessDailyConsumptionHandler` must be 100%.

## Data Model
No data-model changes. Tests interact only with the existing in-memory types:

- `ProcessDailyConsumptionRequest` — input carrying `ProcessingDate`.
- `ProcessDailyConsumptionResponse` — output carrying `Success`, `ProcessedDate`, `MaterialsProcessed`, `Message`.
- The result type returned by `IConsumptionCalculationService.ProcessDailyConsumptionAsync` — exposing `WasRun` and `MaterialsProcessed`.

## API / Interface Design
No API changes. Tests exercise the MediatR handler directly by invoking `Handle(request, cancellationToken)`; they do not go through the MediatR pipeline or HTTP.

The collaborator surface mocked in each test:

```
IConsumptionCalculationService
  Task<{result with WasRun, MaterialsProcessed}> ProcessDailyConsumptionAsync(
      DateTime processingDate,
      CancellationToken cancellationToken)
```

A captured/mocked `ILogger<ProcessDailyConsumptionHandler>` is wired in for verification in the exception-path test (info-level logs may be ignored).

## Dependencies
- Existing backend unit-test project (xUnit-based).
- FluentAssertions (already in use across the repo).
- The repo's established mocking library (Moq or NSubstitute — pick whichever the neighboring PackingMaterials handler tests use; do not introduce a second one).
- `Microsoft.Extensions.Logging.Abstractions` for an `ILogger<>` test double (already transitively available).

No new NuGet packages should be required.

## Out of Scope
- Integration tests, end-to-end tests, or HTTP-level tests for this handler.
- Tests for `IConsumptionCalculationService` itself or any downstream collaborator (invoice fetch, material repository, persistence).
- Refactoring the handler — e.g., extracting message strings, returning typed result objects, surfacing exception detail differently.
- Changes to logging structure or log-message text.
- Localization of response messages.
- Performance/load tests.
- Adding feature-flag gating to the handler.
- Backfilling tests for unrelated PackingMaterials handlers.

## Open Questions
None.

## Status: COMPLETE