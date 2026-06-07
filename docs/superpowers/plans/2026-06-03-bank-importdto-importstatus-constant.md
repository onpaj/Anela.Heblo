# Replace Magic String `"OK"` with `ImportStatus.Success` in `BankStatementImportDto` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the literal string `"OK"` in `BankStatementImportDto.ErrorType` with the existing `ImportStatus.Success` domain constant so the DTO stays in lockstep with every other Bank-module consumer if that constant is ever renormalized.

**Architecture:** Pure refactor — substitute a `const string` reference for an inline string literal in a single Application-layer DTO expression, then propagate the same substitution into the two existing mapping-test assertions. No new files, no new dependencies (Application already references Domain), no API or serialization changes. Emitted IL is equivalent because `ImportStatus.Success` is a compile-time constant.

**Tech Stack:** .NET 8, C#, AutoMapper, xUnit + FluentAssertions for tests.

---

## Background — Why This Refactor

The Bank module declares import-status constants in one place:

```csharp
// backend/src/Anela.Heblo.Domain/Features/Bank/ImportStatus.cs
public static class ImportStatus
{
    public const string Success = "OK";
    public const string ProcessingError = "PROCESSING_ERROR";
    public const string UnknownError = "UNKNOWN_ERROR";
}
```

Every other consumer in the module references `ImportStatus.Success` (e.g. `BankStatementImportRepository.cs:49`, `ImportBankStatementHandler.cs:89`). The lone outlier today is:

```csharp
// backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs:13
public string? ErrorType => ImportResult != "OK" ? ImportResult : null;
```

If `ImportStatus.Success` were ever changed (e.g. lowercased to `"ok"`), every other site would update via the constant — this DTO would silently start misclassifying every successful import as an error. The two existing tests in `BankMappingProfileTests.cs` would also silently keep passing for the wrong reason because they assert against the literal `"OK"` on lines 37 and 42.

## File Structure

This refactor touches **two files only**. No new files, no deletions.

- **Modify:** `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs`
  - Add `using Anela.Heblo.Domain.Features.Bank;` directive.
  - Replace the literal `"OK"` in the `ErrorType` expression with `ImportStatus.Success`.
- **Modify:** `backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs`
  - Replace the two literal `"OK"` occurrences (lines 37 and 42) with `ImportStatus.Success`. The file already imports `Anela.Heblo.Domain.Features.Bank` on line 3, so no `using` change is required.

## Refactor-Style TDD Approach

The existing mapping test (`BankMappingProfileTests.Map_BankStatementImport_To_Dto_When_ImportResult_Is_OK_Sets_ErrorType_To_Null`) already covers the success branch via the real production code path (Entity → AutoMapper → DTO → property access). A new direct-instantiation unit test would duplicate that coverage; per the architecture review's Specification Amendment #2, we **reuse the existing test**.

For a behavior-preserving refactor, the existing test acts as the safety net. The TDD rhythm is:

1. Confirm the existing tests pass on the baseline (no false starts).
2. Make the production change. Re-run tests → they must still pass.
3. Update the test assertions to reference the constant. Re-run tests → they must still pass.
4. Run quality gates (`dotnet format` scoped to the two files, `dotnet build`).
5. Commit a single focused change.

---

### Task 1: Establish baseline — confirm existing tests pass

**Files:**
- Read: `backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs`

- [ ] **Step 1: Run the existing Bank mapping tests as a baseline**

Run from the repo root:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Bank.BankMappingProfileTests" \
  --nologo
```

Expected: All three tests pass — `Profile_Configuration_IsValid`, `Map_BankStatementImport_To_Dto_When_ImportResult_Is_OK_Sets_ErrorType_To_Null`, and `Map_BankStatementImport_To_Dto_When_ImportResult_Is_Not_OK_Sets_ErrorType_To_ImportResult`.

If anything fails on the baseline, **stop**. Do not modify production code until the baseline is green; investigate and resolve first.

---

### Task 2: Update the DTO to reference `ImportStatus.Success`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs`

The current file (read it first to confirm exact contents):

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
    public string? ErrorType => ImportResult != "OK" ? ImportResult : null;
}
```

- [ ] **Step 1: Add the `using` directive**

Use the Edit tool. Replace the file-opening line:

Old:
```csharp
namespace Anela.Heblo.Application.Features.Bank.Contracts;
```

New:
```csharp
using Anela.Heblo.Domain.Features.Bank;

namespace Anela.Heblo.Application.Features.Bank.Contracts;
```

Rationale: the new directive goes above the file-scoped `namespace` declaration, consistent with the project's other Bank-module files (e.g. `BankMappingProfile.cs` and `BankMappingProfileTests.cs`).

- [ ] **Step 2: Replace the literal `"OK"` with `ImportStatus.Success`**

Use the Edit tool.

Old:
```csharp
    public string? ErrorType => ImportResult != "OK" ? ImportResult : null;
