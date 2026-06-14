# Remove TopProductDto Backward-Compatibility Shims Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete the two backward-compatibility shim properties (`ProductCode`, `ProductName`) from `TopProductDto`, regenerate the TypeScript client, and update the single dependent frontend test fixture — so the DTO surface has exactly one canonical name per concept (`GroupKey`, `DisplayName`).

**Architecture:** Single atomic backend + frontend change. The producer (`GetProductMarginSummaryHandler`) already writes only canonical fields, and grep across `backend/src`, `backend/test`, and `frontend/src` confirms no production reader uses `.ProductCode` / `.ProductName` on a `TopProductDto` instance. Only one Jest fixture (`ProductMarginSummary.test.tsx`, lines 67–68) mirrors the legacy shape and must be updated together with the DTO. The generated `api-client.ts` is regenerated via NSwag (`npm run generate-client`) and committed alongside the source changes — no hand edits.

**Tech Stack:** .NET 8, C# (`Anela.Heblo.Application.Features.Analytics.Contracts`), NSwag (OpenAPI client gen), React + TypeScript, Jest, MSBuild.

**Scope guardrails (read before editing):**
- **Do NOT** modify `MarginCalculator.cs`, `ReportBuilderService.cs`, `ProductFilterService.cs`, `ProductMarginSegmentDto.cs`, `GetProductMarginAnalysis*.cs`, or `ProductMarginSummaryDto.cs`. These also reference `.ProductCode`/`.ProductName`, but on **different types** (domain objects or other DTOs), not on `TopProductDto`. They are out of scope per the spec.
- **Do NOT** touch lines 36–37 or 49–50 of `ProductMarginSummary.test.tsx`. Those entries belong to `MonthlyProductSegmentDto`, a different DTO that legitimately uses `productCode` / `productName`.
- **Do NOT** remove the `"productcode"` / `"productname"` sort-key string arms in `GetProductMarginSummaryHandler.ApplySorting` (arch-review Decision 2 — separate follow-up).

---

## File Map

| File | Action | Notes |
|---|---|---|
| `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/TopProductDto.cs` | Modify | Delete lines 25–27 (comment + two shim getters). |
| `frontend/src/api/generated/api-client.ts` | Auto-regenerated | Run `npm run generate-client`; commit the diff. **Do not hand-edit.** |
| `frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx` | Modify | Rename `productCode`→`groupKey`, `productName`→`displayName` **only** on lines 67–68 (inside `topProducts` fixture). |

---

## Task 1: Repo-wide pre-flight scan

Verify the three-file mental model is correct before any edits. Catch any hidden consumer (Razor view, integration fixture, seed file) that the spec's narrower grep would miss.

**Files:**
- Read only — no edits in this task.

- [ ] **Step 1: Run case-insensitive grep across the repo**

Run from the repo root:

```bash
grep -rni --include="*.cs" --include="*.ts" --include="*.tsx" --include="*.cshtml" --include="*.json" \
  -e "\.ProductCode\b" -e "\.ProductName\b" \
  -e "\.productCode\b" -e "\.productName\b" \
  backend frontend docs 2>/dev/null | grep -v "node_modules\|/bin/\|/obj/\|api-client.ts"
```

- [ ] **Step 2: Filter the result mentally to TopProductDto context**

For each hit, look at the variable / type to the left of `.ProductCode` or `.ProductName`. A hit is **in scope** only if the receiver is a `TopProductDto` (C#) or an `ITopProductDto` / object spread destined for `topProducts` (TS). All other hits (e.g., `MonthlyProductSegmentDto`, `ProductMarginSegmentDto`, `AnalyticsProduct`, `CatalogItemDto`) are out of scope.

Expected in-scope hits (these are the only ones the arch-review identified):
- `backend/.../Contracts/TopProductDto.cs:26` (the shim itself — to be deleted in Task 2)
- `backend/.../Contracts/TopProductDto.cs:27` (the shim itself — to be deleted in Task 2)
- `frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx:67` (test fixture — to be updated in Task 5)
- `frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx:68` (test fixture — to be updated in Task 5)

If grep reveals **any other** in-scope hit, STOP and report it before proceeding — the plan needs to be extended.

- [ ] **Step 3: No commit. This is a verification-only task.**

---

## Task 2: Delete the shim properties from TopProductDto

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/TopProductDto.cs`

- [ ] **Step 1: Read the current DTO**

Read `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/TopProductDto.cs` and confirm lines 25–27 read exactly:

```csharp
    // Keep for backward compatibility
    public string ProductCode => GroupKey;
    public string ProductName => DisplayName;
```

- [ ] **Step 2: Delete lines 25–27**

Use Edit to remove the comment and the two computed properties. The block to delete (with the trailing empty-line context) is:

```csharp

    // Keep for backward compatibility
    public string ProductCode => GroupKey;
    public string ProductName => DisplayName;
}
```

Replace with:

```csharp
}
```

The resulting file must read exactly:

```csharp
namespace Anela.Heblo.Application.Features.Analytics.Contracts;

