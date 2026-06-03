All validation checks pass. Here is the output summary:

---

# Implementation: Remove Dead AutoMapper `ForMember` for `BankStatementImportDto.ErrorType`

## What was implemented

Removed the no-op `.ForMember(dest => dest.ErrorType, ...)` from `BankMappingProfile` and added a profile-level unit test class to lock in the preserved mapping behaviour.

`BankStatementImportDto.ErrorType` is a get-only computed property — AutoMapper silently ignores `ForMember` calls targeting read-only members. The DTO getter is now the single source of truth.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Bank/BankMappingProfile.cs` — ForMember chain dropped; bare `CreateMap<BankStatementImport, BankStatementImportDto>()` remains
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs` — new xUnit test class; 3 facts: `AssertConfigurationIsValid`, `ImportResult="OK"→ErrorType=null`, `ImportResult="Failed"→ErrorType="Failed"` using a real `IMapper`

## Tests

`BankMappingProfileTests.cs` — 3 tests, all passing. Covers profile validity guard and both ErrorType derivation paths.

46/46 Bank slice tests pass. Full solution builds clean (0 errors, 0 warnings).

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Bank"
git diff -- frontend/src/api-client/   # empty — no OpenAPI drift
```

## Notes

The test file follows the project convention (`NullLoggerFactory.Instance` passed to `MapperConfiguration`), consistent with `InvoiceClassificationMappingProfileTests`. `BankStatementImportDto.cs`, `BankStatementImport.cs`, and `ImportStatus.cs` are untouched.

## PR Summary

Removed the dead `.ForMember(dest => dest.ErrorType, ...)` AutoMapper configuration in `BankMappingProfile`. AutoMapper silently ignores `ForMember` calls targeting read-only destination properties, so the rule had no runtime effect. `BankStatementImportDto.ErrorType` (a get-only computed property) already encoded the identical derivation logic, making the mapper rule both redundant and misleading. The DTO getter is now the single source of truth.

A new `BankMappingProfileTests` class verifies mapping configuration validity and preserves the `ErrorType` derivation contract, ensuring the change is behaviour-safe and any future setter addition would be caught immediately.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Bank/BankMappingProfile.cs` — ForMember removed; bare CreateMap retained
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs` — new profile-level test class (3 facts, real IMapper)

## Status
DONE