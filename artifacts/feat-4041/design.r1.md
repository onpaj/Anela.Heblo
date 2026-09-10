# Design: Permission-gate invoice-import-statistics and bank-statements routes

## Component Design

No new components. Two existing components in the frontend authorization pipeline gain data
they are currently missing; no component's behavior, interface, or implementation changes.

### `access-matrix.json` (repo root) — source of truth
Responsibility: declare, per menu path, which `Feature` + `AccessLevel` a user must hold to
reach that route. Currently has no `menuPaths` entry for `/automation/invoice-import-statistics`
or `/finance/bank-statements`. This fix adds exactly two entries to the existing `menuPaths`
array, referencing the pre-existing `Finance_MarginAnalysis` feature — no new feature, role, or
`seedGroups` entry. Placement: alongside the other `Finance_MarginAnalysis`/bank-statement
entries (e.g. near `/analytics/product-margin-summary` and
`/customer/bank-statements-overview`) for readability; array order has no functional effect
since both `AccessMatrixGen`'s `MenuPath` list and the generated `ACCESS_ROUTES` object are
keyed by path string, not position.

### `Anela.Heblo.AccessMatrixGen` — generator (unchanged)
Responsibility: compile `access-matrix.json` into five derived, hand-off-limits artifacts.
Runs automatically via the `GenerateAccessMatrix` MSBuild target
(`BeforeTargets="Build"`, Debug configuration) on `Anela.Heblo.API`, or manually via
`dotnet run --project backend/tools/Anela.Heblo.AccessMatrixGen`. Its iteration is order-
preserving and purely additive per manifest entry, so adding two `menuPaths` rows produces
exactly two new lines in each of the two artifacts that carry per-path data
(`AccessMatrix.generated.cs`, `accessMatrix.generated.ts`); the three other generated files
(`Feature.generated.cs`, `AccessRoles.generated.cs`, `access-matrix-entra.generated.json`) are
unaffected in content because no `Feature` or role is introduced. Not modified by this change.

### `guard(path, element)` / `RequireMenuPath` — frontend enforcement (unchanged)
Responsibility: `guard()` (defined once in `App.tsx`) wraps a route element in
`RequireMenuPath`, which looks up `ACCESS_ROUTES[path]` and redirects to `/` if the entry is
absent or the current user's permission set (from `usePermissionsContext`, already loaded
in-memory) doesn't satisfy the required permission(s). Neither `guard()` nor
`RequireMenuPath.tsx` changes — this fix only adds two call sites.

### `App.tsx` — route table
Responsibility: mount every application route. The two routes for
`InvoiceImportStatistics` and `BankStatementImportPage` (currently bare
`<Route path="..." element={<Component />} />` at the two lines noted in the spec) are
rewrapped as `<Route path="..." element={guard("...", <Component />)} />`, matching every
sibling route (e.g. `/logistics/packing-materials`, `/customer/issued-invoices`). No other
route, import, or the `guard()` definition itself is touched.

### `AnalyticsController` — backend (unchanged)
Already carries a class-level `[FeatureAuthorize(Feature.Finance_MarginAnalysis)]` covering
both underlying endpoints (`GET /api/Analytics/invoice-import-statistics`,
`GET /api/Analytics/bank-statement-import-statistics`). Remains the authoritative,
unmodified enforcement point; this fix only brings frontend gating in line with it.

### Files touched (component boundary)

| File | Change |
|---|---|
| `access-matrix.json` | Hand-edited: 2 new `menuPaths` entries |
| `frontend/src/App.tsx` | Hand-edited: 2 routes wrapped in `guard(...)` |
| `backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.generated.cs` | Regenerated only (content unchanged) |
| `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.generated.cs` | Regenerated: 2 new `MenuPath(...)` lines |
| `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs` | Regenerated only (content unchanged) |
| `frontend/src/auth/accessMatrix.generated.ts` | Regenerated: 2 new `ACCESS_ROUTES` keys |
| `access-matrix-entra.generated.json` | Regenerated (unused post-Entra-cutover; content unaffected — no new feature) |

Never hand-edit the five generated files directly — always edit `access-matrix.json` and
regenerate via a Debug build of `Anela.Heblo.API` or a direct `AccessMatrixGen` run.

## Data Schemas

### `access-matrix.json` — new `menuPaths` entries

Schema (existing, unchanged shape — each `menuPaths[]` element):

```
{
  "path": string,          // route path, matches the <Route path> in App.tsx
  "requires": [
    {
      "feature": string,   // a "key" from the top-level "features" array
      "level": "Read" | "Write" | "Admin"
    }
    // ...one or more required permissions (AND-ed)
  ]
}
```

New entries added by this fix:

```json
{ "path": "/automation/invoice-import-statistics", "requires": [{ "feature": "Finance_MarginAnalysis", "level": "Read" }] },
{ "path": "/finance/bank-statements", "requires": [{ "feature": "Finance_MarginAnalysis", "level": "Read" }] }
```

### `AccessMatrix.generated.cs` — resulting shape (regenerated, C#)

```csharp
new MenuPath("/automation/invoice-import-statistics", new FeaturePermission[] { new FeaturePermission(Feature.Finance_MarginAnalysis, AccessLevel.Read) }),
new MenuPath("/finance/bank-statements", new FeaturePermission[] { new FeaturePermission(Feature.Finance_MarginAnalysis, AccessLevel.Read) }),
```

### `accessMatrix.generated.ts` — resulting shape (regenerated, TypeScript)

`ACCESS_ROUTES` is a `Record<string, { permissions: string[] }>` keyed by route path, where
each permission string is `"<feature>.<level>"` in snake_case
(`finance.margin_analysis.read`, matching the existing convention):

```ts
"/automation/invoice-import-statistics": { permissions: ["finance.margin_analysis.read"] },
"/finance/bank-statements": { permissions: ["finance.margin_analysis.read"] },
```

### `App.tsx` — route element shape (hand-edited, no new type)

```tsx
<Route path="/automation/invoice-import-statistics" element={guard("/automation/invoice-import-statistics", <InvoiceImportStatistics />)} />
...
<Route path="/finance/bank-statements" element={guard("/finance/bank-statements", <BankStatementImportPage />)} />
```

### API / runtime payloads

No new or changed request/response shapes. `AnalyticsController`'s existing endpoints, DTOs,
and `[FeatureAuthorize(Feature.Finance_MarginAnalysis)]` attribute are unmodified. The only
runtime effect is that `RequireMenuPath` now resolves an `ACCESS_ROUTES` entry for both paths
and evaluates it before either component mounts, so an unauthorized user's browser never issues
`GET /api/Analytics/invoice-import-statistics` or
`GET /api/Analytics/bank-statement-import-statistics` in the first place.
