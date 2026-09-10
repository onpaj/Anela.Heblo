# Implementation: update-invoice-classification-service-tests-for-abra-invoice-id

## What was implemented
Updated the four test methods in `InvoiceClassificationServiceTests.cs` (following TDD) to assert the
*correct* behavior for `ClassificationHistory.AbraInvoiceId` rather than the current buggy behavior.
Each test's `ReceivedInvoice` fixture now sets a distinct `AbraInvoiceId` value (`ABRA-00N`) separate
from its existing `InvoiceNumber` (`INV-00N`), and each test's assertion was changed from
`capturedHistory.AbraInvoiceId.Should().Be(invoice.InvoiceNumber)` to
`capturedHistory.AbraInvoiceId.Should().Be(invoice.AbraInvoiceId)`. No other assertions (including the
adjacent `capturedHistory.InvoiceNumber.Should().Be(invoice.InvoiceNumber)` lines) were touched, and the
service under test (`InvoiceClassificationService`) was not modified.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationServiceTests.cs` — added `AbraInvoiceId = "ABRA-00N"` to each of the 4 `ReceivedInvoice` fixtures and changed each `capturedHistory.AbraInvoiceId` assertion to compare against `invoice.AbraInvoiceId` instead of `invoice.InvoiceNumber`.

## Tests
`InvoiceClassificationServiceTests.cs` covers `InvoiceClassificationService.ClassifyInvoiceAsync` across four scenarios (no matching rule, rule matched + ABRA succeeds, rule matched + ABRA fails, exception thrown). Ran:

```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceClassificationServiceTests"
```

Result: **Failed! - Failed: 4, Passed: 0, Skipped: 0, Total: 4**. All four failures show the expected mismatch, e.g.:

```
Expected capturedHistory.AbraInvoiceId to be "ABRA-001" with a length of 8, but "INV-001" has a length of 7, differs near "INV" (index 0).
```

Same pattern (ABRA-002/003/004 vs INV-002/003/004) for the other three tests. This confirms the tests
now correctly exercise the known bug in `InvoiceClassificationService.RecordClassificationHistory`
(which still passes `invoice.InvoiceNumber` for both constructor arguments). No unexpected failures or
different failure counts were observed.

## How to verify
1. `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceClassificationServiceTests"`
2. Confirm 4 failures, each with an `AbraInvoiceId` mismatch as shown above.
3. `git show --stat HEAD` to confirm only the test file was committed.

## Notes
No deviations from the task spec. The service call site was intentionally left unfixed per the task
instructions — a later task will update `InvoiceClassificationService.RecordClassificationHistory` to
pass `invoice.AbraInvoiceId` instead of `invoice.InvoiceNumber` for the `AbraInvoiceId` argument, which
will make these four tests pass.

## PR Summary
Updates `InvoiceClassificationServiceTests` to assert the correct (distinct) `AbraInvoiceId` value per TDD, intentionally failing against the current call-site bug that conflates `AbraInvoiceId` with `InvoiceNumber`.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationServiceTests.cs` — added distinct `AbraInvoiceId` fixture values and updated assertions in all 4 test methods.

## Status
DONE
