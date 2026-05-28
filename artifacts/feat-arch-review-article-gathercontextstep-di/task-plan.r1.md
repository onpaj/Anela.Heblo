# Remove Unused `Severe` Member from `StockSeverity` Enum — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete the unreachable `Severe` member from `StockSeverity` in the Purchase module, regenerate the TypeScript API client, and prove no callers regressed.

**Architecture:** Single-file backend edit — remove one line from a transport-only enum declared next to its sole producer and consumer in the `Features/Purchase/UseCases/GetPurchaseStockAnalysis/` vertical slice. The auto-regenerated NSwag TS client picks up the removal; no frontend source edits are needed because no consumer ever branched on `Severe`. Enum serialization is string-based (`JsonStringEnumConverter` is registered in `Program.cs:142`), so the ordinal shift of remaining members is irrelevant on the wire.

**Tech Stack:** .NET 8 backend, `JsonStringEnumConverter` for enum serialization, NSwag for OpenAPI → TypeScript client generation (`prebuild` script on `npm run build`), React/TypeScript frontend.

---

## File Structure

This change touches exactly two files. The plan locks the scope to these — no other edits are permitted (the "surgical change" rule in `CLAUDE.md`).

- **Edit (backend):** `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs`
  - Responsibility: declares the `StockSeverity` transport enum used in `StockAnalysisItemDto.Severity` and the parent response DTO.
  - Change: delete the single line `    Severe,` (current line 99). Preserve the order of the remaining members exactly as on disk: `Critical, Low, Optimal, Overstocked, NotConfigured`.

- **Regenerate (frontend, do not hand-edit):** `frontend/src/api/generated/api-client.ts`
  - Responsibility: NSwag-generated TS client; consumed by `usePurchaseStockAnalysis.ts` and other Purchase-side helpers.
  - Change: the `Severe = "Severe",` line inside the `StockSeverity` enum (current line 34161) disappears when the NSwag prebuild runs. No other lines in this file may be modified by hand.

Explicitly **out of scope** (do not touch, even if you see them):
- `GiftPackageSeverity` enum and its `Severe` member (separate Logistics module, legitimately active).
- `usePurchaseStockAnalysis.ts` helpers `getSeverityColorClass` / `getSeverityDisplayText` — their `default` branch is now effectively unreachable but must remain as a defensive fallback for any wire value that arrives `undefined`.
- `docs/features/gift-package-manufacture.md:476` which contains a stale `StockSeverity.Severe` reference that should read `GiftPackageSeverity.Severe` — flag in PR description, fix in a separate docs PR.

---

## Pre-flight Verification (all prerequisites)

Run these before changing any code. If any check fails, stop and report — do not work around.

### Task 0: Verify prerequisites

**Files:** none (read-only checks)

- [ ] **Step 1: Confirm `JsonStringEnumConverter` is registered**

This is the single load-bearing assumption. If enums are serialized as integers, removing a middle member silently shifts ordinals (`Low` 2→1, etc.) and is a breaking change.

Run from repo root:

```bash
grep -nR "JsonStringEnumConverter" backend/src/Anela.Heblo.API/Program.cs
```

Expected output (line number may drift, the registration must exist):

```
backend/src/Anela.Heblo.API/Program.cs:142:                options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
```

If no match is returned, **stop**. The task expands to either registering the converter first or assigning explicit numeric values to all `StockSeverity` members before removing `Severe`. Report and wait for direction.

- [ ] **Step 2: Confirm no live references to `StockSeverity.Severe`**

Run from repo root:

```bash
grep -nR "StockSeverity.Severe" backend frontend/src --include="*.cs" --include="*.ts" --include="*.tsx"
```

Expected output: empty (no matches).

If any match is returned (excluding the enum declaration itself in `GetPurchaseStockAnalysisResponse.cs`), **stop** and report — there is a live consumer the spec did not anticipate.

- [ ] **Step 3: Confirm the NSwag toolchain is available**

Run from repo root:

```bash
dotnet tool restore
```

Expected: completes successfully (may print "Tools restored" or "All tools are already restored"). NSwag is required to regenerate the client per `docs/development/api-client-generation.md`.

- [ ] **Step 4: Capture a green backend baseline**

Run from repo root:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded.` with zero errors. Note the warning count — the post-change build must not increase it.

- [ ] **Step 5: Capture a green Purchase-module test baseline**

Run from repo root:

```bash
dotnet test backend/Anela.Heblo.sln --no-build --filter "FullyQualifiedName~Purchase"
```

Expected: all matched tests pass. Note the pass count — the post-change run must match it.

---

## Task 1: Remove `Severe` from the backend `StockSeverity` enum

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs` (lines 96–104)

