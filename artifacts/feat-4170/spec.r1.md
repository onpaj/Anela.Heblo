# Specification: Unit test coverage for DailyInvoiceImportJobBase

## Summary
`DailyInvoiceImportJobBase` (the shared base class for the EUR and CZK daily invoice import Hangfire jobs) currently has only 20.0% line coverage against a 60% threshold. Three of its four execution paths — the disabled-job short-circuit, the partial-failure warning branch, and the exception-rethrow branch — have no test coverage at all. This work adds focused unit tests for the base class's `ExecuteAsync` method, using a minimal test double derived class, to close the coverage gap and lock in the job's documented safety behavior (no live calls when disabled, and errors always propagate to Hangfire).

## Background
`DailyInvoiceImportJobBase` is an `IRecurringJob` implementation registered twice (`DailyInvoiceImportEurJob`, `DailyInvoiceImportCzkJob`) via Hangfire's recurring job scheduler. On each run it:
1. Checks `IRecurringJobStatusChecker.IsJobEnabledAsync` and returns immediately, doing nothing else, if the job is disabled.
2. Otherwise calls `IInvoiceImportService.ImportInvoicesAsync` for "yesterday" in the job's configured currency.
3. Logs a warning (but does not throw or return early) when the import result contains any `Failed` entries.
4. Catches any exception from the import call, logs it, and rethrows so Hangfire can record the run as failed and apply its own retry policy.

A weekly automated coverage-gap scan flagged this file (`backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/Jobs/DailyInvoiceImportJobBase.cs`) at 20.0% line coverage, filed as GitHub issue #4170. No existing test file covers this class or its two derived jobs (`backend/test/Anela.Heblo.Tests/Features/Invoices/` has no `Infrastructure/Jobs/` subfolder today). An equivalent, already-tested sibling exists at `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/BankImportJobBase.cs`, covered by `backend/test/Anela.Heblo.Tests/Features/Bank/Infrastructure/Jobs/BankImportJobBaseTests.cs`, which establishes the codebase's convention for testing an abstract recurring-job base class: instantiate it through a private nested test-double subclass and drive it through mocked dependencies.

This is a pure test-authoring task — no production code changes are required or expected. The three behaviors under test are already implemented correctly; the goal is to verify and pin them down.

## Functional Requirements

### FR-1: Disabled-job short-circuit is verified
When `IRecurringJobStatusChecker.IsJobEnabledAsync(Metadata.JobName, ...)` returns `false`, `ExecuteAsync` must return without calling `IInvoiceImportService.ImportInvoicesAsync`.

**Acceptance criteria:**
- A test sets up the status-checker mock to return `false` for the job's name.
- After calling `ExecuteAsync`, the test asserts `IInvoiceImportService.ImportInvoicesAsync` was never invoked (`Moq` `Times.Never`).
- The call completes without throwing.

### FR-2: Partial-failure branch logs a warning and completes normally
When the import service returns an `ImportResultDto` whose `Failed` list is non-empty, `ExecuteAsync` must not throw, must complete the run, and must emit a warning-level log entry.

**Acceptance criteria:**
- A test sets up the status checker to return `true` (enabled) and the import service to return an `ImportResultDto` with at least one entry in `Failed` (and, to distinguish this path from the happy path, ideally also at least one entry in `Succeeded`).
- The test asserts `ExecuteAsync` completes without throwing.
- The test asserts a warning was logged — either via an injected `ILogger`/`ILoggerFactory` test double capturing log calls at `LogLevel.Warning`, or via `Moq`'s `ILogger.Log` verification pattern (`logger.Verify(x => x.Log(LogLevel.Warning, ...), Times.Once)`), consistent with how logging is verified elsewhere in this codebase (check for an existing pattern before introducing a new one; if none exists, a `Mock<ILogger>` injected through a `Mock<ILoggerFactory>.Setup(f => f.CreateLogger(...)).Returns(...)` is acceptable).
- A companion/contrasting case (all-success, empty `Failed`) is include-able but not required — FR-1's mock defaults already establish the happy path implicitly through the other tests' arrange sections; a dedicated "no failures ⇒ no warning logged" assertion is a nice-to-have, not required for closing this coverage gap, and should be added only if it does not meaningfully increase task size.

### FR-3: Exception from the import service propagates (rethrow path)
When `IInvoiceImportService.ImportInvoicesAsync` throws, `ExecuteAsync` must log an error and rethrow the same (or an equivalent) exception rather than swallowing it.

