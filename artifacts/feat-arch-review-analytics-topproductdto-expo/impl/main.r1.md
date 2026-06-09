All 9 tasks complete. Writing the output summary.

---

# Implementation: Remove TopProductDto Backward-Compatibility Shims

## What was implemented

Deleted the two computed shim properties (`ProductCode`, `ProductName`) and their `// Keep for backward compatibility` comment from `TopProductDto.cs`, regenerated the TypeScript OpenAPI client, and updated the one test fixture that mirrored the legacy field names.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/TopProductDto.cs` — removed `ProductCode`/`ProductName` shim getters and the backward-compat comment
- `frontend/src/api/generated/api-client.ts` — auto-regenerated via `dotnet msbuild -t:GenerateFrontendClientManual`; `ITopProductDto` and `TopProductDto` class no longer carry `productCode`/`productName` fields. Also picked up pre-existing stale diff (packaging endpoint parameter rename + new `PackingUserNotEligible` ErrorCode) that were already committed to the branch but not yet reflected in the generated client.
- `frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx` — renamed `productCode`→`groupKey` and `productName`→`displayName` in the `topProducts` fixture array (lines 67–68 only); `MonthlyProductSegmentDto` entries on lines 36–37 / 49–50 were left untouched.

## Tests

- **Backend**: `dotnet test` — 5,093 passed, 40 Docker/Testcontainers failures (pre-existing infrastructure issue, unrelated to this change). Zero failures referencing the removed shim properties.
- **Frontend**: `npm test -- --watchAll=false` — 2,068 passed, 5 skipped, 259 suites pass. `ProductMarginSummary.test.tsx` has a **pre-existing** Babel/jest.mock parse error that prevents it from running in Jest — it was already broken before this change. The TypeScript build confirms the fixture types are correct.

## How to verify

```bash
# Backend
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
dotnet test backend/Anela.Heblo.sln --nologo

# Frontend
cd frontend && npm run build && npm run lint
cd frontend && npm test -- --watchAll=false

# Confirm shims are gone
grep -n "ProductCode\|ProductName" backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/TopProductDto.cs
# Expected: no output

# Confirm client is clean
awk '/interface ITopProductDto/,/^}/' frontend/src/api/generated/api-client.ts | grep -E "productCode|productName"
# Expected: no output
```

## Notes

1. **`npm run generate-client` script is missing** — generation fell back to `dotnet msbuild -t:GenerateFrontendClientManual` as documented in the plan.
2. **Client diff includes pre-existing stale drift** — the committed `api-client.ts` was stale relative to prior backend commits on this branch (packaging endpoint parameter change, new `PackingUserNotEligible` ErrorCode). The regeneration correctly brings the client up to date with the current backend state. These additional diffs are legitimate and not caused by this task.
3. **`ProductMarginSummary.test.tsx` does not run in Jest** — a pre-existing Babel parse error in the `jest.mock()` call prevents the file from executing. The TypeScript compiler (via `npm run build`) validates the fixture types successfully. This is out of scope for this task.
4. **Follow-up**: `GetProductMarginSummaryHandler.ApplySorting` still accepts `"productcode"` / `"productname"` as sort-key string aliases (arch-review Decision 2 — intentionally out of scope per the spec).

## PR Summary

Removes the two backward-compatibility shim properties (`ProductCode`, `ProductName`) from `TopProductDto` that aliased the canonical `GroupKey`/`DisplayName` fields. These violated the project's no-backwards-compat-shim rule, inflated the generated TypeScript client with redundant read-only fields, and had no actual consumers — the handler only ever wrote to the canonical fields, and the single test fixture mirroring the legacy shape is renamed in the same commit.

The NSwag TypeScript client is regenerated to reflect the trimmed DTO. The regeneration also brings in two pre-existing stale diffs already committed to the backend (packaging endpoint parameter rename, new `PackingUserNotEligible` error code) that had not yet been reflected in the checked-in client.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/TopProductDto.cs` — deleted `ProductCode`/`ProductName` shim getters and the backward-compat comment
- `frontend/src/api/generated/api-client.ts` — regenerated; `ITopProductDto` interface and `TopProductDto` class no longer carry `productCode`/`productName`
- `frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx` — fixture `topProducts` entries renamed to `groupKey`/`displayName`; `MonthlyProductSegmentDto` entries unchanged

## Status

DONE_WITH_CONCERNS

Concerns:
- Pre-existing stale drift in `api-client.ts` (packaging + ErrorCode) is included in the regenerated client — correct behavior for a stale client, but reviewers should be aware the diff is wider than just TopProductDto.
- `ProductMarginSummary.test.tsx` has a pre-existing Babel parse error that prevents it from running in Jest; the fixture update is type-checked but not executed.