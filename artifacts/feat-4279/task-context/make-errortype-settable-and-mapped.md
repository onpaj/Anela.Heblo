### task: make-errortype-settable-and-mapped

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/BankMappingProfile.cs`

Both edits in this task must be applied together before building — arch-review confirms AutoMapper does not raise any compile-time or config-time error if `ForMember` is added while the destination member is still get-only (it silently no-ops); the edits are only meaningfully verifiable as a pair.

- [ ] **Step 1: Make `ErrorType` a plain settable auto-property and drop the unused `using`**

In `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs`, replace the entire file content with:

```csharp
namespace Anela.Heblo.Application.Features.Bank.Contracts;

public class BankStatementImportDto
{
    public int Id { get; set; }
    public string TransferId { get; set; } = null!;
    public DateTime StatementDate { get; set; }
    public DateTime ImportDate { get; set; }
    public string Account { get; set; } = null!;
    public string Currency { get; set; } = null!;
    public int ItemCount { get; set; }
    public string ImportResult { get; set; } = null!;
    public string? ErrorType { get; set; }
}
```

(This removes the `using Anela.Heblo.Domain.Features.Bank;` line and replaces the expression-bodied `ErrorType` getter with a plain auto-property.)

- [ ] **Step 2: Add the `ForMember` rule to `BankMappingProfile`**

In `backend/src/Anela.Heblo.Application/Features/Bank/BankMappingProfile.cs`, replace:

```csharp
        CreateMap<BankStatementImport, BankStatementImportDto>();
```

with:

```csharp
        CreateMap<BankStatementImport, BankStatementImportDto>()
            .ForMember(dest => dest.ErrorType,
                opt => opt.MapFrom(src =>
                    src.ImportResult != ImportStatus.Success ? src.ImportResult : null));
```

The final file content must be exactly:

```csharp
using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Domain.Features.Bank;
using AutoMapper;

namespace Anela.Heblo.Application.Features.Bank;

public class BankMappingProfile : Profile
{
    public BankMappingProfile()
    {
        CreateMap<BankStatementImport, BankStatementImportDto>()
            .ForMember(dest => dest.ErrorType,
                opt => opt.MapFrom(src =>
                    src.ImportResult != ImportStatus.Success ? src.ImportResult : null));
    }
}
```

- [ ] **Step 3: Build the Application project**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: `Build succeeded.` with `0 Error(s)`.

- [ ] **Step 4: Run the Bank mapping tests — all 4 must now pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Bank.BankMappingProfileTests" \
  --nologo
```

Expected: all 4 tests pass — `Profile_Configuration_IsValid`, `Map_BankStatementImport_To_Dto_When_ImportResult_Is_OK_Sets_ErrorType_To_Null`, `Map_BankStatementImport_To_Dto_When_ImportResult_Is_Not_OK_Sets_ErrorType_To_ImportResult`, and the new `BankStatementImportDto_ErrorType_Is_Settable_Independently_Of_The_Mapper` (the RED test from the previous task is now GREEN).

If `Profile_Configuration_IsValid` fails with an unmapped-destination-member error, the `ForMember` syntax was mistyped — compare against Step 2's exact final file content and fix.

If either mapper-based `ErrorType` test fails, the `ForMember` predicate does not match `src.ImportResult != ImportStatus.Success ? src.ImportResult : null` exactly — compare against Step 2 and fix.

- [ ] **Step 5: Run the by-id handler tests — must still pass unmodified**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Bank.GetBankStatementByIdHandlerTests" \
  --nologo
```

Expected: all 4 tests pass, including `Handle_WithExistingId_ReturnsMappedDto` (asserts `Assert.Null(result.ErrorType)`) and `Handle_ProducesSameDtoAsListHandlerMapping_ForSameEntity` (asserts `Assert.Equal(fromListMapping.ErrorType, fromHandler.ErrorType)`). This is the end-to-end proof that both existing consumers still derive `ErrorType` identically to before the refactor.

- [ ] **Step 6: Run the full Bank-module test scope to confirm no regressions outside these two files**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.Bank" \
  --nologo
```

Expected: all tests under `Anela.Heblo.Tests.Features.Bank.*` pass. If any pre-existing test fails for an unrelated environmental reason (e.g. a Testcontainers PostgreSQL test needing Docker), note it but do not attempt to fix it — out of scope for this task.

- [ ] **Step 7: Apply formatting scoped to the touched files**

```bash
dotnet format backend/Anela.Heblo.sln \
  --include backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs \
            backend/src/Anela.Heblo.Application/Features/Bank/BankMappingProfile.cs \
            backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs \
  --verify-no-changes
```

Expected: exit code `0`, no diff. If a diff is reported, drop `--verify-no-changes`, re-run, then `git diff` the three files to confirm only expected whitespace/formatting changed — no unrelated reformatting. Re-run Step 4–6's tests after formatting to confirm nothing broke.

- [ ] **Step 8: Commit the production change**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs \
        backend/src/Anela.Heblo.Application/Features/Bank/BankMappingProfile.cs

git commit -m "$(cat <<'EOF'
refactor(bank): move BankStatementImportDto.ErrorType derivation into BankMappingProfile

BankStatementImportDto.ErrorType was a computed get-only property that
referenced the domain constant ImportStatus.Success directly, coupling
the contract to domain logic (violates the project's DTO/contract
rules in docs/architecture/development_guidelines.md) and causing the
generated OpenAPI/TypeScript client to emit errorType as a read-only
field that cannot be round-tripped.

ErrorType is now a plain nullable auto-property; BankMappingProfile's
existing CreateMap<BankStatementImport, BankStatementImportDto>() gains
a ForMember rule that reproduces the exact same success/failure
derivation. Both existing consumers (GetBankStatementListHandler,
GetBankStatementByIdHandler) already populate the DTO exclusively via
IMapper, so no handler changes were needed.

Runtime ErrorType values are unchanged for any given ImportResult
(verified by BankMappingProfileTests and GetBankStatementByIdHandlerTests).
The generated OpenAPI/TypeScript client's errorType field is expected
to change from read-only to writable as the intended fix for issue #4279.
EOF
)"
```

---

