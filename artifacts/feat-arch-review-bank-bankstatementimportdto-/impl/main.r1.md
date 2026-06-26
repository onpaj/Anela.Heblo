---

# Implementation: Replace `"OK"` literal with `ImportStatus.Success` in `BankStatementImportDto`

## What was implemented

Replaced the magic string `"OK"` in `BankStatementImportDto.ErrorType` with the existing `ImportStatus.Success` domain constant, and updated the two corresponding test assertions in `BankMappingProfileTests.cs` to use the same constant. Behavior is unchanged — `ImportStatus.Success` is `const string Success = "OK"`, so emitted IL is equivalent.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs` — added `using Anela.Heblo.Domain.Features.Bank;` and replaced `"OK"` with `ImportStatus.Success` in `ErrorType`
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs` — replaced the two `"OK"` literals (test setup and assertion in the success-branch test) with `ImportStatus.Success`

## Tests

- `BankMappingProfileTests.cs` — all 3 existing tests (`Profile_Configuration_IsValid`, `…_Is_OK_Sets_ErrorType_To_Null`, `…_Is_Not_OK_Sets_ErrorType_To_ImportResult`) pass after both edits
- Full Bank-module test scope passes with no regressions
- TS client: no diff under `frontend/src/api/` (no-op verified)

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Bank" --nologo
dotnet build backend/Anela.Heblo.sln --nologo
```

## Notes

No deviations. The second test's `ImportResult = "Failed"` was intentionally left as a literal per the surgical-changes rule — it is an arbitrary non-success value, not a named constant.

## PR Summary

Replaces the lone magic string `"OK"` in `BankStatementImportDto.ErrorType` with the existing `ImportStatus.Success` domain constant, closing a scattered-literal hazard where every other Bank-module consumer already uses the constant. Also updates the two test assertions in `BankMappingProfileTests` that previously compared against the literal, so they will stay correct if the constant is ever renormalized in the future.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs` — added `using Anela.Heblo.Domain.Features.Bank;` and replaced `"OK"` with `ImportStatus.Success` in the `ErrorType` expression
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs` — replaced two `"OK"` literals with `ImportStatus.Success` in the success-branch test setup and assertion

## Status
DONE