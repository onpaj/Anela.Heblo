# Implementation: fix-classification-history-call-site

## What was implemented
Changed `InvoiceClassificationService.RecordClassificationHistory` so the `ClassificationHistory` audit row's `abraInvoiceId` constructor argument is now `invoice.AbraInvoiceId` instead of `invoice.InvoiceNumber`. Previously both the `abraInvoiceId` and `invoiceNumber` arguments were passed the same value (`invoice.InvoiceNumber`), so every history row had `AbraInvoiceId == InvoiceNumber`. With `ReceivedInvoice.AbraInvoiceId` now populated from FlexiBee's internal `Id` (previous two tasks in this plan), the two fields carry genuinely distinct values.

All other lines in the file are unchanged. In particular, `ClassifyInvoiceAsync`'s outbound FlexiBee calls (`MarkInvoiceForManualReviewAsync`, `UpdateInvoiceClassificationAsync`) correctly continue to use `invoice.InvoiceNumber` (FlexiBee's `Code`), since that is explicitly out of scope per the spec.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/InvoiceClassificationService.cs` — one-line change: first `ClassificationHistory` constructor argument changed from `invoice.InvoiceNumber` to `invoice.AbraInvoiceId`.

## Tests
No new tests added by this task — it makes the four tests already updated by `update-invoice-classification-service-tests-for-abra-invoice-id` pass. Verified: `InvoiceClassificationServiceTests` (4/4 passed).

## How to verify
```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceClassificationServiceTests"
```
Expected: build succeeds with 0 errors, and `Passed: 4, Failed: 0`.

## Notes
- Full `InvoiceClassification`-namespace test run also executed: 94 passed, 3 failed — all 3 failures are pre-existing `ClassificationRuleRepositoryReorderIntegrationTests` Testcontainers/Postgres integration tests that fail because Docker is not available in this sandbox (`System.ArgumentException: Docker is either not running or misconfigured`), unrelated to this change. `InvoiceClassificationMappingProfileTests` and `ClassificationHistoryRepositoryTests` are unaffected, as predicted by the spec.
- `dotnet format Anela.Heblo.sln --verify-no-changes` reported no formatting changes needed — no extra diff to include.

## PR Summary
Fixes the root cause of the arch-review finding: `RecordClassificationHistory` was passing `invoice.InvoiceNumber` for both the `AbraInvoiceId` and `InvoiceNumber` fields of every `ClassificationHistory` audit row, making the `AbraInvoiceId` column always redundant. This change passes the newly-populated `invoice.AbraInvoiceId` (FlexiBee's internal invoice `Id`) for that field instead, so the two columns now carry their intended, distinct values.

### Changes
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/InvoiceClassificationService.cs` — `RecordClassificationHistory` now passes `invoice.AbraInvoiceId` instead of `invoice.InvoiceNumber` as the first `ClassificationHistory` constructor argument.

## Status
DONE
