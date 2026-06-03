# Remove Dead AutoMapper `ForMember` for `BankStatementImportDto.ErrorType` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the no-op `.ForMember(dest => dest.ErrorType, ...)` configuration from `BankMappingProfile` so the get-only computed `BankStatementImportDto.ErrorType` becomes the single source of truth, and add a profile-level unit test that proves mapping behaviour is preserved.

**Architecture:** Surgical edit in one Application file plus one new xUnit test class in the existing Bank tests folder. The `CreateMap<BankStatementImport, BankStatementImportDto>()` declaration stays — only the chained `ForMember` call is dropped. A new `BankMappingProfileTests` builds a real `IMapper` from the profile to assert `AssertConfigurationIsValid()` plus `ErrorType` derivation for both `"OK"` and non-`"OK"` `ImportResult` values. No DTO, domain, handler, OpenAPI, frontend, or migration changes.

**Tech Stack:** .NET 8, C#, AutoMapper, xUnit, FluentAssertions (all already present in the test project).

---

## File Structure

**Modify (1 file):**
- `backend/src/Anela.Heblo.Application/Features/Bank/BankMappingProfile.cs` — delete the chained `.ForMember(...)` call on line 12; terminate the `CreateMap<>()` with a semicolon.

**Create (1 file):**
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs` — xUnit test class that builds a real `IMapper` from `BankMappingProfile`, runs `AssertConfigurationIsValid()`, and verifies `ErrorType` derivation for `ImportResult = "OK"` and `ImportResult = "Failed"`.

**Do NOT modify:**
- `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs` (FR-3 — DTO contract is frozen).
- `backend/src/Anela.Heblo.Domain/Features/Bank/BankStatementImport.cs` (domain unchanged).
- `backend/src/Anela.Heblo.Domain/Features/Bank/ImportStatus.cs` (rename of `"OK"` magic string is explicitly out of scope).
- Any other AutoMapper profile, handler, repository, or controller.
- Any frontend file, including generated API clients under `frontend/src/api-client/` (NFR-4 forbids client diff).

---

## Verified Codebase Facts (do not re-research)

- `BankMappingProfile.cs:11-12` currently reads:
  ```csharp
  CreateMap<BankStatementImport, BankStatementImportDto>()
      .ForMember(dest => dest.ErrorType, opt => opt.MapFrom(src => src.ImportResult != "OK" ? src.ImportResult : null));
  ```
- `BankStatementImportDto.cs:13` already derives the value:
  ```csharp
  public string? ErrorType => ImportResult != "OK" ? ImportResult : null;
  ```
- `BankStatementImport` (domain) construction:
  - Constructor signature: `public BankStatementImport(string transferId, DateTime statementDate)`.
  - `ImportResult` has a `public set` (validated non-null).
  - `Currency` defaults to `CurrencyCode.CZK` from the constructor.
- Test project `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` already references `xunit`, `FluentAssertions`, and the Application project — no new packages required.
- Existing Bank test files use the `Anela.Heblo.Tests.Features.Bank` namespace and xUnit `[Fact]` style (e.g. `ImportBankStatementHandlerTests.cs`).
- Only consumer of the mapping: `GetBankStatementListHandler` calls `_mapper.Map<List<BankStatementImportDto>>(items)`. Removing the `ForMember` (but keeping `CreateMap`) does not affect it because `ErrorType` is computed by the DTO getter, not set by the mapper.

---

### Task 1: Add `BankMappingProfile` test that proves current behaviour (RED phase, pre-edit)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs`

The test is written BEFORE the production edit so that it locks in the expected post-change behaviour. It will pass both before and after the `ForMember` removal — that is precisely the point (the `ForMember` is a no-op, so removing it must not change behaviour). The `AssertConfigurationIsValid` test is what gives us a hard guard against accidentally deleting `CreateMap` too.

- [ ] **Step 1: Write the test file**

Create `backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs` with the following exact contents:

