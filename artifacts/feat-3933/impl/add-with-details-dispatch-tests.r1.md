# Implementation: add-with-details-dispatch-tests

## What was implemented
Added two `[Fact]` test methods to `GetIssuedInvoiceDetailHandlerTests` covering FR-2:
the repository dispatch behavior based on `GetIssuedInvoiceDetailRequest.WithDetails`.

- `Handle_WithDetailsTrue_CallsGetByIdWithSyncHistoryAsync` — asserts that when
  `WithDetails == true`, the handler calls `IIssuedInvoiceRepository.GetByIdWithSyncHistoryAsync`
  exactly once and never calls `GetByIdAsync`, and that the response is a successful,
  mapped `IssuedInvoiceDetailDto`.
- `Handle_WithDetailsFalse_CallsGetByIdAsync` — asserts the inverse: when
  `WithDetails == false`, the handler calls `GetByIdAsync` exactly once and never calls
  `GetByIdWithSyncHistoryAsync`, with the same successful/mapped-response assertions.

Both tests were inserted verbatim as specified in the task context, directly after the
existing `Handle_EmptyOrWhitespaceInvoiceId_ReturnsValidationError` test method and before
the closing brace of the test class.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs` — added the two new `[Fact]` methods described above.

## Tests
- `GetIssuedInvoiceDetailHandlerTests.Handle_WithDetailsTrue_CallsGetByIdWithSyncHistoryAsync`
- `GetIssuedInvoiceDetailHandlerTests.Handle_WithDetailsFalse_CallsGetByIdAsync`

Ran the full test class:

```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetIssuedInvoiceDetailHandlerTests"
```

Result: `Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5` (3 pre-existing validation
theory cases + 2 new dispatch tests). No production code changes were required — the
handler already implements the described dispatch behavior.

## How to verify
Run the command above; expect all 5 tests in the class to pass.

## Notes
No deviations from the task-context spec. The pre-existing xUnit1012 analyzer warning on
line 37 (nullable `InlineData` for the `Theory`) is unrelated to this task and was left
untouched per the surgical-changes rule.

## PR Summary
Added two unit tests to `GetIssuedInvoiceDetailHandlerTests` that pin the
`WithDetails`-based repository dispatch behavior in `GetIssuedInvoiceDetailHandler`:
`WithDetails = true` must call `GetByIdWithSyncHistoryAsync` (never `GetByIdAsync`), and
`WithDetails = false` must call `GetByIdAsync` (never `GetByIdWithSyncHistoryAsync`). Both
verify a successful, mapped response. No production code changes were needed.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs` — added `Handle_WithDetailsTrue_CallsGetByIdWithSyncHistoryAsync` and `Handle_WithDetailsFalse_CallsGetByIdAsync`

## Status
DONE
