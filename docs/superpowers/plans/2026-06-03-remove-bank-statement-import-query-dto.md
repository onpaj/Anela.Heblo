# Remove Unused `BankStatementImportQueryDto` Dead Code — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete the orphaned `BankStatementImportQueryDto` class from the Bank module and update the one doc that still references it, eliminating the lure of a misleading non-authoritative query contract.

**Architecture:** Pure backend dead-code deletion in the Clean Architecture / Vertical Slice `Features/Bank/` module. The authoritative query contract `GetBankStatementListRequest` (in `UseCases/GetBankStatementList/`) is a strict superset of the dead DTO and remains untouched. A small in-place edit to `docs/features/comgate.md` aligns documentation with the surviving type.

**Tech Stack:** .NET 8, C# 12, MediatR, OpenAPI/NSwag client generation, `git rm` for the deletion, `dotnet build` / `dotnet format` / `dotnet test` for validation.

---

## File Structure

**Files to delete:**
- `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportQueryDto.cs` — the unused DTO; no references anywhere in the codebase.

**Files to modify:**
- `docs/features/comgate.md` — the section "Query API: GET /api/bank-statements" (lines 174–182) names `BankStatementImportQueryDto` as the query-parameter shape and lists only 3 of the 11 real filter properties. Rewrite to name `GetBankStatementListRequest` and list its actual properties.

**Files that must NOT change (per spec FR-2):**
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/Bank/Validators/GetBankStatementListRequestValidator.cs`
- `backend/src/Anela.Heblo.Application/Features/Bank/BankMappingProfile.cs`
- `backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs`
- The OpenAPI-generated TypeScript client (the deleted DTO was never on a controller, so `swagger.json` and the regenerated client must come out byte-identical).

---

## Task 1: Verify the DTO is truly unreferenced before deleting

**Files:**
- Read: `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportQueryDto.cs`

**Why this task exists:** Both the spec and arch review claim repository-wide grep returns zero non-declaring hits in code. Re-run the grep yourself so you have first-hand evidence before deleting. This is the cheap safety net mentioned in the arch review's risk table.

- [ ] **Step 1: Confirm the file is the only code-side reference**

Run:
```bash
grep -rn "BankStatementImportQueryDto" backend/ frontend/
```

Expected output: exactly one hit — the declaration line in `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportQueryDto.cs`. If you see any other hit (handler, controller, mapper, validator, test, frontend), STOP and surface it before continuing — the spec's premise no longer holds.

- [ ] **Step 2: Confirm doc-side references are only in `docs/features/comgate.md`**

Run:
```bash
grep -rn "BankStatementImportQueryDto" docs/
```

Expected output: hits only inside `docs/features/comgate.md` (the arch review identifies line 176). If any other doc names the type, add it to the list of files Task 3 must update — do not silently leave a stale reference behind.

- [ ] **Step 3: Confirm no tests reference the type**

Run:
```bash
grep -rn "BankStatementImportQueryDto" backend/test/
```

Expected output: zero hits.

---

## Task 2: Delete the unused DTO file

**Files:**
- Delete: `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportQueryDto.cs`

- [ ] **Step 1: Remove the file via `git rm`**

Run:
```bash
git rm backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportQueryDto.cs
```

Expected output: `rm 'backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportQueryDto.cs'`. Use `git rm` (not a filesystem delete + `git add -A`) so the deletion is staged cleanly and no unrelated files get picked up. This matches arch-review Decision 2.

- [ ] **Step 2: Verify the file is gone from the working tree**

Run:
```bash
ls backend/src/Anela.Heblo.Application/Features/Bank/Contracts/
```

Expected output: four files remain — `BankAccountDto.cs`, `BankImportRequestDto.cs`, `BankStatementImportDto.cs`, `BankStatementImportResultDto.cs`. `BankStatementImportQueryDto.cs` must NOT appear.

- [ ] **Step 3: Verify the deletion is staged**

Run:
```bash
git status
```

Expected output: `deleted: backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportQueryDto.cs` under "Changes to be committed".

---

## Task 3: Update the stale doc reference in `docs/features/comgate.md`

**Files:**
- Modify: `docs/features/comgate.md` — the "Query API: GET /api/bank-statements" subsection around lines 174–182.

**Why this task exists:** Arch-review Decision 3 / Specification Amendment FR-4. The spec's `Out of Scope` claim "no docs reference `BankStatementImportQueryDto`" is factually wrong — `docs/features/comgate.md:176` names the type and lists only 3 of its 11 properties. Leaving the doc referencing a deleted type creates a worse inconsistency than the dead DTO itself and would lure a reviewer or future contributor into "restoring" the missing type.

- [ ] **Step 1: Replace the stale block with the corrected version**

The current block (lines 174–182, do not include line numbers in the file) reads:

```markdown
### Query API: GET /api/bank-statements