**Acceptance criteria:**
- A test sets up the status checker to return `true` and the import service mock to throw (e.g. `ThrowsAsync(new InvalidOperationException(...))`).
- The test asserts that calling `ExecuteAsync` throws that exception type (FluentAssertions `await act.Should().ThrowAsync<InvalidOperationException>()`, matching the convention in `BankImportJobBaseTests.ExecuteAsync_DoesNotWriteState_AndRethrows_WhenHandlerThrows`).

### FR-4: Test double for the abstract base class
Because `DailyInvoiceImportJobBase` is `abstract` with an abstract `Metadata` property and an abstract `Currency` property, the tests need a minimal concrete subclass to instantiate it.

**Acceptance criteria:**
- A private nested test class (e.g. `TestDailyInvoiceImportJob`) derives from `DailyInvoiceImportJobBase`, supplies a fixed `Metadata` (arbitrary `JobName`/`DisplayName`/`Description`/`CronExpression`/`DefaultIsEnabled`) and a fixed `Currency` (e.g. `"EUR"`), and forwards constructor parameters to the base constructor — mirroring `BankImportJobBaseTests.TestBankImportJob`.
- Alternatively/additionally, the existing derived classes (`DailyInvoiceImportEurJob`, `DailyInvoiceImportCzkJob`) may be exercised directly instead of (or in addition to) a test double, per the issue's suggested approach ("unit tests for each derived job class (or the base class with a mock import service)"); the nested-test-double approach is preferred as it matches the codebase's established convention (`BankImportJobBaseTests`) and avoids duplicating the same three tests across two job classes.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — these are unit tests with mocked dependencies; no timing constraints beyond the standard fast-unit-test expectation (milliseconds per test, no real I/O, no real `Task.Delay`/sleep).

### NFR-2: Security
Not applicable — no new production code, no new secrets, no new external calls. Tests must not perform any real Shoptet/ABRA Flexi network calls (the existing design already avoids this since `IInvoiceImportService` is mocked).

## Data Model
No data model changes. Relevant existing types used by the tests (read-only, not modified):
- `Anela.Heblo.Application.Features.Invoices.Infrastructure.Jobs.DailyInvoiceImportJobBase` (system under test)
- `Anela.Heblo.Application.Features.Invoices.Services.IInvoiceImportService` — single method `Task<ImportResultDto> ImportInvoicesAsync(string description, IssuedInvoiceSourceQuery query, CancellationToken cancellationToken = default)`
- `Anela.Heblo.Application.Features.Invoices.Contracts.ImportResultDto` — `RequestId: string`, `Succeeded: List<string>`, `Failed: List<string>`
- `Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceSourceQuery` — `RequestId`, `DateFrom`, `DateTo`, `Currency`
- `Anela.Heblo.Domain.Features.BackgroundJobs.IRecurringJobStatusChecker` — `Task<bool> IsJobEnabledAsync(string jobName, CancellationToken cancellationToken = default, bool defaultIfMissing = true)`
- `Anela.Heblo.Domain.Features.BackgroundJobs.RecurringJobMetadata` — used to populate the test double's `Metadata`
- `Anela.Heblo.Domain.Features.BackgroundJobs.IRecurringJob` — the interface `DailyInvoiceImportJobBase` implements (`ExecuteAsync`)

## API / Interface Design
No API changes. No new public interfaces. The only new artifact is a test class (and its private nested test-double type) under `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/Jobs/`.

## Dependencies
- xUnit, Moq, FluentAssertions — already used project-wide and in the directly analogous `BankImportJobBaseTests.cs`.
- `Microsoft.Extensions.Logging.Abstractions` (`NullLoggerFactory`) for tests that don't need to assert on log content; a `Mock<ILoggerFactory>`/`Mock<ILogger>` pair only for the one test (FR-2) that needs to assert a warning was logged.
- No new NuGet packages required.

## Out of Scope
- Any change to `DailyInvoiceImportJobBase.cs`, `DailyInvoiceImportEurJob.cs`, or `DailyInvoiceImportCzkJob.cs` production code — this is coverage-only.
- Testing `IInvoiceImportService`'s own internals (already covered by `InvoiceImportServiceTests.cs` and related files).
- Integration/E2E-level testing of the Hangfire scheduling itself (covered elsewhere, e.g. `HangfireRecurringJobSchedulerTests.cs`, `RecurringJobDiscoveryServiceTests.cs`).
- Raising the coverage threshold or changing CI coverage-gate configuration.
- Testing the "happy path" (no failures, successful import) as a dedicated separate case beyond what's incidentally exercised while arranging FR-1/FR-2/FR-3 — the coverage gap explicitly named in the issue is the disabled guard, the partial-failure branch, and the exception-rethrow branch, not the already-covered-by-line-execution happy path.

## Open Questions
None.

## Status: COMPLETE
