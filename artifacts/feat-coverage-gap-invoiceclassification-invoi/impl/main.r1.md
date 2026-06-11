All tasks complete. Here is the output summary:

---

# Implementation: Unit Test Coverage for InvoiceClassificationService.ClassifyInvoiceAsync

## What was implemented

Four xUnit `[Fact]` tests covering every outcome path of `ClassifyInvoiceAsync`:
1. No rule matched → manual review + history recorded
2. Rule matched, ABRA update succeeds → success result + history recorded
3. Rule matched, ABRA update fails → error result with rule ID (AccountingTemplateCode null on DTO, present on history — matches SUT asymmetry) + history recorded
4. Exception thrown → caught, error result with exception message + history recorded

All tests use `Callback`-captured `ClassificationHistory` objects for diff-friendly failure messages on history assertions (FR-5), and follow the exact conventions of `ClassifyInvoicesHandlerTests`.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationServiceTests.cs` — 340-line test class with four `[Fact]` methods, six field-level Moq mocks, and SUT constructed in the xUnit constructor

## Tests

- `InvoiceClassificationServiceTests` — 4/4 passing, 65 ms total. Covers all four outcome paths of `ClassifyInvoiceAsync` including history recording verification on every path (FR-1 through FR-5).

## How to verify

```bash
cd backend/test/Anela.Heblo.Tests
dotnet test --filter "FullyQualifiedName~InvoiceClassificationServiceTests"
# Expected: Passed: 4, Failed: 0
```

## Notes

Two spec amendments were applied per the arch review:
- `EvaluateAsync` → `FindMatchingRule` (synchronous, not async)
- `ClassificationHistoryStatus` enum → `ClassificationResult` (actual enum name)
- `GetByIdAsync` assertion removed from FR-3 (SUT does not call it on ABRA-failure path)
- File placed flat under `Features/InvoiceClassification/` (no `Services/` subfolder) to match existing sibling test class layout

The 38 pre-existing integration test failures in the full suite are Testcontainers PostgreSQL failures (Docker not available in this environment) — unrelated to this change.

## PR Summary

Added four unit tests for `InvoiceClassificationService.ClassifyInvoiceAsync` to close a critical coverage gap in the automated invoice routing pipeline. The service previously had zero unit test coverage despite being the core decision component that writes to the accounting history audit table on every processed invoice.

Tests cover: no-rule-matched → manual review, rule matched + ABRA success, rule matched + ABRA failure (with the documented DTO/history asymmetry on `AccountingTemplateCode`), and exception catch. Every test asserts that `RecordClassificationHistory` was invoked with the correct `ClassificationResult` status, using Callback-captured history objects for clear assertion diffs.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationServiceTests.cs` — new file: 340-line xUnit test class, 4 Fact methods, Moq + FluentAssertions, matching conventions of sibling `ClassifyInvoicesHandlerTests`

## Status
DONE