# Specification: Unit Test Coverage for InvoiceClassificationService.ClassifyInvoiceAsync

## Summary
Add direct unit tests for `InvoiceClassificationService.ClassifyInvoiceAsync` covering all four outcome paths (no rule matched, rule matched + ABRA success, rule matched + ABRA failure, exception during classification) and verifying that `RecordClassificationHistory` is invoked correctly on every path. This closes a critical coverage gap in the automated invoice routing pipeline where a regression could silently misclassify or fail to record received invoices.

## Background
`InvoiceClassificationService` is the core decision component for automated invoice routing at Anela Heblo. It evaluates classification rules against incoming invoices and pushes the resulting accounting template and department assignment to ABRA Flexi. The service is currently untested at the unit level — the only existing test class, `ClassifyInvoicesHandlerTests`, mocks `IInvoiceClassificationService` entirely, so `ClassifyInvoiceAsync` has never been exercised by the test suite.

This gap was flagged by the weekly coverage-gap routine on 2026-06-08 (CI run #27104028537, commit `6568feba`). Given that the service decides the accounting template for every received invoice and writes to a history audit table, an untested regression could silently corrupt accounting data or leave invoices stuck in manual review without alerting.

## Functional Requirements

### FR-1: Test the "no matching rule" outcome path
Verify that when `IRuleEvaluationEngine` returns no matching rule for an invoice, `ClassifyInvoiceAsync`:
1. Calls `IInvoiceClassificationsClient.MarkInvoiceForManualReviewAsync` with the correct invoice identifier.
2. Calls `RecordClassificationHistory` with status `ManualReviewRequired`.
3. Returns a result indicating manual review is required (no rule ID, no accounting template).

**Acceptance criteria:**
- Test method `ClassifyInvoiceAsync_NoMatchingRule_MarksForManualReviewAndRecordsHistory` exists.
- Mocked `IRuleEvaluationEngine.EvaluateAsync` returns "no match" (null or equivalent).
- `IInvoiceClassificationsClient.MarkInvoiceForManualReviewAsync` is verified called exactly once with the invoice ID under test.
- `IClassificationHistoryRepository.AddAsync` (or whichever method `RecordClassificationHistory` delegates to) is verified called with a history entry whose status equals `ManualReviewRequired`.
- The returned result reflects manual-review state (assert on returned DTO fields).

### FR-2: Test the "rule matched, ABRA update succeeds" outcome path
Verify that when a rule matches and the ABRA client returns success, `ClassifyInvoiceAsync`:
1. Calls `IInvoiceClassificationsClient` to update the invoice with the rule's accounting template and department.
2. Records history with status `Success`.
3. Returns a result containing the matched rule ID and accounting template.

**Acceptance criteria:**
- Test method `ClassifyInvoiceAsync_RuleMatchedAndAbraSucceeds_RecordsSuccessAndReturnsRuleResult` exists.
- Mocked `IRuleEvaluationEngine` returns a matched rule with a known ID and template.
- Mocked `IInvoiceClassificationsClient` update method returns `success == true`.
- History repository is verified called with status `Success`.
- The returned result's rule ID and accounting template equal the rule's values.

### FR-3: Test the "rule matched, ABRA update fails" outcome path
Verify that when a rule matches but the ABRA client returns failure, `ClassifyInvoiceAsync`:
1. Looks up the rule name via `IClassificationRuleRepository.GetByIdAsync` for display purposes.
2. Records history with status `Error`.
3. Returns an error result containing the rule ID (so the UI can display which rule was attempted).

**Acceptance criteria:**
- Test method `ClassifyInvoiceAsync_RuleMatchedAndAbraFails_RecordsErrorAndReturnsRuleIdForDisplay` exists.
- Mocked `IRuleEvaluationEngine` returns a matched rule.
- Mocked `IInvoiceClassificationsClient` update method returns `success == false`.
- `IClassificationRuleRepository.GetByIdAsync` is verified called with the matched rule's ID.
- History repository is verified called with status `Error`.
- The returned result indicates error state and contains the rule ID.

### FR-4: Test the "exception during classification" outcome path
Verify that when an exception is thrown during classification (e.g., by `IRuleEvaluationEngine` or `IInvoiceClassificationsClient`), `ClassifyInvoiceAsync`:
1. Catches the exception (does not propagate to the caller).
2. Records history with status `Error` and the exception message.
3. Returns an error result.

**Acceptance criteria:**
- Test method `ClassifyInvoiceAsync_ExceptionThrown_RecordsErrorWithMessageAndReturnsErrorResult` exists.
- A dependency mock is configured to throw a known exception with a known message.
- History repository is verified called with status `Error` and a history entry whose error message equals (or contains) the thrown exception's message.
- The returned result indicates error state.
- The test asserts no exception is thrown out of `ClassifyInvoiceAsync`.

### FR-5: Verify `RecordClassificationHistory` invocation on every path
Each test (FR-1 through FR-4) must include an assertion that the history recording side effect occurred. This is currently never asserted anywhere in the suite.

**Acceptance criteria:**
- Every test in FR-1 through FR-4 calls `Verify` (or equivalent) on the history repository mock with the expected status and payload.
- Test failure messages clearly identify which path's history recording broke if the assertion fails.

### FR-6: Follow existing test conventions
Tests must match the conventions of the surrounding suite (e.g., `ClassifyInvoicesHandlerTests`) so they are recognized as part of the standard backend test suite and run on every PR build.

**Acceptance criteria:**
- New test class lives in the matching test project under the same folder structure as `ClassifyInvoicesHandlerTests`.
- Test class name is `InvoiceClassificationServiceTests`.
- Uses the same mocking library, assertion library, and DI patterns as `ClassifyInvoicesHandlerTests`.
- Tests follow Arrange-Act-Assert structure.
- `dotnet build` and `dotnet test` pass cleanly with no new warnings.

## Non-Functional Requirements

### NFR-1: Performance
- The new tests must run in under 500ms total wall-clock time. They are pure unit tests with all dependencies mocked — there should be no I/O, no database, no HTTP calls.
- The new tests must not slow down the existing CI pipeline meaningfully (target: < 1 second added to total test suite runtime).

### NFR-2: Security
- No real credentials, connection strings, or external service URLs in test code.
- Test data (invoice numbers, rule IDs, templates) must be synthetic — not copied from production.

### NFR-3: Maintainability
- Tests must be isolated: each test sets up its own mocks and does not depend on test execution order.
- Test names must describe the scenario under test in `MethodName_StateUnderTest_ExpectedBehavior` form.
- Mock setup should use shared helpers only when the helper genuinely reduces duplication across 2+ tests; avoid premature abstraction.

### NFR-4: Coverage
- After these tests are added, line and branch coverage of `InvoiceClassificationService.ClassifyInvoiceAsync` should reach ≥ 90% (target: 100% for the four outcome paths).
- Coverage of `RecordClassificationHistory` invocations should be 100% (called from every path).

## Data Model
No data model changes. Tests interact with existing types:
- `InvoiceClassificationService` (system under test)
- `IClassificationRuleRepository` (mocked)
- `IRuleEvaluationEngine` (mocked)
- `IInvoiceClassificationsClient` (mocked)
- `IClassificationHistoryRepository` (mocked)
- `ICurrentUserService` (mocked)
- The existing `ClassificationHistoryStatus` enum values: `Success`, `Error`, `ManualReviewRequired`
- The existing invoice and rule DTOs/entities used by the service

## API / Interface Design
No production API or interface changes. The deliverable is a new test class:

```
backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Services/InvoiceClassificationServiceTests.cs
```

(Exact path must match the test project's existing layout — confirm against `ClassifyInvoicesHandlerTests` location.)

The test class will contain at minimum the four test methods named in FR-1 through FR-4, plus any private helpers needed to construct mocked dependencies and the SUT.

## Dependencies
- Existing test project for the `InvoiceClassification` feature (where `ClassifyInvoicesHandlerTests` lives).
- Existing mocking library used by the backend test suite (likely Moq, NSubstitute, or FakeItEasy — match what `ClassifyInvoicesHandlerTests` uses).
- Existing assertion library (likely FluentAssertions or xUnit's built-in `Assert`).
- `InvoiceClassificationService` source remains unchanged — this is a pure test addition.

## Out of Scope
- Refactoring `InvoiceClassificationService` itself. Tests are added against the current implementation as-is.
- Integration tests, E2E tests, or tests against a real ABRA Flexi instance.
- Adding new test coverage for `ClassifyInvoicesHandler` or other components in the classification feature.
- Modifying `IRuleEvaluationEngine`, `IInvoiceClassificationsClient`, or any other dependency surface.
- Changing the `ClassificationHistoryStatus` enum or history schema.
- Performance, load, or stress tests.
- Updating any documentation beyond what is implied by code changes.

## Open Questions
None.

## Status: COMPLETE