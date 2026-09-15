### task: verify-build-and-contract-regen

**Files:**
- No files modified in this task — verification only.
- Generated (build output, not hand-edited): `frontend/src/api/generated/api-client.ts`

- [ ] **Step 1: Full backend build**

Run:
```bash
cd backend
dotnet build
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (or the same warning count as before this change — no new warnings introduced by the two `[Required]` additions).

- [ ] **Step 2: Backend format check**

Run:
```bash
cd backend
dotnet format --verify-no-changes
```
Expected: exits 0 with no reported formatting violations. If it reports a violation on either edited file, run `dotnet format` (no `--verify-no-changes`) to auto-fix, then re-stage and amend the relevant commit from the task that touched that file before moving on.

- [ ] **Step 3: Full backend test suite for the Journal module**

Run:
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Journal" -v minimal
```
Expected: all Journal-related tests PASS, including both `CreateJournalEntryHandlerTests` and `UpdateJournalEntryHandlerTests` from the two tasks above. No test file is modified as part of this plan (per spec Out of Scope) — this step only re-confirms the full Journal test slice together, since prior steps ran the two handler test classes individually.

- [ ] **Step 4: Frontend build (regenerates the OpenAPI client)**

Run:
```bash
cd frontend
npm run build
```
Expected: build succeeds. Per `docs/development/api-client-generation.md`, this regenerates `frontend/src/api/generated/api-client.ts` from the backend's OpenAPI spec as a build step — do not hand-edit this file.

- [ ] **Step 5: Confirm the regenerated client now marks `title` as required on both DTOs**

Run:
```bash
grep -n "title" frontend/src/api/generated/api-client.ts | grep -i "CreateJournalEntryRequest\|UpdateJournalEntryRequest" -A2 -B2
```
Or more directly, inspect the two generated classes:
```bash
grep -n "title" frontend/src/api/generated/api-client.ts
```
Expected: the `title` property on the generated `CreateJournalEntryRequest` and `UpdateJournalEntryRequest` classes (and their `I...` interfaces) is emitted as non-optional (e.g. `title!: string;` on the classes, `title: string;` on the interfaces), matching the existing `content!: string;` pattern in the same file — no more `title?: string;` for these two types. If it still shows `title?: string;`, the build in Step 4 did not pick up the backend's rebuilt OpenAPI spec; rebuild the backend (Step 1) before re-running Step 4.

- [ ] **Step 6: Frontend lint**

Run:
```bash
cd frontend
npm run lint
```
Expected: no new lint errors. This plan makes no hand-edits to frontend source — only the generated client changes — so this step is a safety check, not expected to surface anything.

- [ ] **Step 7: Commit the regenerated client if the build step leaves it modified in the working tree**

Run:
```bash
git status --porcelain frontend/src/api/generated/api-client.ts
```
If it shows as modified:
```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "chore(journal): regenerate api client for required Title"
```
If the project's build/CI pipeline regenerates this file itself and does not expect it committed (check whether it is already tracked and previously-committed generated output, per `docs/development/api-client-generation.md`), skip this step — do not commit a generated file that the project's convention keeps out of version control. Follow whatever the existing tracked/untracked status of this file already establishes as the project's convention; do not change that convention as part of this fix.

---

## Self-Review

**1. Spec coverage:**
- FR-1 (add `[Required]` to `Title` on `CreateJournalEntryRequest`, preserve `MaxLength`/other properties) → `add-required-title-create`, Step 2.
- FR-2 (same for `UpdateJournalEntryRequest`) → `add-required-title-update`, Step 2.
- FR-3 (preserve existing handler tests unmodified, still passing) → both tasks' Steps 1 and 3, plus the full-suite re-run in `verify-build-and-contract-regen` Step 3. No test file is created or edited anywhere in this plan, matching spec's Out of Scope.
- NFR-1 (contract accuracy: OpenAPI spec + TS client reflect required `title`) → `verify-build-and-contract-regen`, Steps 4-5.
- NFR-2 (no behavioral regression for valid requests / existing handler-level rejection path) → covered by the unmodified handler tests continuing to pass (both tasks' Step 3, and the full Journal suite in the verify task); the one caller-visible nuance already flagged and accepted in `spec.r1.md`/`arch-review.r1.md` (missing/blank `title` in the raw JSON body now short-circuits at `[ApiController]` model validation instead of reaching the handler) requires no code change per the architecture review's Decision 1, so no task attempts to change that framework-level behavior.
- Out of Scope items (handler/domain/controller/frontend-form changes, hand-editing the generated client, new tests) → correctly absent from all three tasks.

**2. Placeholder scan:** No "TBD"/"TODO"/"implement later" language; every code change is shown in full (both the before and after `Title` property block); every command has an explicit expected result.

**3. Type consistency:** `Title` stays `string` (`= null!`) in both DTOs — no signature change beyond the added attribute. No new method or function names are introduced anywhere in this plan, so there is nothing to drift between tasks.

No gaps found; no tasks added during self-review beyond the three above.