public class TopProductDto
{
    public string GroupKey { get; set; } = string.Empty; // Product code, family, or type key
    public string DisplayName { get; set; } = string.Empty; // Display name for the group
    public decimal TotalMargin { get; set; } // Total margin across entire time period
    public string ColorCode { get; set; } = string.Empty;
    public int Rank { get; set; }

    // M0-M2 margin levels - amounts (for sorting)
    public decimal M0Amount { get; set; }
    public decimal M1Amount { get; set; }
    public decimal M2Amount { get; set; }

    // M0-M2 margin levels - percentages (for sorting)
    public decimal M0Percentage { get; set; }
    public decimal M1Percentage { get; set; }
    public decimal M2Percentage { get; set; }

    // Pricing (for sorting)
    public decimal SellingPrice { get; set; }
    public decimal PurchasePrice { get; set; }
}
```

- [ ] **Step 3: Build the backend**

Run from the repo root:

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Expected: **Build succeeded. 0 Errors.** (Warnings about unrelated areas are acceptable.)

If the build fails because some file references `TopProductDto.ProductCode` or `.ProductName`, the Task 1 pre-flight scan missed a consumer. Stop, identify it, decide whether it's truly a `TopProductDto` reader (in scope) or an unrelated type matching the same name (likely a Task 1 false-negative), and amend this plan with a new task if needed.

- [ ] **Step 4: Format the changed file**

```bash
dotnet format backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj --include backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/TopProductDto.cs
```

Expected: no output (formatter finds nothing to change) or only whitespace fixups inside the edited file.

- [ ] **Step 5: Do NOT commit yet** — backend, generated client, and frontend test must land in one atomic commit per arch-review Decision 1.

---

## Task 3: Regenerate the TypeScript client

The NSwag pipeline regenerates `frontend/src/api/generated/api-client.ts` from the API's OpenAPI spec. Per `docs/development/api-client-generation.md`, the canonical manual command is `npm run generate-client` from `frontend/`.

**Files:**
- Auto-regenerated: `frontend/src/api/generated/api-client.ts`

- [ ] **Step 1: Snapshot the current generated client**

Capture the current state so the diff in Task 4 only reflects this change:

```bash
git status frontend/src/api/generated/api-client.ts
```

Expected: file is **unmodified** (clean tree before regeneration). If it shows local edits, stop — those edits must be reconciled before regenerating.

- [ ] **Step 2: Run client generation**

```bash
cd frontend && npm run generate-client
```

Expected: NSwag prints generation progress and exits 0. The `api-client.ts` file is rewritten.

If `npm run generate-client` is not available (script missing or NSwag misconfigured), fall back to:

```bash
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
```

- [ ] **Step 3: Do NOT commit yet** — verify the diff in Task 4.

---

## Task 4: Verify the regenerated client diff is scoped

NSwag regeneration can occasionally introduce unrelated diffs if the OpenAPI surface drifted. Confirm the diff is limited to `TopProductDto` removals.

**Files:**
- Inspect: `frontend/src/api/generated/api-client.ts`

- [ ] **Step 1: Inspect the diff**

```bash
git diff frontend/src/api/generated/api-client.ts | head -200
```

- [ ] **Step 2: Confirm the diff contains ONLY**

Expected in the diff (acceptable changes):
- Deleted lines inside the `class TopProductDto` definition that previously declared `productCode` and `productName` properties and their initializers/getters.
- Deleted lines inside the `interface ITopProductDto` definition for the same two fields.

Unacceptable changes (must NOT be present):
- Any rename of unrelated types.
- Any signature changes to controller methods (`catalog_*`, `analytics_*`, etc.).
- Any reordering of unrelated interfaces or enums.
- Any change to other DTOs (e.g., `MonthlyProductSegmentDto`, `ProductMarginSegmentDto`).

- [ ] **Step 3: If the diff includes unrelated changes**

Reset the generated file and rebuild from a clean backend state:

```bash
git checkout -- frontend/src/api/generated/api-client.ts
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
cd frontend && npm run generate-client
git diff frontend/src/api/generated/api-client.ts | head -200
```

Re-verify. If unrelated diffs persist, those came from a prior uncommitted backend change in this worktree, not from this task — investigate before continuing.

- [ ] **Step 4: Confirm `productCode` / `productName` are gone from the `TopProductDto` block specifically**

```bash
awk '/class TopProductDto/,/^}/' frontend/src/api/generated/api-client.ts | grep -E "productCode|productName"
awk '/interface ITopProductDto/,/^}/' frontend/src/api/generated/api-client.ts | grep -E "productCode|productName"
```

Expected: **both commands print nothing.**

- [ ] **Step 5: Do NOT commit yet.**

---

## Task 5: Update the frontend test fixture

Only the `topProducts` array entries (lines 67–68) need renaming. The `monthlyData[0].productSegments` entries (lines 36–37 and 49–50) belong to a different DTO and must remain untouched.

**Files:**
- Modify: `frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx` (lines 65–73)

- [ ] **Step 1: Read lines 60–80 of the test file**

Confirm the current `topProducts` block matches:

```typescript
  topProducts: [
    {
      productCode: "PROD001",
      productName: "Product 1",
      totalMargin: 1500,
      colorCode: "#2563EB",
      rank: 1,
    },
  ],
