# Implementation: write-dto-settability-test

## What was implemented
Added the RED-step regression test that requires `BankStatementImportDto.ErrorType`
to have a public setter, as specified in the task context. Against the
current (unmodified) DTO this fails to *compile* rather than fails at
runtime, which is the expected failing signal for this refactor since the
settable member does not exist yet.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs` —
  added a new `[Fact]`, `BankStatementImportDto_ErrorType_Is_Settable_Independently_Of_The_Mapper`,
  immediately after the existing `Map_BankStatementImport_To_Dto_When_ImportResult_Is_Not_OK_Sets_ErrorType_To_ImportResult`
  method, exactly as given in the task context (object-initializer setting
  `ErrorType = "SOME_ERROR"`, then asserting it round-trips).

## Tests
- `BankMappingProfileTests.BankStatementImportDto_ErrorType_Is_Settable_Independently_Of_The_Mapper`
  (new) — constructs `BankStatementImportDto` directly via object initializer
  and asserts `ErrorType` round-trips. This currently fails to **compile**
  (by design — see below), so it is not yet a "passing" test; it becomes
  green once the next task (`make-errortype-settable-and-mapped`) makes
  `ErrorType` a settable auto-property.

## How to verify
```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Confirmed: build **fails** with
`error CS0200: Property or indexer 'BankStatementImportDto.ErrorType' cannot be assigned to -- it is read only`
at `BankMappingProfileTests.cs(66,13)`, exactly the error signature the task
context specified as the expected RED result. No other errors were
introduced (only pre-existing nullable-reference warnings unrelated to this
change appear in the same build output).

The failing test was committed on its own, per the task context's Step 3:
```
git commit -m "test(bank): add failing test proving BankStatementImportDto.ErrorType is not yet settable"
```

## Notes
No deviations from the task context. This task intentionally leaves the
solution non-buildable for the DTO/mapper production code path (only the
test project was touched) — the next task
(`make-errortype-settable-and-mapped`) makes the property settable and adds
the corresponding `ForMember` mapping rule, turning this test GREEN.

## PR Summary
Added a failing regression test proving `BankStatementImportDto.ErrorType`
is not yet settable, as the RED step of the TDD refactor for GitHub issue
#4279 (moving `ErrorType`'s derivation out of the DTO and into
`BankMappingProfile`). The test project intentionally fails to compile
against the current DTO with `CS0200`, confirming it exercises the
not-yet-implemented behavior; the next task in the plan makes it pass.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs` —
  added `BankStatementImportDto_ErrorType_Is_Settable_Independently_Of_The_Mapper`

## Status
DONE