```csharp
using Anela.Heblo.Application.Features.Bank;
using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Domain.Features.Bank;
using AutoMapper;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank;

public class BankMappingProfileTests
{
    private static IMapper CreateMapper()
    {
        var configuration = new MapperConfiguration(cfg => cfg.AddProfile<BankMappingProfile>());
        return configuration.CreateMapper();
    }

    [Fact]
    public void Profile_Configuration_IsValid()
    {
        var configuration = new MapperConfiguration(cfg => cfg.AddProfile<BankMappingProfile>());

        configuration.AssertConfigurationIsValid();
    }

    [Fact]
    public void Map_BankStatementImport_To_Dto_When_ImportResult_Is_OK_Sets_ErrorType_To_Null()
    {
        var mapper = CreateMapper();
        var source = new BankStatementImport("transfer-1", new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc))
        {
            ImportResult = "OK",
        };

        var dto = mapper.Map<BankStatementImportDto>(source);

        dto.ImportResult.Should().Be("OK");
        dto.ErrorType.Should().BeNull();
    }

    [Fact]
    public void Map_BankStatementImport_To_Dto_When_ImportResult_Is_Not_OK_Sets_ErrorType_To_ImportResult()
    {
        var mapper = CreateMapper();
        var source = new BankStatementImport("transfer-2", new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc))
        {
            ImportResult = "Failed",
        };

        var dto = mapper.Map<BankStatementImportDto>(source);

        dto.ImportResult.Should().Be("Failed");
        dto.ErrorType.Should().Be("Failed");
    }
}
```

- [ ] **Step 2: Build the test project**

Run from the worktree root:

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: build succeeds with no errors. Warnings about other code are acceptable; errors are not.

- [ ] **Step 3: Run the new tests (they must pass against the current, unmodified profile)**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~BankMappingProfileTests" --no-build
```

Expected: 3 tests pass (`Profile_Configuration_IsValid`, `Map_BankStatementImport_To_Dto_When_ImportResult_Is_OK_Sets_ErrorType_To_Null`, `Map_BankStatementImport_To_Dto_When_ImportResult_Is_Not_OK_Sets_ErrorType_To_ImportResult`).

If `Profile_Configuration_IsValid` fails with a message about an unmapped destination member, that means AutoMapper actually *does* honour the `ForMember` and the architectural premise is wrong — **stop, do not proceed to Task 2, and surface the failure to the reviewer**. The remaining two tests passing against the current code confirms the no-op behaviour described in the spec.

- [ ] **Step 4: Commit the new test file**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs
git commit -m "test: add BankMappingProfile mapping and ErrorType derivation tests"
```

---

### Task 2: Remove the dead `ForMember` configuration

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/BankMappingProfile.cs:11-12`

- [ ] **Step 1: Replace the `CreateMap` + `ForMember` chain with a bare `CreateMap`**

In `backend/src/Anela.Heblo.Application/Features/Bank/BankMappingProfile.cs`, replace:

```csharp
        CreateMap<BankStatementImport, BankStatementImportDto>()
            .ForMember(dest => dest.ErrorType, opt => opt.MapFrom(src => src.ImportResult != "OK" ? src.ImportResult : null));
```

with:

```csharp
        CreateMap<BankStatementImport, BankStatementImportDto>();
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
        CreateMap<BankStatementImport, BankStatementImportDto>();
    }
}
```

- [ ] **Step 2: Build the solution to confirm the Application project still compiles**

Run:

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build succeeds with no errors.

- [ ] **Step 3: Re-run the Bank profile tests to confirm behaviour is unchanged**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~BankMappingProfileTests"
```

Expected: all 3 tests pass (same as Task 1 Step 3).

- [ ] **Step 4: Run the broader Bank slice tests to confirm no regression in adjacent code**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Bank"
```

Expected: all tests in `Anela.Heblo.Tests.Features.Bank.*` pass (this includes `ImportBankStatementHandlerTests`, `BankStatementImportIntegrationTests`, `BankStatementImportRepositoryTests`, etc.). If any pre-existing test fails for environmental reasons unrelated to this change (e.g. a Testcontainers PostgreSQL test that needs Docker), note it but do not attempt to fix it as part of this task — it is out of scope.

- [ ] **Step 5: Apply formatting**

Run:

```bash
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: no errors. Stage any whitespace adjustments the formatter applies to the two files touched.

