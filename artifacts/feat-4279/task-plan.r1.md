# Move BankStatementImportDto.ErrorType derivation into BankMappingProfile Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn `BankStatementImportDto.ErrorType` into a plain settable auto-property and move its `ImportResult != ImportStatus.Success` derivation rule into `BankMappingProfile`, so the DTO is a pure data container and the domain decision lives in the Application-layer mapping profile, per GitHub issue #4279 and `docs/architecture/development_guidelines.md`'s Contracts/DTO rules.

**Architecture:** Surgical two-file production edit (`BankStatementImportDto.cs`, `BankMappingProfile.cs`) plus one new regression test in the existing `BankMappingProfileTests.cs`. Both production edits must land together in one buildable state — arch-review confirms AutoMapper silently no-ops a `ForMember` targeting a get-only destination member (no compile or config-time error), so there is no safety net for landing them separately. No handler, controller, domain, or frontend source changes: both existing consumers (`GetBankStatementListHandler`, `GetBankStatementByIdHandler`) already populate the DTO exclusively via `_mapper.Map(...)`. The OpenAPI/TypeScript client is expected to change shape for `errorType` (read-only → writable) as the explicit, intended fix — this is not a regression to prevent.

**Tech Stack:** .NET 8, C#, AutoMapper, xUnit, FluentAssertions (all already present in the test project).

---

## File Structure

**Modify (2 files):**
- `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs` — replace the computed `ErrorType` expression-bodied property with a plain `{ get; set; }` auto-property; remove the now-unused `using Anela.Heblo.Domain.Features.Bank;` directive.
- `backend/src/Anela.Heblo.Application/Features/Bank/BankMappingProfile.cs` — add `.ForMember(dest => dest.ErrorType, opt => opt.MapFrom(src => src.ImportResult != ImportStatus.Success ? src.ImportResult : null))` to the existing `CreateMap<BankStatementImport, BankStatementImportDto>()` call.

**Modify (1 test file):**
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs` — add one new `[Fact]` that constructs `BankStatementImportDto` directly and proves `ErrorType` is settable (proves FR-1/FR-4 independently of the mapper).

**Do NOT modify:**
- `backend/src/Anela.Heblo.Domain/Features/Bank/BankStatementImport.cs` and `ImportStatus.cs` (domain unchanged — Out of Scope).
- `GetBankStatementListHandler.cs`, `GetBankStatementByIdHandler.cs`, `BankStatementsController.cs`, `GetBankStatementListResponse.cs` (all already map exclusively through `IMapper`; no code change needed).
- `GetBankStatementByIdHandlerTests.cs` (already exercises the handler → mapper path and asserts `ErrorType`; must keep passing unmodified).
- Any frontend source file, including `ImportTab.tsx` (read-only consumer of `errorType`, needs no edit) — only the auto-generated OpenAPI/TypeScript client is expected to change, as a build side effect.

---

## Verified Codebase Facts (do not re-research)

- `BankStatementImportDto.cs` currently (full file):
  ```csharp
  using Anela.Heblo.Domain.Features.Bank;

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
      public string? ErrorType => ImportResult != ImportStatus.Success ? ImportResult : null;
  }
  ```
- `BankMappingProfile.cs` currently (full file):
  ```csharp
  using Anela.Heblo.Application.Features.Bank.Contracts;
  using Anela.Heblo.Domain.Features.Bank;
  using AutoMapper;

  namespace Anela.Heblo.Application.Features.Bank;

  public class BankMappingProfile : Profile
  {
      public BankMappingProfile()
      {
          CreateMap<BankStatementImport, BankStatementImportDto>();
      }
  }
  ```
  It already has `using Anela.Heblo.Domain.Features.Bank;` — no new `using` needed here.
- `ImportStatus.cs` (unchanged, for reference): `public const string Success = "OK";` (plus `ProcessingError`, `UnknownError`).
- `BankMappingProfileTests.cs` currently has 3 `[Fact]`s: `Profile_Configuration_IsValid`, `Map_BankStatementImport_To_Dto_When_ImportResult_Is_OK_Sets_ErrorType_To_Null`, `Map_BankStatementImport_To_Dto_When_ImportResult_Is_Not_OK_Sets_ErrorType_To_ImportResult`. It builds `IMapper` via `new MapperConfiguration(cfg => cfg.AddProfile<BankMappingProfile>(), NullLoggerFactory.Instance)`. It already has all needed `using`s (`Anela.Heblo.Application.Features.Bank`, `.Contracts`, `Anela.Heblo.Domain.Features.Bank`, `AutoMapper`, `FluentAssertions`, `Microsoft.Extensions.Logging.Abstractions`, `Xunit`).
- `GetBankStatementByIdHandlerTests.cs` already has 4 passing `[Fact]`s including two that assert on `.ErrorType` through the handler → mapper path (`Assert.Null(result.ErrorType)` and `Assert.Equal(fromListMapping.ErrorType, fromHandler.ErrorType)`) — these must keep passing unmodified as regression proof that FR-2/FR-3 hold end-to-end.
- Both `GetBankStatementListHandler.Handle` (line 50: `_mapper.Map<List<BankStatementImportDto>>(items)`) and `GetBankStatementByIdHandler.Handle` (line 37: `_mapper.Map<BankStatementImportDto>(entity)`) populate the DTO exclusively via `IMapper` — confirmed by reading both files. Neither needs any edit.
- Repo-wide search confirms `ErrorType`/`errorType` has no other production usage outside: `BankStatementImportDto.cs`, `BankMappingProfile.cs`, `BankMappingProfileTests.cs`, `GetBankStatementByIdHandlerTests.cs`, `frontend/src/components/customer/tabs/ImportTab.tsx` (read-only display via `getImportStatusIcon(statement.errorType)`), and the generated `frontend/src/api/generated/api-client.ts`.
- Test project path: `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`. Solution path: `backend/Anela.Heblo.sln`.

---

### task: write-dto-settability-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs`