- [ ] **Step 1: Read the current enum block to confirm the exact lines**

Read `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs` lines 96–104. Expected current state:

```csharp
public enum StockSeverity
{
    Critical,
    Severe,
    Low,
    Optimal,
    Overstocked,
    NotConfigured
}
```

If the on-disk content does not match this exactly, stop and reconcile before editing — the spec assumed this layout.

- [ ] **Step 2: Delete the `Severe,` line**

Apply this exact edit:

Old:

```csharp
public enum StockSeverity
{
    Critical,
    Severe,
    Low,
    Optimal,
    Overstocked,
    NotConfigured
}
```

New:

```csharp
public enum StockSeverity
{
    Critical,
    Low,
    Optimal,
    Overstocked,
    NotConfigured
}
```

No other line in this file changes. Do not reorder, rename, or assign explicit integer values to the remaining members.

- [ ] **Step 3: Verify the file content post-edit**

Read the same line range again. Expected: the enum now has exactly five members in the order `Critical, Low, Optimal, Overstocked, NotConfigured`.

- [ ] **Step 4: Verify no remaining references in the backend**

Run:

```bash
grep -nR "StockSeverity.Severe" backend --include="*.cs"
grep -nR "\bSevere\b" backend/src/Anela.Heblo.Application/Features/Purchase --include="*.cs"
```

Expected: both commands return empty output.

- [ ] **Step 5: Build the backend**

Run:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded.` with zero errors. Warning count must not exceed the baseline from pre-flight Step 4.

- [ ] **Step 6: Run Purchase-module tests**

Run:

```bash
dotnet test backend/Anela.Heblo.sln --no-build --filter "FullyQualifiedName~Purchase"
```

Expected: all tests pass; total count matches the baseline from pre-flight Step 5.

- [ ] **Step 7: Run the full backend test suite as a safety net**

Run:

```bash
dotnet test backend/Anela.Heblo.sln --no-build
```

Expected: all tests pass. This catches any cross-module test that happens to reference `StockSeverity` (none expected, but cheap to verify).

- [ ] **Step 8: Format**

Run:

```bash
dotnet format backend/Anela.Heblo.sln
```

Expected: no diff on `GetPurchaseStockAnalysisResponse.cs` (or, at most, trailing-whitespace normalization on the edited block). If the formatter rewrites unrelated files, revert those changes — they are out of scope for this PR.

---

## Task 2: Regenerate the TypeScript API client

**Files:**
- Regenerate: `frontend/src/api/generated/api-client.ts` (NSwag-managed; do not hand-edit)

- [ ] **Step 1: Run the manual NSwag regeneration target**

Per `docs/development/api-client-generation.md`, the deterministic regeneration target is:

```bash
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
```

Expected: completes successfully, prints a generation message, and writes `frontend/src/api/generated/api-client.ts`.

- [ ] **Step 2: Verify the `StockSeverity` enum no longer contains `Severe`**

Run:

```bash
grep -n -A 8 "^export enum StockSeverity" frontend/src/api/generated/api-client.ts
```

Expected output (line number may drift):

```
export enum StockSeverity {
    Critical = "Critical",
    Low = "Low",
    Optimal = "Optimal",
    Overstocked = "Overstocked",
    NotConfigured = "NotConfigured",
}
```

The line `Severe = "Severe",` must be absent. If it still appears, the regeneration did not run against the freshly built backend — rebuild and retry.

- [ ] **Step 3: Verify the unrelated `GiftPackageSeverity` enum is unchanged**

Run:

```bash
grep -n -A 8 "^export enum GiftPackageSeverity" frontend/src/api/generated/api-client.ts
```

Expected: `GiftPackageSeverity` still includes its own `Severe = "Severe",` member. If `GiftPackageSeverity.Severe` is missing, the regeneration corrupted an unrelated enum — stop and investigate.

- [ ] **Step 4: Confirm the diff is scoped**

Run:

```bash
git diff --stat frontend/src/api/generated/api-client.ts
git diff frontend/src/api/generated/api-client.ts | head -40
```

Expected: a single small hunk inside the `StockSeverity` enum block deleting one line. If the diff touches dozens of unrelated lines (header banner, formatting drift), that may still be acceptable for an auto-generated file, but flag any non-trivial unrelated structural changes in the commit message.

---

## Task 3: Verify frontend build, lint, and tests

**Files:** none modified (verification only)

- [ ] **Step 1: Run the frontend build (this also re-runs the prebuild generator)**

Run from repo root:

```bash
cd frontend && npm run build
```

Expected: build succeeds with no TypeScript errors. The `prebuild` script re-runs NSwag — confirm the `api-client.ts` diff from Task 2 Step 4 is unchanged after this build (i.e., the manual generation and the prebuild generator agree).

If `npm run build` modifies `api-client.ts` further, the new diff supersedes the manual one — capture it.

- [ ] **Step 2: Run the frontend linter**

Run from `frontend/`:

```bash
npm run lint
```

Expected: completes with zero new warnings or errors. Warnings already present on `main` are not part of this change.

- [ ] **Step 3: Run the frontend unit tests**

Run from `frontend/`:

```bash
npm test -- --watchAll=false
```

Expected: all tests pass. Pay particular attention to any test file under `frontend/src/components/pages/Purchase/` or anything importing `usePurchaseStockAnalysis` — these are the natural failure surfaces if anything broke.

- [ ] **Step 4: Final sanity grep for `StockSeverity.Severe`**

Run from repo root:

```bash
grep -nR "StockSeverity.Severe" backend frontend/src --include="*.cs" --include="*.ts" --include="*.tsx"
```

Expected: empty output. The only legitimate remaining `Severe` references in `frontend/src` should be the `GiftPackageSeverity.Severe` uses inside `frontend/src/components/pages/GiftPackageManufacturing/` (verify with `grep -nR "GiftPackageSeverity.Severe" frontend/src` if curious).

---

## Task 4: Commit

**Files:** all staged changes from Tasks 1 and 2.

- [ ] **Step 1: Inspect the staged surface**

Run:

```bash
git status
git diff --stat
```

Expected file list (exactly):

```
backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs
frontend/src/api/generated/api-client.ts
```

If any other file appears in the status, decide whether it is a legitimate side effect of the NSwag run (rare — generally only `api-client.ts` is regenerated) or accidental drift. Revert accidental drift before committing.

- [ ] **Step 2: Stage the two files**

Run:

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs \
  frontend/src/api/generated/api-client.ts
```