- [ ] **Step 6: Verify the OpenAPI-generated client did not drift (NFR-4 guard)**

Run a full backend build (which regenerates OpenAPI clients via the project's build steps) and check the generated client tree for diff:

```bash
dotnet build
git status --short frontend/src/api-client/
git diff -- frontend/src/api-client/
```

Expected: `git status` and `git diff` show **no changes** under `frontend/src/api-client/`. If any file there is modified, that is a regression — revert the production edit and stop. NFR-4 forbids any consumer-visible diff.

- [ ] **Step 7: Commit the production change**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/BankMappingProfile.cs
git commit -m "refactor(bank): remove dead ForMember on BankStatementImportDto.ErrorType

AutoMapper silently ignores ForMember calls targeting read-only
destination members. ErrorType is a computed get-only property on
the DTO that already encodes the 'ImportResult != \"OK\"' rule.
Removing the no-op rule makes the DTO getter the single source of
truth and eliminates duplicated logic.

No behaviour change: mapping output (including ErrorType derivation)
is bit-identical, verified by BankMappingProfileTests."
```

---

## Validation Checklist (run after both tasks)

- [ ] `dotnet build` — full solution builds cleanly.
- [ ] `dotnet format` — no formatting diff on the two touched files.
- [ ] `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Bank"` — all Bank tests green.
- [ ] `git diff -- frontend/src/api-client/` shows nothing (NFR-4).
- [ ] `git log --oneline -2` shows the test commit before the production commit.
- [ ] `BankStatementImportDto.cs`, `BankStatementImport.cs`, and `ImportStatus.cs` are unchanged in the diff (`git diff main -- backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs backend/src/Anela.Heblo.Domain/Features/Bank/BankStatementImport.cs backend/src/Anela.Heblo.Domain/Features/Bank/ImportStatus.cs` returns empty).

---

## Spec Coverage Map

| Spec requirement | Where covered |
|---|---|
| FR-1: Remove dead `ForMember` configuration | Task 2 Step 1 (edit) + Task 2 Step 7 (commit) |
| FR-2: Preserve `ErrorType` runtime behaviour for `"OK"`, non-`"OK"`, and `null` (note: `null` setter on entity throws, so verified at DTO getter level only) | Task 1 Step 1 (tests for `"OK"` and `"Failed"`); the `null`-`ImportResult` case is unreachable through the domain constructor / setter and therefore intentionally not tested at the mapping layer |
| FR-3: No DTO contract change | "Do NOT modify" list at top + Validation Checklist last item |
| FR-4: Verification via tests against real `IMapper` (not a mock) | Task 1 Step 1 — `BankMappingProfileTests.CreateMapper()` builds a real `IMapper` from `BankMappingProfile`; the arch-review clarifying note ("not a `Mock<IMapper>`") is honoured |
| NFR-1: Performance | No code that affects throughput; one fewer profile rule evaluated at startup |
| NFR-2: Security | No surface affected |
| NFR-3: Maintainability | Single source of truth restored (DTO getter) |
| NFR-4: Backwards compatibility — no OpenAPI client diff | Task 2 Step 6 explicitly verifies via `git diff -- frontend/src/api-client/` |
| Out of Scope: `ImportStatus.Success` rename, enum extraction, other profiles, DTO setter, FE changes | "Do NOT modify" list + commit message scope |

---

## Risk Register (from architecture review, with mitigations baked into tasks)

| Risk | Mitigated by |
|---|---|
| Accidentally removing `CreateMap` too | Task 1 `Profile_Configuration_IsValid` test fails immediately if mapping breaks; Task 2 Step 1 shows the exact final file contents to copy |
| Future contributor adds an `ErrorType` setter expecting mapper population | DTO getter remains documented derivation; `Map_*Sets_ErrorType_*` tests will fail loudly if behaviour changes |
| OpenAPI client drift | Task 2 Step 6 git-diff check on `frontend/src/api-client/` |
| Reviewer interprets as behaviour change | Commit message in Task 2 Step 7 explicitly states AutoMapper ignores `ForMember` on get-only members and asserts no behaviour change |
