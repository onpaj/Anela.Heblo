# Specification: Permission-gate the invoice-import-statistics and bank-statements routes

## Summary
Two frontend routes — `/automation/invoice-import-statistics` and `/finance/bank-statements` — are reachable by any authenticated user even though their backing API (`AnalyticsController`) requires the `Finance_MarginAnalysis` permission on every action. Because the routes lack both a menu-permission entry in `access-matrix.json` and a `guard(...)` wrapper in `App.tsx`, unauthorized users load a page that fails outright with a 403 on data fetch instead of never reaching it. This spec adds the missing `access-matrix.json` menu-path entries, regenerates the five derived artifacts, and wraps both routes with `guard(...)`, bringing them in line with every sibling route.

## Background
`AnalyticsController` (`backend/src/Anela.Heblo.API/Controllers/AnalyticsController.cs:14`) carries a class-level `[FeatureAuthorize(Feature.Finance_MarginAnalysis)]`, so all five of its actions — including `GET /api/Analytics/invoice-import-statistics` and `GET /api/Analytics/bank-statement-import-statistics` — require the `finance.margin_analysis.read` permission and return 403 without it.

The frontend's permission gating is driven by `access-matrix.json` (repo root), the single source of truth for menu-path → feature-permission mappings. It is compiled by `backend/tools/Anela.Heblo.AccessMatrixGen` into five generated artifacts, one of which is `frontend/src/auth/accessMatrix.generated.ts` (`ACCESS_ROUTES`). `App.tsx` wraps a route with the `guard(path, element)` helper (backed by `RequireMenuPath`), which looks up `path` in `ACCESS_ROUTES` and redirects to `/` if the current user lacks the required permission(s). If `ACCESS_ROUTES` has no entry for a path, `RequireMenuPath` also redirects — but only if the route is wrapped in `guard(...)` at all.

Investigation confirms both problems are present simultaneously for these two routes:
1. **No `access-matrix.json` menu-path entry** exists for `/automation/invoice-import-statistics` or `/finance/bank-statements` (confirmed absent from both `access-matrix.json` and the generated `AccessMatrix.generated.cs` / `accessMatrix.generated.ts`).
2. **Neither route is wrapped in `guard(...)`** in `App.tsx` (lines 415 and 445), unlike every sibling route in the same route list (e.g. `/logistics/packing-materials` at line 443, `/customer/issued-invoices` at line 451).

Both routes are reachable today via dashboard tiles (`invoiceimportstatistics` and `bankstatementimportstatistics` tiles in `frontend/src/components/dashboard/tiles/tileRegistry.tsx`, lines 66–85, which set `targetUrl` to these paths) as well as by direct URL navigation/bookmarks. Telemetry shows one user hitting both pages repeatedly across three separate days, each visit producing a 403 on data fetch.

There is also an existing automated test, `frontend/src/auth/__tests__/accessMatrixConsistency.test.ts`, that enforces **bidirectional** consistency between `guard(...)`/`RequireMenuPath` usages in `App.tsx` and the keys of `ACCESS_ROUTES`:
- every `guard(...)` call must have a matching `ACCESS_ROUTES` entry, and
- every non-virtual (non-`#`-prefixed) `ACCESS_ROUTES` entry must be guarded in `App.tsx`.

This means the fix **must** add both the `access-matrix.json` menu-path entries *and* the `guard(...)` wrappers together — adding only one side would fail this test.

## Functional Requirements

### FR-1: Add menu-path permission entries for both routes
Add two new entries to the `menuPaths` array in `access-matrix.json`, each requiring `Finance_MarginAnalysis` at `Read` level, matching the permission already required by the backing `AnalyticsController` endpoints:

```json
{ "path": "/automation/invoice-import-statistics", "requires": [{ "feature": "Finance_MarginAnalysis", "level": "Read" }] },
{ "path": "/finance/bank-statements", "requires": [{ "feature": "Finance_MarginAnalysis", "level": "Read" }] }
```

