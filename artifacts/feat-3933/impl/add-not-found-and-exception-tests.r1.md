# Implementation: add-not-found-and-exception-tests

## What was implemented
Added two `[Fact]` test methods to `GetIssuedInvoiceDetailHandlerTests` covering FR-3 and
FR-4: the handler's not-found and outer-exception error paths.

- `Handle_InvoiceNotFound_ReturnsResourceNotFoundError` — asserts that when the repository
  returns `null` for `GetByIdAsync`, the handler returns an unsuccessful response with
  `ErrorCodes.ResourceNotFound`, a null `Invoice`, the `ErrorMessage` param set to
  `"Faktura nebyla nalezena"`, and that the mapper is never invoked.
- `Handle_RepositoryThrows_ReturnsExceptionError` — asserts that when the repository throws
  an `InvalidOperationException`, the handler catches it and returns an unsuccessful
  response with `ErrorCodes.Exception`, a null `Invoice`, and the `ErrorMessage` param set
  to `"Chyba při načítání detailu faktury"` (no rethrow).

Both tests were inserted verbatim as specified in the task context, directly after the
existing `Handle_WithDetailsFalse_CallsGetByIdAsync` test method and before the closing
brace of the test class.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs` — added the two new `[Fact]` methods described above.

## Tests
- `GetIssuedInvoiceDetailHandlerTests.Handle_InvoiceNotFound_ReturnsResourceNotFoundError`
- `GetIssuedInvoiceDetailHandlerTests.Handle_RepositoryThrows_ReturnsExceptionError`

Ran the full test class:

```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetIssuedInvoiceDetailHandlerTests"
```

Result: `Passed! - Failed: 0, Passed: 7, Skipped: 0, Total: 7` (3 pre-existing validation
theory cases + 2 dispatch tests + the 2 new not-found/exception tests). No production code
changes were required — the handler already implements the described error-handling
behavior.

Also ran `dotnet build Anela.Heblo.sln` (0 errors, only pre-existing warnings) and
`dotnet format Anela.Heblo.sln --verify-no-changes --include backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs`
(no formatting violations reported).

## How to verify
Run the command above; expect all 7 tests in the class to pass.

## Notes
No deviations from the task-context spec. The pre-existing xUnit1012 analyzer warning on
line 37 (nullable `InlineData` for the `Theory`) is unrelated to this task and was left
untouched per the surgical-changes rule.

## PR Summary
Added two unit tests to `GetIssuedInvoiceDetailHandlerTests` that pin the not-found and
outer-exception error paths of `GetIssuedInvoiceDetailHandler`: a `null` repository result
must produce `ErrorCodes.ResourceNotFound` with the mapper never invoked, and a thrown
`InvalidOperationException` must be caught and produce `ErrorCodes.Exception` rather than
propagating. No production code changes were needed.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs` — added `Handle_InvoiceNotFound_ReturnsResourceNotFoundError` and `Handle_RepositoryThrows_ReturnsExceptionError`

## Status
DONE
