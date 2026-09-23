# Code Review: feat-4279 (BankStatementImportDto.ErrorType → BankMappingProfile)

## Review Result: CLEAN

### Scope reviewed
Full feature-branch diff against merge-base with `main` (`d84ddf77`). Production/test code changes:
- `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs`
- `backend/src/Anela.Heblo.Application/Features/Bank/BankMappingProfile.cs`
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs`

(The remaining diff entries are pipeline artifacts under `artifacts/feat-4279/` — spec, design, arch-review, task-plan, impl/review notes — not reviewable code.)

### Findings

`BankStatementImportDto.ErrorType` was converted from a computed, get-only property (`=> ImportResult != ImportStatus.Success ? ImportResult : null`) referencing the domain constant `ImportStatus`, to a plain settable auto-property (`{ get; set; }`), with the now-unused `using Anela.Heblo.Domain.Features.Bank;` removed from the DTO file. `BankMappingProfile`'s `CreateMap<BankStatementImport, BankStatementImportDto>()` gained a `.ForMember(dest => dest.ErrorType, opt => opt.MapFrom(src => src.ImportResult != ImportStatus.Success ? src.ImportResult : null))` rule that reproduces the exact same derivation logic. A new test, `BankStatementImportDto_ErrorType_Is_Settable_Independently_Of_The_Mapper`, proves the DTO round-trips a directly-set value.

This exactly matches spec FR-1/FR-2/FR-4:
- The derivation logic is byte-identical to what the DTO getter used to compute — no behavior change for any `ImportResult` value (FR-3).
- Both existing consumers (`GetBankStatementListHandler`, `GetBankStatementByIdHandler`) only ever populate the DTO via `_mapper.Map(...)`, so moving the rule into the single shared `CreateMap` is a complete, correct choke point — verified no other code path constructs `BankStatementImportDto` directly expecting the old getter behavior.
- `BankMappingProfileTests.Profile_Configuration_IsValid` (`AssertConfigurationIsValid`) still passes since `ErrorType` is now settable, so the `ForMember` rule is a real, executing mapping rule rather than the previously-documented AutoMapper silent-no-op-on-get-only-member failure mode.
- The two existing `ErrorType`-derivation tests are unmodified and still assert through `_mapper.Map(...)`, remaining valid regardless of which layer performs the derivation.
- `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` succeeds with 0 errors (verified directly in this review).

No correctness bug found: no logic error, no missing null/edge-case handling (the ternary is unchanged), no contract violation against the spec, no security or data-loss concern (server-side-only field, no new client-controllable input).

No advisory cleanup findings either — the diff is minimal, matches the plan's exact specified final file content, and introduces no duplication, dead code, or avoidable complexity.

### Blocking (correctness)
- None

### Advisory (cleanup)
- None