**Acceptance criteria:**
- `access-matrix.json` contains both new `menuPaths` entries, each requiring `Finance_MarginAnalysis` / `Read`.
- No new `Feature` enum value, role string, or `seedGroups` change is introduced — `Finance_MarginAnalysis` (`finance.margin_analysis.read`) already exists and is already granted to the groups that should have it (e.g. `Vedeni`, `Ucetni`).

### FR-2: Regenerate the five derived artifacts
Run `Anela.Heblo.AccessMatrixGen` (per `memory/patterns/adding-a-new-permission.md`) against the updated `access-matrix.json` to regenerate:
- `backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.generated.cs`
- `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.generated.cs`
- `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs`
- `frontend/src/auth/accessMatrix.generated.ts`
- `access-matrix-entra.generated.json`

**Acceptance criteria:**
- `AccessMatrix.generated.cs` contains new `MenuPath(...)` entries for both paths, each with `FeaturePermission(Feature.Finance_MarginAnalysis, AccessLevel.Read)`.
- `accessMatrix.generated.ts`'s `ACCESS_ROUTES` contains:
  - `"/automation/invoice-import-statistics": { permissions: ["finance.margin_analysis.read"] }`
  - `"/finance/bank-statements": { permissions: ["finance.margin_analysis.read"] }`
- `Feature.generated.cs` and `AccessRoles.generated.cs` are unchanged in content other than possible regeneration-stable output (no new feature/role is added by this change).
- All five files are regenerated together, not hand-edited (they carry an auto-generated header forbidding manual edits).

### FR-3: Gate both routes in `App.tsx` with `guard(...)`
Wrap both route elements with the existing `guard(path, element)` helper, matching the pattern used by every sibling route:

```tsx
<Route path="/automation/invoice-import-statistics" element={guard("/automation/invoice-import-statistics", <InvoiceImportStatistics />)} />
...
<Route path="/finance/bank-statements" element={guard("/finance/bank-statements", <BankStatementImportPage />)} />
```

**Acceptance criteria:**
- Both routes in `frontend/src/App.tsx` (currently at lines 415 and 445) are wrapped in `guard(...)`.
- A user without `finance.margin_analysis.read` who navigates to either path (via sidebar/tile click, typed URL, or bookmark) is redirected to `/` and never renders `InvoiceImportStatistics` or `BankStatementImportPage`, and never issues the underlying API call.
- A user *with* `finance.margin_analysis.read` sees no behavior change: the page renders and data loads exactly as before.
- No other route in `App.tsx` is modified.

### FR-4: No regressions to existing gated routes or permission groups
The change must not alter the permission requirements or generated output for any other existing menu path.

**Acceptance criteria:**
- Diff of `access-matrix.json` shows only the two new `menuPaths` entries added — no existing entries, features, or `seedGroups` modified.
- Diff of each generated artifact shows only additive changes (new `MenuPath`/`ACCESS_ROUTES` entries), no reordering or modification of existing entries beyond what regeneration naturally does.

## Non-Functional Requirements

### NFR-1: Performance
No measurable impact. `RequireMenuPath`'s permission check is an in-memory lookup against already-loaded permission state (`usePermissionsContext`); it adds no network round-trip beyond what already occurs for every other guarded route.

### NFR-2: Security
This is a security-correctness fix: it closes a gap where the frontend allowed navigation to a page whose only backing data source is authorization-gated, giving an unauthorized user a broken/empty page rather than being blocked as intended. No new attack surface is introduced. The backend `[FeatureAuthorize(Feature.Finance_MarginAnalysis)]` gate on `AnalyticsController` remains the authoritative enforcement point regardless of frontend state — this change only improves the frontend's handling to match it, consistent with the project's existing security model (frontend gating is UX, backend gating is enforcement).

### NFR-3: Consistency / test enforcement
The existing `accessMatrixConsistency.test.ts` test must pass after the change, both directions (every `guard()` has a matching `ACCESS_ROUTES` entry; every non-virtual `ACCESS_ROUTES` entry is guarded in `App.tsx`).