This is the RED step: write a test that requires `ErrorType` to have a public setter. Against the current (unmodified) DTO this fails to **compile**, which is the expected "failing" signal for this particular refactor (there is no runtime-failing equivalent since the member does not exist yet in settable form).

- [ ] **Step 1: Add the new test method**

Add the following `[Fact]` to `BankMappingProfileTests.cs`, immediately after the existing `Map_BankStatementImport_To_Dto_When_ImportResult_Is_Not_OK_Sets_ErrorType_To_ImportResult` method (before the closing `}` of the class):

```csharp
    [Fact]
    public void BankStatementImportDto_ErrorType_Is_Settable_Independently_Of_The_Mapper()
    {
        var dto = new BankStatementImportDto
        {
            ErrorType = "SOME_ERROR",
        };

        dto.ErrorType.Should().Be("SOME_ERROR");
    }
```

- [ ] **Step 2: Build the test project and confirm it fails to compile**

Run from the repo root:

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: build **fails** with a compiler error on the new test's object-initializer line, of the form `CS0200: Property or indexer 'BankStatementImportDto.ErrorType' cannot be assigned to -- it is read only`. This confirms the test correctly exercises the not-yet-implemented behavior (FR-1). If the build succeeds, stop — the DTO was already settable and something upstream of this plan is inconsistent with the Verified Codebase Facts above; investigate before continuing.

- [ ] **Step 3: Commit the failing test**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs
git commit -m "test(bank): add failing test proving BankStatementImportDto.ErrorType is not yet settable"
```

---

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

### task: verify-client-diff-and-final-checklist

**Files:**
- Inspect only (no source edits expected): generated OpenAPI/TypeScript client output.

- [ ] **Step 1: Confirm how the TypeScript client is regenerated**

Read `docs/development/api-client-generation.md` to confirm whether `dotnet build` regenerates the client automatically or a separate command is required (per that doc, the client is auto-generated on build — confirm this still holds before proceeding).

- [ ] **Step 2: Run a full backend build to trigger client regeneration**

```bash
dotnet build backend/Anela.Heblo.sln --nologo
```

Expected: `Build succeeded.` with `0 Error(s)`.

- [ ] **Step 3: Inspect the generated client diff and confirm it is limited to `errorType`'s read-only-ness**

```bash
git status --short frontend/src/api/generated/
git diff -- frontend/src/api/generated/
```

Expected: the diff (if any) touches only the `errorType` property's declaration in the type/interface generated for `BankStatementImportDto` (e.g. it stops being marked read-only, or a setter/assignability annotation appears), and only within the section(s) of the generated file that describe `BankStatementImportDto`. This is the **expected, intended** outcome of FR-1/FR-2 (spec NFR-4) — do not revert it. If the diff touches any other DTO, type, or property, or is empty (no regeneration occurred), investigate: an empty diff after Step 2 means the client-generation step needs to be re-run per Step 1's findings before this check is meaningful.

- [ ] **Step 4: Commit the generated client diff, if any**

```bash
git add frontend/src/api/generated/
git commit -m "chore(bank): regenerate OpenAPI client for BankStatementImportDto.ErrorType now being settable" || true
```

(The `|| true` only absorbs the case where the client regenerated byte-identical — do not use it to skip investigating an unexpected diff found in Step 3.)

- [ ] **Step 5: Full solution build and full test suite, final gate**

```bash
dotnet build backend/Anela.Heblo.sln --nologo
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Bank" --nologo
```

Expected: build succeeds, all Bank-module tests pass (same scope and expected outcome as the previous task's Step 6).

- [ ] **Step 6: Verify clean working tree**

```bash
git status
```

Expected: `nothing to commit, working tree clean` (all edits from this plan are committed across the three tasks in this plan).

---

## Validation Checklist (run after all tasks)

- [ ] `dotnet build backend/Anela.Heblo.sln` — full solution builds cleanly.
- [ ] `dotnet format` scoped to the three touched backend files — no diff.
- [ ] `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Bank"` — all Bank tests green, including all 4 `BankMappingProfileTests` and all 4 `GetBankStatementByIdHandlerTests`.
- [ ] `BankStatementImportDto.cs` no longer contains `ImportStatus` or `using Anela.Heblo.Domain.Features.Bank;` (`grep -n "ImportStatus\|using Anela.Heblo.Domain" backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs` returns nothing).
- [ ] `BankMappingProfile.cs` contains exactly one `.ForMember(dest => dest.ErrorType, ...)` call.
- [ ] `git diff origin/main -- backend/src/Anela.Heblo.Domain/` returns empty (domain untouched).
- [ ] Any diff under `frontend/src/api/generated/` is limited to `errorType`'s read-only/writable classification for `BankStatementImportDto` (NFR-4 — this is expected, not a regression).