```

- [ ] **Step 2: Edit the fixture**

Use Edit with `old_string`:

```typescript
  topProducts: [
    {
      productCode: "PROD001",
      productName: "Product 1",
      totalMargin: 1500,
      colorCode: "#2563EB",
      rank: 1,
    },
  ],
```

and `new_string`:

```typescript
  topProducts: [
    {
      groupKey: "PROD001",
      displayName: "Product 1",
      totalMargin: 1500,
      colorCode: "#2563EB",
      rank: 1,
    },
  ],
```

Because this exact 9-line block appears only once in the file (it's the `topProducts` fixture), the Edit is unambiguous and will not touch the `productSegments` entries above.

- [ ] **Step 3: Verify lines 36–37 and 49–50 were NOT changed**

```bash
sed -n '34,52p' frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx
```

Expected: both `productSegments` entries still contain `productCode:` and `productName:` keys. If those were modified, undo and redo the Edit with a tighter `old_string`.

- [ ] **Step 4: Confirm no remaining `productCode` / `productName` keys exist inside the `topProducts` block**

```bash
awk '/topProducts: \[/,/\],/' frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx | grep -E "productCode|productName"
```

Expected: prints nothing.

- [ ] **Step 5: Do NOT commit yet.**

---

## Task 6: Run backend tests

The producer (`GetProductMarginSummaryHandler`) already populates only canonical fields, so backend behavior is unchanged. This task confirms no test fixture or assertion in the backend layer depended on the shim getters.

**Files:**
- No edits — test execution only.

- [ ] **Step 1: Run the full backend test suite**

```bash
dotnet test backend/Anela.Heblo.sln --no-build --nologo
```

(Use `--no-build` to reuse the build from Task 2. If `dotnet test` complains about needing a rebuild, drop the flag.)

Expected: **All tests pass.** Exit code 0.

- [ ] **Step 2: If a test fails referencing `.ProductCode` or `.ProductName` on a `TopProductDto`**

Task 1's grep missed it. Fix the assertion to use `.GroupKey` / `.DisplayName` and re-run. Do NOT add the shim back. Do NOT commit until tests are green.

- [ ] **Step 3: Do NOT commit yet.**

---

## Task 7: Run frontend lint, build, and tests

**Files:**
- No edits — verification only.

- [ ] **Step 1: Lint**

```bash
cd frontend && npm run lint
```

Expected: exit code 0, no errors. Warnings unrelated to this change are acceptable.

- [ ] **Step 2: Type-check + build (also re-runs the prebuild client generator — confirms idempotency)**

```bash
cd frontend && npm run build
```

Expected: build succeeds, no TS errors. The prebuild step will regenerate `api-client.ts` again; the resulting file should be byte-identical to the one already on disk (no further git diff).

- [ ] **Step 3: Run the affected Jest test specifically**

```bash
cd frontend && npx jest src/components/pages/__tests__/ProductMarginSummary.test.tsx --no-coverage
```

Expected: all tests in this file pass.

- [ ] **Step 4: Run the full frontend test suite**

```bash
cd frontend && npm test -- --watchAll=false
```

Expected: all tests pass. If a test elsewhere fails referencing `productCode` / `productName` on `ITopProductDto`, Task 1's grep missed it — fix the assertion to use `groupKey` / `displayName` and re-run.

- [ ] **Step 5: Do NOT commit yet.**

---

## Task 8: Manual smoke check of the Analytics screens (FR-5)

`ProductMarginSummary.tsx` and any sibling components consuming `topProducts` already read the canonical `groupKey` / `displayName` fields (confirmed by the arch-review). This is a final visual sanity check — fast, since no UI logic changed.

**Files:**
- No edits — manual verification.

- [ ] **Step 1: Confirm the page uses canonical fields**

```bash
grep -nE "topProducts\[" frontend/src/components/pages/ProductMarginSummary.tsx
grep -nE "\.productCode|\.productName" frontend/src/components/pages/ProductMarginSummary.tsx
```

Expected: the first command shows references reading `groupKey` / `displayName` from `topProducts`; the second command prints nothing.

If the second command prints anything, the page is reading the now-removed shim fields and the analytics view will render `undefined` — the spec / arch-review missed a consumer. STOP and add a new task to rename those reads, then re-run Tasks 6 and 7.

- [ ] **Step 2: (Optional, low-confidence-only) start the dev server and load the Analytics page**

Skip if Step 1 passes — the arch-review already verified consumer cleanliness across the codebase and the unit test in Task 7 exercises rendering with the renamed fixture.

If running the smoke check anyway:

```bash
cd frontend && npm start
```

Open the Analytics / Product Margin Summary screen. Confirm the top-products table renders product codes and names. Confirm the browser console shows no `undefined` warnings related to `productCode` / `productName`.

Stop the dev server (Ctrl-C) when done.

- [ ] **Step 3: No commit. Proceed to Task 9.**

---

## Task 9: Commit the atomic change

Per arch-review Decision 1, all three diffs land in one commit: the backend DTO, the regenerated client, and the test fixture.

**Files:**
- Stage and commit: TopProductDto.cs, api-client.ts, ProductMarginSummary.test.tsx.

- [ ] **Step 1: Stage the three files explicitly**

Avoid `git add -A` to keep the commit surgical.

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/TopProductDto.cs \
  frontend/src/api/generated/api-client.ts \
  frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx
```