## Data Model
No new entities. This change only adds two rows to the existing `menuPaths` array in `access-matrix.json`, each referencing the pre-existing `Finance_MarginAnalysis` feature at `Read` level. No database schema or migration is involved — `access-matrix.json` is a build-time source of truth compiled into code, not a runtime data store. (Runtime permission *grants* to users/groups are stored via `seedGroups`/the in-app admin UI and are unaffected — the existing `Finance_MarginAnalysis` permission already has real-world grantees, e.g. the `Vedeni` and `Ucetni` seed groups.)

## API / Interface Design
No backend API changes. `AnalyticsController`'s existing endpoints and `[FeatureAuthorize(Feature.Finance_MarginAnalysis)]` attribute are unchanged — they are already correct per the brief's root-cause analysis.

Frontend interface changes:
- `access-matrix.json`: two new `menuPaths` entries (see FR-1).
- `frontend/src/auth/accessMatrix.generated.ts`: two new `ACCESS_ROUTES` keys (generated, see FR-2).
- `frontend/src/App.tsx`: two `<Route>` elements gain `guard(...)` wrapping (see FR-3).

User-facing flow after the fix:
1. User without `finance.margin_analysis.read` clicks the "Faktury importované včera" or "Bankovní výpisy" dashboard tile, or types/bookmarks either URL.
2. `RequireMenuPath` finds the `ACCESS_ROUTES` entry, evaluates `hasPermission("finance.margin_analysis.read")` as `false`, and redirects to `/` — identical to the existing behavior for every other permission-gated route (e.g. `/logistics/packing-materials`).
3. No call to `GET /api/Analytics/invoice-import-statistics` or `GET /api/Analytics/bank-statement-import-statistics` is made, so no 403 is generated or logged.

## Dependencies
- `backend/tools/Anela.Heblo.AccessMatrixGen` — the code generator that must be run to regenerate the five derived artifacts; this is a build-time dependency already used for every access-matrix change (per `memory/patterns/adding-a-new-permission.md`), not a new one.
- `frontend/src/components/auth/RequireMenuPath.tsx` and the `guard(...)` helper in `App.tsx` — both pre-existing and unmodified by this change; the fix only adds calls to them.
- `frontend/src/auth/__tests__/accessMatrixConsistency.test.ts` — pre-existing test that will validate this change.
- No external service or library dependency is introduced.

## Out of Scope
- Changing which groups/users are granted `Finance_MarginAnalysis` — this is a permission-*gating* fix, not a permission-*grant* change. If the previously-affected user genuinely needs access to these pages, granting them `finance.margin_analysis.read` (e.g. via `/admin/access`) is a separate, independent action outside this fix.
- The `invoiceimportstatistics` and `bankstatementimportstatistics` dashboard tiles both declare `RequiredPermissions => Array.Empty<string>()` (`InvoiceImportStatisticsTile.cs` and its counterpart), meaning they are visible to every user regardless of `Finance_MarginAnalysis`, including users who will now be redirected away when they click through. This matches the codebase-wide pattern — essentially every dashboard tile currently declares empty `RequiredPermissions` — and is not specific to this bug. Gating tile *visibility* by the same permission is a reasonable follow-up but is not required to fix the reported 403s (post-fix, clicking the tile redirects gracefully to `/` instead of loading a page that then fails), and is left as a separate potential enhancement.
- Any change to `AnalyticsController`, its `[FeatureAuthorize]` attribute, or any of its five actions — these are already correct.
- Any change to how `RequireMenuPath`, `guard(...)`, or `AccessMatrixGen` themselves work.
- Adding a user-facing "access denied" message/page in place of the current silent redirect-to-`/` — this fix intentionally matches the existing behavior of every other guarded route, which redirects silently. Changing that UX is a separate, broader concern affecting all guarded routes, not specific to this bug.

## Open Questions
None.

## Status: COMPLETE