**Query parametry (BankStatementImportQueryDto)**:
- **Id**: Filtrace podle ID záznamu
- **StatementDate**: Filtrace podle data výpisu  
- **ImportDate**: Filtrace podle data importu
- Standardní ABP parametry pro stránkování a řazení

**Výstup**: Stránkovaný seznam BankStatementImportDto
```

Replace it with:

```markdown
### Query API: GET /api/bank-statements

**Query parametry (GetBankStatementListRequest)**:
- **Id**: Filtrace podle ID záznamu
- **TransferId**: Filtrace podle ID transferu
- **Account**: Filtrace podle účtu
- **StatementDate**: Filtrace podle data výpisu
- **ImportDate**: Filtrace podle data importu
- **DateFrom**: Spodní hranice rozsahu data
- **DateTo**: Horní hranice rozsahu data
- **ErrorsOnly**: Filtrace pouze chybných záznamů
- **Skip**, **Take**: Stránkování (default `Skip = 0`, `Take = 10`)
- **OrderBy**, **Ascending**: Řazení (default `OrderBy = "ImportDate"`, `Ascending = false`)

**Výstup**: Stránkovaný seznam BankStatementImportDto
```

The property list mirrors `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListRequest.cs` exactly — all 11 properties with their actual defaults. Do not paraphrase the property names; the doc must match the class so a future grep for `GetBankStatementListRequest` reaches both.

- [ ] **Step 2: Verify no `BankStatementImportQueryDto` references remain anywhere**

Run:
```bash
grep -rn "BankStatementImportQueryDto" .
```

Expected output: **zero hits**. This is the FR-3 verification step strengthened by FR-4: the type name must be gone from both code and docs. If even one hit remains, STOP and fix it before moving on.

- [ ] **Step 3: Stage the doc edit**

Run:
```bash
git add docs/features/comgate.md
```

Expected output: silent success.

---

## Task 4: Validate build, format, and tests

**Files:** None modified in this task — verification only.

**Why this task exists:** FR-3 acceptance criteria. The change is compile-time-only and should be invisible to runtime, but the build is the cheap safety net against the "hidden runtime reference" risk in the arch review.

- [ ] **Step 1: Confirm the backend builds with no new warnings**

Run from the repo root:
```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded` with **zero new errors and zero new warnings** attributable to this change. A pre-existing warning count that stays the same is fine; any new diagnostic mentioning `BankStatementImportQueryDto`, the Bank module, or the deleted file path is a regression that must be investigated before continuing.

- [ ] **Step 2: Confirm formatting is clean**

Run:
```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: exit code 0 and no output, meaning no formatting changes are required. If it reports changes, run `dotnet format backend/Anela.Heblo.sln` to apply them and re-run `--verify-no-changes` until clean.

- [ ] **Step 3: Run the Bank-module tests**

Run:
```bash
dotnet test backend/Anela.Heblo.sln --no-build --filter "FullyQualifiedName~Bank"
```