---

## Spec Coverage Map

| Spec requirement | Where covered |
|---|---|
| FR-1: `ErrorType` becomes a plain settable auto-property; `ImportStatus` reference and `using` removed from the DTO | `make-errortype-settable-and-mapped` Step 1 |
| FR-2: `BankMappingProfile` derives `ErrorType` via `ForMember`/`MapFrom` reproducing exact semantics | `make-errortype-settable-and-mapped` Step 2 |
| FR-2 acceptance: identical `ErrorType` for both `GetBankStatementListHandler` and `GetBankStatementByIdHandler` paths | `make-errortype-settable-and-mapped` Step 5 (`Handle_ProducesSameDtoAsListHandlerMapping_ForSameEntity`) |
| FR-3: No behavior change for any `ImportResult` value | `make-errortype-settable-and-mapped` Steps 4–6 (existing tests re-run and pass) |
| FR-4: New settability test; existing `ErrorType`/handler tests continue to pass | `write-dto-settability-test` (new test) + `make-errortype-settable-and-mapped` Steps 4–5 (existing tests unmodified and green) |
| NFR-1: Performance — no impact | Implicit; no new work, same single comparison moved between layers |
| NFR-2: Security — no impact | Implicit; no new data flow or trust boundary |
| NFR-3: Maintainability — SRP restored | `make-errortype-settable-and-mapped` Steps 1–2 (the structural change itself) |
| NFR-4: OpenAPI/TS client diff for `errorType` is expected, not a regression | `verify-client-diff-and-final-checklist` Steps 2–4 |
| Out of Scope: `ImportStatus` unchanged, domain unchanged, no other DTO/handler/frontend source edits | File Structure "Do NOT modify" list; Validation Checklist |

No gaps.

## Placeholder Scan

Searched this plan for: TBD, TODO, "implement later", "fill in details", "add appropriate error handling", "add validation", "handle edge cases", "write tests for the above" (without code), "similar to Task N". None present — every step shows exact file content or exact commands with expected output.

## Type / Symbol Consistency Check

- `BankStatementImportDto.ErrorType` — same name and type (`string?`) used consistently across the DTO edit, the mapping profile's `ForMember` target, the new settability test, and the Spec Coverage Map.
- `BankMappingProfile` / `CreateMap<BankStatementImport, BankStatementImportDto>()` — matches the on-disk class and existing `CreateMap` call exactly (only the `ForMember` chain is added).
- `ImportStatus.Success` — matches `backend/src/Anela.Heblo.Domain/Features/Bank/ImportStatus.cs`'s existing `public const string Success = "OK";` declaration; only referenced from `BankMappingProfile.cs` (which already imports the namespace), never re-added to the DTO.
- Test method name `BankStatementImportDto_ErrorType_Is_Settable_Independently_Of_The_Mapper` used consistently between `write-dto-settability-test` and `make-errortype-settable-and-mapped`.
- File paths (`backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs`, `backend/src/Anela.Heblo.Application/Features/Bank/BankMappingProfile.cs`, `backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs`, `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`, `backend/Anela.Heblo.sln`) used consistently across all three tasks.

No inconsistencies found.
