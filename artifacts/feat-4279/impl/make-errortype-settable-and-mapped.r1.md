# Implementation: make-errortype-settable-and-mapped

## What was implemented
Made `BankStatementImportDto.ErrorType` a plain settable auto-property
(removing the computed get-only expression body and the now-unused
`using Anela.Heblo.Domain.Features.Bank;` line), and added a `ForMember`
rule to `BankMappingProfile` that reproduces the exact same
success/failure derivation logic that used to live on the DTO. Both edits
were applied together, exactly as specified in the task context.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs` —
  `ErrorType` changed from `public string? ErrorType => ImportResult != ImportStatus.Success ? ImportResult : null;`
  to a plain `public string? ErrorType { get; set; }`; dropped the
  now-unused `using Anela.Heblo.Domain.Features.Bank;`.
- `backend/src/Anela.Heblo.Application/Features/Bank/BankMappingProfile.cs` —
  `CreateMap<BankStatementImport, BankStatementImportDto>()` now has a
  `.ForMember(dest => dest.ErrorType, opt => opt.MapFrom(src => src.ImportResult != ImportStatus.Success ? src.ImportResult : null))`
  rule, matching the task context's exact final file content.

## Tests
- `BankMappingProfileTests` (existing 3 + the new settability test from the
  prior task) — all 4 pass, including
  `BankStatementImportDto_ErrorType_Is_Settable_Independently_Of_The_Mapper`
  (now GREEN) and `Profile_Configuration_IsValid`.
- `GetBankStatementByIdHandlerTests` — all 4 pass unmodified, including
  `Handle_WithExistingId_ReturnsMappedDto` and
  `Handle_ProducesSameDtoAsListHandlerMapping_ForSameEntity`, confirming
  both existing consumers still derive `ErrorType` identically to before
  the refactor.
- Full `Features.Bank` scope — 119 passed, 8 failed. All 8 failures are
  pre-existing `BankStatementImportRepositoryIntegrationTests` cases that
  fail with `System.ArgumentException: Docker is either not running or
  misconfigured` (a Testcontainers/PostgreSQL fixture requiring Docker,
  unavailable in this environment) — an unrelated environmental issue, not
  a regression from this change. Per the task context's Step 6 guidance,
  noted but not fixed (out of scope).

## How to verify
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
# Build succeeded, 0 Error(s)

dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Bank.BankMappingProfileTests" --nologo
# Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4

dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Bank.GetBankStatementByIdHandlerTests" --nologo
# Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4

dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.Bank" --nologo
# Failed! - Failed: 8, Passed: 119 (all 8 failures are Docker/Testcontainers-related, pre-existing)

dotnet format Anela.Heblo.sln \
  --include backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs \
            backend/src/Anela.Heblo.Application/Features/Bank/BankMappingProfile.cs \
            backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs \
  --verify-no-changes
# exit 0, no diff
```

## Notes
- The solution file lives at the repo root (`Anela.Heblo.sln`), not under
  `backend/` as the task context's Step 7 command implied
  (`backend/Anela.Heblo.sln` does not exist) — ran `dotnet format
  Anela.Heblo.sln ...` from the repo root instead, with the same
  `--include`/`--verify-no-changes` arguments. No other deviation.
- The production change was committed on its own per the task context's
  Step 8, with the exact commit message given there.

## PR Summary
Moved `BankStatementImportDto.ErrorType`'s derivation out of the DTO
(which referenced the domain constant `ImportStatus.Success` directly)
and into `BankMappingProfile`'s AutoMapper configuration via a
`ForMember` rule. `ErrorType` is now a plain settable auto-property,
which fixes the generated OpenAPI/TypeScript client emitting it as a
read-only field. Runtime values are unchanged for any given
`ImportResult`, verified by the existing mapping and handler test suites.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs`
- `backend/src/Anela.Heblo.Application/Features/Bank/BankMappingProfile.cs`

## Status
DONE