- [ ] **Step 3: Commit with a conventional-commits message**

Run:

```bash
git commit -m "$(cat <<'EOF'
refactor(purchase): remove dead StockSeverity.Severe member

StockSeverityCalculator never returned Severe, no consumer branched on
it, and it only leaked into the generated TypeScript client. Drop it
from the enum and regenerate the API client so the public contract
reflects only values the backend actually emits.

No behavior change — JsonStringEnumConverter is active so the on-wire
representation is by name, not ordinal. GiftPackageSeverity (separate
Logistics enum) is unaffected.

Note: docs/features/gift-package-manufacture.md:476 contains a stale
reference reading "StockSeverity.Severe" where it should say
"GiftPackageSeverity.Severe" — out of scope for this PR; tracked for a
separate docs fix.
EOF
)"
```

Expected: commit succeeds. The pre-commit hook (if any) must pass. If a hook fails, fix the underlying issue and create a NEW commit — never `--amend` after a hook failure.

- [ ] **Step 4: Verify the commit landed**

Run:

```bash
git log -1 --stat
```

Expected: the single commit shows exactly the two files from Step 1.

---

## Self-Review Notes

- **Spec coverage:**
  - FR-1 (remove `Severe` from backend enum) → Task 1.
  - FR-2 (regenerate TS client) → Task 2.
  - FR-3 (no frontend regression) → Task 3 Steps 1–4.
  - FR-4 (preserve `GiftPackageSeverity`) → Task 2 Step 3 and Task 3 Step 4.
  - NFR-4 (existing tests pass) → Task 1 Steps 6–7 and Task 3 Step 3.
  - Arch-review prerequisite 1 (verify `JsonStringEnumConverter`) → Task 0 Step 1.
  - Arch-review prerequisite 2 (NSwag toolchain) → Task 0 Step 3.
  - Arch-review amendment 1 (declaration order `Critical, Low, Optimal, Overstocked, NotConfigured`) → Task 1 Steps 1–2 use the on-disk order, not the spec's alphabetical-ish wording.

- **Placeholder scan:** none. Every command, file path, line range, and expected output is concrete.

- **Type / name consistency:** the enum members named in every task are `Critical, Low, Optimal, Overstocked, NotConfigured` (post-change) and `Critical, Severe, Low, Optimal, Overstocked, NotConfigured` (pre-change). No drift between tasks.