```

New:
```csharp
    public string? ErrorType => ImportResult != ImportStatus.Success ? ImportResult : null;
```

- [ ] **Step 3: Re-run the Bank mapping tests — must still pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Bank.BankMappingProfileTests" \
  --nologo
```

Expected: All three tests still pass. Because `ImportStatus.Success` is `const string Success = "OK"`, the runtime comparison is identical to the prior literal — the refactor is behavior-preserving and the assertions in the test file (which still compare against the literal `"OK"`) continue to hold.

If any test fails, **stop and investigate**. Likely causes: typo, wrong `using` directive, or accidental edit to another expression.

---

### Task 3: Update the existing mapping tests to reference the constant

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs`

The current file's relevant region (lines 31–44):

```csharp
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
```

The file already has `using Anela.Heblo.Domain.Features.Bank;` on line 3, so no import change is needed.

- [ ] **Step 1: Replace the `ImportResult = "OK"` setup literal**

Use the Edit tool.

Old:
```csharp
            ImportResult = "OK",
```

New:
```csharp
            ImportResult = ImportStatus.Success,
```

- [ ] **Step 2: Replace the `dto.ImportResult.Should().Be("OK")` assertion**

Use the Edit tool.

Old:
```csharp
        dto.ImportResult.Should().Be("OK");
```

New:
```csharp
        dto.ImportResult.Should().Be(ImportStatus.Success);
```

- [ ] **Step 3: Do not touch the second test (`…_Is_Not_OK_…`)**

The second test on lines 46–59 uses `ImportResult = "Failed"` to exercise the not-success branch. `"Failed"` is intentionally an arbitrary non-success value, not a named import-status constant. Per the surgical-changes rule, leave it as-is. Do **not** replace it with `ImportStatus.ProcessingError` or similar — that would change the test's semantic from "any non-success value flows through" to "this specific named status flows through."

- [ ] **Step 4: Re-run the Bank mapping tests — must still pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Bank.BankMappingProfileTests" \
  --nologo
```

Expected: All three tests still pass. The test now references `ImportStatus.Success` symbolically, so if the underlying constant value is ever renormalized in a future change, the test will adapt automatically rather than silently passing against a stale literal.

---

### Task 4: Quality gates — format and build

**Files:**
- No file modifications expected; this task verifies the two edits cleanly satisfy formatter and compiler.

- [ ] **Step 1: Run `dotnet format` scoped to the two edited files**

Per the architecture review's NFR-3 clarification, scope the formatter narrowly to avoid incidental reformatting:

```bash
dotnet format backend/Anela.Heblo.sln \
  --include backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs \
            backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs \
  --verify-no-changes
```

Expected: Exit code `0` and no diff. If `--verify-no-changes` reports a change, drop the `--verify-no-changes` flag, re-run, then `git diff` the two files to confirm only the new `using` directive (and possibly its position relative to other usings) was adjusted. Adjacent unrelated reformatting is a violation of the surgical-changes rule — back out and redo the edit.

- [ ] **Step 2: Build the full solution**

```bash
dotnet build backend/Anela.Heblo.sln --nologo
```

Expected: `Build succeeded.` with `0 Error(s)`. Warnings count must not increase vs. baseline. If a CS0246 (`type or namespace not found`) appears on `ImportStatus`, the `using` directive in `BankStatementImportDto.cs` is missing or misspelled — fix and re-build.

- [ ] **Step 3: Run the full Bank-module test scope to confirm no regressions outside the mapping tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Bank" \
  --no-build --nologo
```

Expected: All tests in `Anela.Heblo.Tests.Features.Bank` pass. This covers `BankMappingProfileTests`, `BankStatementImportRepositoryTests`, and any other Bank-module test classes — the refactor must not destabilize any of them.

---

### Task 5: Verify the OpenAPI / TypeScript client regenerates to a no-op

**Files:**
- Inspect (no edits expected): `frontend/src/api/` for any file referencing `BankStatementImportDto`.

Per `spec.r1.md` FR-2 and NFR-3, the generated TS client surface for `BankStatementImportDto` must be byte-identical. The DTO's public shape (property names, types, nullability) is unchanged by the refactor, so this is a verification gate only.

- [ ] **Step 1: Confirm whether the TS client is regenerated by the build**

Read `docs/development/api-client-generation.md` to confirm whether `dotnet build` regenerates the TS client automatically or whether a separate command is required. Do not skip this check — if the project regenerates on build, Task 4 Step 2 already produced any diff; if it requires a separate command, run it now.

If a separate generation command exists (commonly `npm run generate-api` or similar), run it from the appropriate directory as documented.

- [ ] **Step 2: Inspect for unintended client diff**

```bash
git status --short frontend/
git diff --stat frontend/src/api/
```

Expected: No changes under `frontend/src/api/`. If any diff appears in a file that mentions `BankStatementImportDto`, **stop**. Investigate whether the generator picked up the new `using` directive as a schema change — if so, the refactor would breach FR-2 and must be re-examined. If the diff is unrelated noise (e.g. timestamp comments, formatting), confirm it matches existing client-generation behavior on `main` and is not caused by this refactor.

---

### Task 6: Commit

**Files:**
- Stage: `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs`
- Stage: `backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs`

- [ ] **Step 1: Final sanity check of the staged diff**

```bash
git diff --stat backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs \
                backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs
git diff backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs \
        backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs
```

Expected diff content:
- `BankStatementImportDto.cs`: one added `using Anela.Heblo.Domain.Features.Bank;` line and one changed expression body. No other lines touched.
- `BankMappingProfileTests.cs`: two changed lines — the `ImportResult = "OK"` setup and the `dto.ImportResult.Should().Be("OK")` assertion in the first test. The second test (`…_Is_Not_OK_…`) is untouched.

If the diff contains anything else (other Bank files, reformatted whitespace elsewhere, the second test changed, etc.), **stop and revert the unintended changes** before committing.

- [ ] **Step 2: Stage and commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs \
        backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs

git commit -m "$(cat <<'EOF'
refactor: replace "OK" literal with ImportStatus.Success in BankStatementImportDto

Aligns the BankStatementImportDto.ErrorType expression with every other
Bank-module consumer that already references ImportStatus.Success.
Updates the existing BankMappingProfileTests assertion that used the
literal "OK" so the test stays correct if the constant is ever
renormalized in the future.
EOF
)"
```

Expected: Commit succeeds. Any pre-commit hook must pass without modification — if it fails, fix the underlying issue (do not use `--no-verify`) and create a new commit.

- [ ] **Step 3: Verify clean working tree**

```bash
git status
```

Expected: `nothing to commit, working tree clean`.

---

## Self-Review

**1. Spec coverage**

| Spec requirement | Implemented in |
|---|---|
| FR-1: `BankStatementImportDto` references `ImportStatus.Success` and no longer contains the literal `"OK"` | Task 2, Steps 1–2 |
| FR-1 acceptance: `ImportResult == ImportStatus.Success` ⇒ `ErrorType == null` | Existing test re-run in Task 2 Step 3 and Task 3 Step 4 |
| FR-1 acceptance: `ImportResult != ImportStatus.Success` ⇒ `ErrorType == ImportResult` | Existing `…_Is_Not_OK_…` test, re-run in Task 2 Step 3 and Task 3 Step 4 |
| FR-2: Public surface, nullability, class-not-record, TS client unchanged | Task 5 verification |
| FR-3 (as amended by arch review): Diff limited to the DTO and the test assertions previously using `"OK"` | Task 6 Step 1 (diff sanity check) |
| NFR-1: No performance impact | Implicit — `const string` substitution emits equivalent IL |
| NFR-2: No security impact | Implicit — no new data flows |
| NFR-3: `dotnet build` succeeds, `dotnet format` clean, Bank tests pass, TS client no-op | Tasks 4 and 5 |
| NFR-4: Existing `"OK"` payloads still classified as success | Existing test re-run in Task 3 Step 4 |
| Test Strategy (as amended): Reuse `BankMappingProfileTests`, update assertions to reference `ImportStatus.Success`, do not add a new test file | Task 3 |
| Arch Amendment 1: Update the two literal `"OK"` test lines (37, 42) | Task 3 Steps 1–2 |
| Arch Amendment 2: No new test file | Honored — no new file is created |
| Arch Amendment 3: Format scope limited to the two edited files | Task 4 Step 1 |

No gaps.

**2. Placeholder scan**

Searched the plan for: TBD, TODO, implement later, fill in details, add appropriate error handling, handle edge cases, write tests for the above, similar to Task N. None present. Every code step contains the exact `Old` / `New` text the engineer needs to apply.

**3. Type / symbol consistency**

- `ImportStatus.Success` used consistently in Tasks 2, 3, and 6 — matches the canonical declaration in `backend/src/Anela.Heblo.Domain/Features/Bank/ImportStatus.cs`.
- `using Anela.Heblo.Domain.Features.Bank;` namespace matches the file header of `ImportStatus.cs`.
- Test class name `BankMappingProfileTests` and method names `Map_BankStatementImport_To_Dto_When_ImportResult_Is_OK_Sets_ErrorType_To_Null` / `…_Is_Not_OK_…` match the on-disk file verbatim.
- Test project path `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` matches the actual project structure.
- Solution path `backend/Anela.Heblo.sln` used consistently in `dotnet format` and `dotnet build`.

No inconsistencies.