Expected: all matched tests pass. If the filter matches zero tests (unlikely but possible depending on the assembly layout), fall back to running the full backend test suite: `dotnet test backend/Anela.Heblo.sln --no-build`.

- [ ] **Step 4: Confirm OpenAPI/TypeScript client is unaffected**

The deleted DTO was never wired into a controller, so the regenerated TypeScript client must come out byte-identical. After the build in Step 1 (which regenerates the client), run:
```bash
git status frontend/
```

Expected: no changes under `frontend/`. If `git status` shows any modification under `frontend/src/api-client/` or wherever the generated client lives, STOP — that means the dead DTO was reachable from the OpenAPI surface in a way the static grep missed, and you need to investigate before committing.

---

## Task 5: Commit and push

**Files:** Staged deletions and edits from Tasks 2 and 3.

- [ ] **Step 1: Sanity-check the staged diff**

Run:
```bash
git diff --staged
```

Expected: exactly two changes — the deletion of `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportQueryDto.cs` and the small edit to `docs/features/comgate.md`. Nothing else should appear in the diff. If anything unrelated is staged, unstage it with `git restore --staged <file>` before committing.

- [ ] **Step 2: Commit**

Run:
```bash
git commit -m "refactor: remove unused BankStatementImportQueryDto dead code

The DTO was never instantiated or referenced anywhere in the codebase.
The active query contract is GetBankStatementListRequest in
UseCases/GetBankStatementList/, which is a strict superset of this type.

Also updates docs/features/comgate.md to name GetBankStatementListRequest
as the documented query-parameter shape and lists its actual 11 filter
and paging properties."
```

Expected: a single new commit on the current branch. The commit message follows the conventional-commit `refactor:` prefix because no behavior changes — only dead code is removed.

- [ ] **Step 3: Push the branch**

Run:
```bash
git push
```

Expected: branch is pushed to the existing upstream tracking remote. If the branch has no upstream, push with `-u`: `git push -u origin HEAD`.

---

## Self-Review

**Spec coverage:**

- **FR-1 (delete the DTO file):** Task 2 deletes via `git rm`, verifies via `ls`, and Task 1 Step 1 + Task 3 Step 2 jointly enforce the "zero hits across the repo" criterion.
- **FR-2 (preserve `GetBankStatementListRequest` and its handler/validator/tests):** The "Files that must NOT change" list in **File Structure** spells them out by path. Task 5 Step 1 (sanity-check the staged diff) catches any accidental touch.
- **FR-3 (verify build and tests after removal):** Task 4 covers `dotnet build` (Step 1), `dotnet format --verify-no-changes` (Step 2), Bank-module tests (Step 3), and OpenAPI client byte-identity (Step 4).
- **FR-4 from arch-review Specification Amendments (update stale doc reference):** Task 3 rewrites the `docs/features/comgate.md` section and Task 3 Step 2 verifies zero remaining `BankStatementImportQueryDto` hits anywhere.
- **NFR-1 / NFR-2 / NFR-3 / NFR-4:** All trivially satisfied — no behavior, security, performance, or backwards-compatibility surface changes; covered by the no-runtime-reference grep + build verification.

**Placeholder scan:** No "TBD", no "implement later", no "similar to Task N", no abstract "add appropriate error handling" or "write tests for the above". The two code-style steps (the doc rewrite in Task 3 Step 1 and the commit message in Task 5 Step 2) contain the actual final text.

**Type consistency:** The doc rewrite in Task 3 lists exactly the 11 properties present on `GetBankStatementListRequest` (`Id`, `TransferId`, `Account`, `StatementDate`, `ImportDate`, `DateFrom`, `DateTo`, `ErrorsOnly`, `Skip`, `Take`, `OrderBy`, `Ascending`) with their actual defaults from the class file. Property names are spelled the same as the C# source so cross-referencing via grep works.

**Atomicity:** The whole change is a single commit (Task 5 Step 2) — deletion + doc fix together — per arch-review Decision 3, so the doc never references a deleted type in any intermediate state on the branch.