- [ ] **Step 2: Verify the staged set matches expectation**

```bash
git status --short
git diff --cached --stat
```

Expected: exactly **three** files staged. The working tree should be clean except for any files you intentionally chose not to commit (none expected here). If `git status` shows extra modified files, decide whether they belong to this commit (they should not).

- [ ] **Step 3: Commit**

```bash
git commit -m "$(cat <<'EOF'
refactor(analytics): remove TopProductDto ProductCode/ProductName shims

The ProductCode and ProductName computed properties on TopProductDto were
read-only aliases for GroupKey and DisplayName, kept under a "backward
compatibility" comment. They violate the project's no-backwards-compat-shim
rule, surface as redundant fields in the generated TypeScript client, and
no producer or consumer in the repo depends on them — the handler only
populates the canonical fields, and the single test fixture mirroring the
legacy shape is renamed alongside.

Regenerates the NSwag TypeScript client to drop the shim fields from
ITopProductDto.

Out of scope (follow-up): the "productcode" / "productname" sort-key
string aliases in GetProductMarginSummaryHandler.ApplySorting.
EOF
)"
```

- [ ] **Step 4: Confirm the commit landed**

```bash
git log -1 --stat
```

Expected: the most recent commit lists exactly the three files above.

---

## Verification Summary

After Task 9, all spec acceptance criteria are satisfied:

- **FR-1** (remove shims): `TopProductDto.cs` no longer declares `ProductCode` / `ProductName`; the comment is gone. Backend builds (Task 2).
- **FR-2** (backend call-sites): No backend reference to `.ProductCode` / `.ProductName` on a `TopProductDto` remains. `dotnet test` and `dotnet format` pass (Tasks 2, 6).
- **FR-3** (regenerate TS client): `npm run generate-client` regenerated the client; `ITopProductDto` no longer carries `productCode` / `productName`. `npm run build` passes (Tasks 3, 4, 7).
- **FR-4** (frontend call-sites): Only the `topProducts` fixture entry was updated; lint, build, and tests pass (Tasks 5, 7).
- **FR-5** (no UI regression): Consumer scan confirms `ProductMarginSummary.tsx` reads canonical fields; unit test passes with renamed fixture (Tasks 7, 8).
- **NFR-3** (maintainability): One canonical name per concept now exists on `TopProductDto`.

## Rollback

If a regression surfaces after merge, revert the single commit:

```bash
git revert <commit-sha>
```

The revert restores all three files atomically — no manual reconciliation needed.
