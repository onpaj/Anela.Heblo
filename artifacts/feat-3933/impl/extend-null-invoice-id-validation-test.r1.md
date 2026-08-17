# Implementation: extend-null-invoice-id-validation-test

## What was implemented
Extended the existing `Handle_EmptyOrWhitespaceInvoiceId_ReturnsValidationError` theory in `GetIssuedInvoiceDetailHandlerTests` with an `[InlineData(null)]` case, so `string.IsNullOrWhiteSpace(null)` validation path in `GetIssuedInvoiceDetailHandler` is exercised alongside the existing empty-string and whitespace-only cases. No production code or test method body changes were needed — the existing assertions (`Success == false`, `ErrorCode == ErrorCodes.ValidationError`, `Invoice == null`, `_repositoryMock.VerifyNoOtherCalls()`) already cover the null case correctly.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs` — added `[InlineData(null)]` to the `Handle_EmptyOrWhitespaceInvoiceId_ReturnsValidationError` theory attribute list (line 37).

## Tests
`GetIssuedInvoiceDetailHandlerTests.Handle_EmptyOrWhitespaceInvoiceId_ReturnsValidationError` now covers three cases: `""`, `"   "`, and `null`.

Ran:
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetIssuedInvoiceDetailHandlerTests.Handle_EmptyOrWhitespaceInvoiceId_ReturnsValidationError"
```

Result:
```
Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 5 ms - Anela.Heblo.Tests.dll (net8.0)
```

Note: the build emitted a pre-existing analyzer warning `xUnit1012: Null should not be used for type parameter 'invoiceId' of type 'string'` on the new `[InlineData(null)]` line — this is expected and matches the task spec's stated intent (exercising the null path despite the non-nullable `string` parameter declaration); it does not fail the build or the test run.

## How to verify
```bash
cd backend/test/Anela.Heblo.Tests
dotnet test Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetIssuedInvoiceDetailHandlerTests.Handle_EmptyOrWhitespaceInvoiceId_ReturnsValidationError"
```
Expect 3 tests passed, 0 failed.

## Notes
No deviations from the task spec. Only the target test file was modified and committed; `artifacts/feat-3933/state.json` had unrelated pending changes in the worktree which were left untouched, per instructions, for the pipeline to manage.

## Status
DONE
