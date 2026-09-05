# Architecture Review: Permission-gate invoice-import-statistics and bank-statements routes

## Skip Design: true

No new or changed UI components, screens, layouts, or visual design. The two pages
(`InvoiceImportStatistics`, `BankStatementImportPage`) are unchanged; the only user-visible
behavior change is that an unauthorized user is redirected to `/` instead of seeing a broken
page — identical to the existing redirect behavior of every other guarded route.

## Architectural Fit Assessment

This fits the codebase's existing frontend-authorization architecture exactly as designed —
it does not introduce a new pattern, it closes a gap in applying an existing one.

Verified in the codebase:

- `access-matrix.json` (repo root) is the declared single source of truth for menu-path →
  feature-permission mappings, compiled by `backend/tools/Anela.Heblo.AccessMatrixGen` into
  five generated artifacts (`Feature.generated.cs`, `AccessMatrix.generated.cs`,
  `AccessRoles.generated.cs`, `frontend/src/auth/accessMatrix.generated.ts`,
  `access-matrix-entra.generated.json`). This is documented in
  `memory/patterns/adding-a-new-permission.md` and matches `Program.cs`'s argument order
  exactly.
- `access-matrix.json`'s `menuPaths` array today has **no entry** for
  `/automation/invoice-import-statistics` or `/finance/bank-statements` — confirmed by direct
  grep; also absent from the generated `AccessMatrix.generated.cs` and
  `accessMatrix.generated.ts`.
- `frontend/src/App.tsx` lines 415 and 445 mount these two routes as bare
  `<Route path="..." element={<Component />} />`, with no `guard(...)` wrapper, while every
  structurally similar sibling route (`/logistics/packing-materials`,
  `/customer/issued-invoices`, `/purchase/orders`, etc.) uses
  `guard(path, <Component />)`.
- `guard()` (App.tsx line 292) is a one-line wrapper around `RequireMenuPath`
  (`frontend/src/components/auth/RequireMenuPath.tsx`), which looks up `ACCESS_ROUTES[path]`
  and redirects to `/` if the entry is missing *or* the user lacks a required permission.
  Because these two routes are never wrapped in `guard(...)` at all, `RequireMenuPath` never
  runs for them regardless of what `ACCESS_ROUTES` contains — this is why both halves of the
  fix are required.
- The backend contract is already correct and out of scope:
  `AnalyticsController` (`backend/src/Anela.Heblo.API/Controllers/AnalyticsController.cs:14`)
  carries a class-level `[FeatureAuthorize(Feature.Finance_MarginAnalysis)]`, and
  `Finance_MarginAnalysis` (`finance.margin_analysis.read`) already exists as a feature and is
  already granted to `Vedeni`, `Ucetni`, and `Spravce` in `access-matrix.json`'s `seedGroups`.
  No new feature, role, or grant is needed.
- A bidirectional consistency test,
  `frontend/src/auth/__tests__/accessMatrixConsistency.test.ts`, regex-scans `App.tsx` for
  `guard(...)`/`RequireMenuPath` usages and cross-checks them against `ACCESS_ROUTES` keys in
  both directions. This test is a structural constraint on the fix, not just documentation:
  adding only the `access-matrix.json` entries (no `guard()`) fails the "every ACCESS_ROUTES
  key is guarded" check; adding only `guard()` (no matrix entries) fails the "every guard() has
  an ACCESS_ROUTES entry" check and — separately — would leave `RequireMenuPath` redirecting
  everyone (including authorized users) because the lookup returns `undefined` regardless of
  permission held.
- `AccessMatrixGen`'s code generation is order-preserving and purely additive per manifest
  entry (verified in `Program.cs`): it iterates `manifest.Features` and `manifest.MenuPaths` in
  file order and emits one line per entry with no cross-entry logic. Adding two new
  `menuPaths` rows referencing the *existing* `Finance_MarginAnalysis` feature therefore only
  adds two new `MenuPath(...)`/`ACCESS_ROUTES` lines; `Feature.generated.cs` and
  `AccessRoles.generated.cs` are untouched in content (no new `Feature` enum value is
  introduced).

Conclusion: the spec's two-part root cause and its proposed fix are accurate, exactly grounded
in what the code and tooling actually do, and require no architectural rethinking — this is a
same-pattern, data-plus-wiring fix.

## Proposed Architecture

No new architecture. Existing components involved:

### Component Overview

```
access-matrix.json  (source of truth: menuPaths[] + features[] + seedGroups[])
        │
        │  AccessMatrixGen (backend/tools/Anela.Heblo.AccessMatrixGen)
        │  — invoked automatically by Anela.Heblo.API.csproj's
        │    GenerateAccessMatrix target BeforeTargets="Build",
        │    Debug configuration only
        ▼
  ┌─────────────────────────────┬──────────────────────────────────┐
  │ Backend generated            │ Frontend generated                │
  │ Feature.generated.cs         │ accessMatrix.generated.ts         │
  │ AccessMatrix.generated.cs    │   → ACCESS_ROUTES["<path>"] =     │
  │ AccessRoles.generated.cs     │     { permissions: [...] }        │
  │ (+ access-matrix-entra.json, │                                    │
  │   unused post Entra cutover) │                                    │
  └─────────────────────────────┴──────────────────────────────────┘
        │                                    │
        ▼                                    ▼
  [FeatureAuthorize(Feature.Finance_MarginAnalysis)]   App.tsx route
  on AnalyticsController (unchanged, already correct)  wrapped in guard(path, el)
                                                          │
                                                          ▼
                                                  RequireMenuPath
                                                  (looks up ACCESS_ROUTES[path],
                                                   redirects to "/" if missing
                                                   entry or missing permission)
```

Both enforcement points (backend `[FeatureAuthorize]`, frontend `RequireMenuPath`) already
exist and are independently correct in isolation; the bug is that the frontend map + wiring for
these two specific paths was never created when the routes/tiles were added.

### Key Design Decisions

#### Decision 1: Fix at the data + wiring layer, not in `RequireMenuPath`/`guard`/`AccessMatrixGen`
**Options considered:**
- (a) Add the missing `menuPaths` entries and `guard()` calls (spec's proposal).
- (b) Change `RequireMenuPath` to fail closed (redirect) by default for any path with no
  `ACCESS_ROUTES` entry, regardless of whether it's wrapped in `guard()` — i.e. move the
  "missing route" case into `App.tsx`'s routing itself so bare `<Route>` declarations can't
  silently skip permission checks in the future.
- (c) Add a build-time/test-time lint that flags any `<Route element={<X/>}>` in the guarded
  Layout's route group that isn't wrapped in `guard(...)`, independent of `access-matrix.json`
  content.

**Chosen approach:** (a), exactly as the spec proposes.

**Rationale:** (b) is not mechanically possible without restructuring routing — `guard()` is
what *inserts* `RequireMenuPath` into the tree; a route that never calls `guard()` never
executes any check, so there is no hook point to "fail closed" from inside `RequireMenuPath`
itself. (c) is a legitimate defense-in-depth idea (see Specification Amendments below) but is a
broader, separate change (a static-analysis/test addition affecting the whole route table, not
just these two paths) and risks false positives on routes intentionally left ungated (e.g.
`/orgchart`, `/logistics/warehouse-statistics`, `/customer/cooling` — confirmed present in
`App.tsx` as bare routes today, apparently by design). Scope this fix to the two broken routes
per the brief; track (c) as a follow-up if the team wants systemic prevention.

#### Decision 2: Where to insert the two new `menuPaths` entries in `access-matrix.json`
**Options considered:**
- Insert alphabetically/contextually near related entries (e.g. next to
  `/analytics/product-margin-summary` and `/customer/bank-statements-overview`, which share the
  same feature or module).
- Append to the end of the `menuPaths` array.

**Chosen approach:** Insert near related entries — specifically, place
`/automation/invoice-import-statistics` near other `/automation/*` paths (if any share a
feature) or immediately after `/analytics/product-margin-summary` (same feature,
`Finance_MarginAnalysis`); place `/finance/bank-statements` immediately after
`/customer/bank-statements-overview` or `/finance/overview` for discoverability.

**Rationale:** `AccessMatrixGen` iterates the array in file order with no sorting, so generated
output order mirrors source order. This is purely cosmetic/maintainability — it does not affect
correctness, since `ACCESS_ROUTES` is a keyed object and `MenuPaths` is a flat list checked by
path string, not position. Optimize for humans reading `access-matrix.json` next to the two
existing `Finance_MarginAnalysis`/bank-statement entries. This is not worth deliberating over;
either position passes FR-4's "additive diff only" acceptance criterion.

## Implementation Guidance

### Directory / Module Structure

No new files or directories. Exactly three files change by hand, five are regenerated:

**Hand-edited:**
- `access-matrix.json` (repo root) — add two `menuPaths` entries (FR-1).
- `frontend/src/App.tsx` — wrap the two existing `<Route>` elements (lines 415, 445) in
  `guard(...)` (FR-3).

**Regenerated (never hand-edit — each carries an auto-generated header):**
- `backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.generated.cs`
- `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.generated.cs`
- `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs`
- `frontend/src/auth/accessMatrix.generated.ts`
- `access-matrix-entra.generated.json`

**Regeneration mechanism (verified in `Anela.Heblo.API.csproj`):** the `GenerateAccessMatrix`
MSBuild target (`BeforeTargets="Build"`, `Condition="'$(Configuration)' == 'Debug'"`) already
invokes `AccessMatrixGen` with the correct 6-argument order matching `Program.cs`'s expected
`(manifest, featureEnum, matrixData, roles, ts, entra)` signature. **A plain Debug build of
`Anela.Heblo.API` (`dotnet build backend/src/Anela.Heblo.API`) regenerates all five artifacts
automatically** — there is no separate manual step required beyond editing
`access-matrix.json` and building. Running `dotnet run --project
backend/tools/Anela.Heblo.AccessMatrixGen -- access-matrix.json <5 output paths...>` directly
(per `memory/patterns/adding-a-new-permission.md`) is an equivalent, explicit alternative.

**Known environment gotcha** (`memory/gotchas/dotnet-build-hangs-nodereuse-accessmatrixgen.md`):
in this sandbox, a `dotnet build`/`dotnet test` that triggers `GenerateAccessMatrix` can hang on
stale MSBuild/VBCSCompiler node-reuse servers. If a build hangs after "Generating access matrix
artifacts...", run:
```
dotnet build-server shutdown
MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DISABLE_BUILD_SERVERS=1 \
  dotnet build backend/src/Anela.Heblo.API -nodeReuse:false -p:UseSharedCompilation=false
```
This is an environment quirk, not something this feature needs to fix or work around in code.

### Interfaces and Contracts

No new interfaces. Existing contracts that must be matched exactly (all verified against
current generated output):

`access-matrix.json` new entries (append near related entries per Decision 2):
```json
{ "path": "/automation/invoice-import-statistics", "requires": [{ "feature": "Finance_MarginAnalysis", "level": "Read" }] },
{ "path": "/finance/bank-statements", "requires": [{ "feature": "Finance_MarginAnalysis", "level": "Read" }] }
```

Resulting `AccessMatrix.generated.cs` shape (matches existing pattern, e.g. line 51):
```csharp
new MenuPath("/automation/invoice-import-statistics", new FeaturePermission[] { new FeaturePermission(Feature.Finance_MarginAnalysis, AccessLevel.Read) }),
new MenuPath("/finance/bank-statements", new FeaturePermission[] { new FeaturePermission(Feature.Finance_MarginAnalysis, AccessLevel.Read) }),
```

Resulting `accessMatrix.generated.ts` shape (matches existing pattern, e.g. line 10):
```ts
"/automation/invoice-import-statistics": { permissions: ["finance.margin_analysis.read"] },
"/finance/bank-statements": { permissions: ["finance.margin_analysis.read"] },
```

`App.tsx` route wrapping (matches every sibling route's pattern exactly):
```tsx
<Route path="/finance/bank-statements" element={guard("/finance/bank-statements", <BankStatementImportPage />)} />
...
<Route path="/automation/invoice-import-statistics" element={guard("/automation/invoice-import-statistics", <InvoiceImportStatistics />)} />
```

**Do not** touch `RequireMenuPath.tsx`, the `guard()` definition (App.tsx line 292), or
`AccessMatrixGen/Program.cs` — all three are correct and unmodified per the spec's stated scope,
and this review found no reason to deviate.

### Data Flow

1. Developer edits `access-matrix.json`, adding the two `menuPaths` entries.
2. A Debug build of `Anela.Heblo.API` (or a direct `AccessMatrixGen` run) regenerates the five
   derived artifacts, in particular `ACCESS_ROUTES` in `accessMatrix.generated.ts`.
3. Developer edits `App.tsx` to wrap both routes in `guard(...)`.
4. At runtime: user navigates to `/finance/bank-statements` or
   `/automation/invoice-import-statistics` → `RequireMenuPath` looks up the path in
   `ACCESS_ROUTES` (now present) → evaluates `hasPermission("finance.margin_analysis.read")`
   via `usePermissionsContext` (already-loaded in-memory permission state, no extra network
   call) → renders the page if true, redirects to `/` if false.
5. `accessMatrixConsistency.test.ts` runs in the existing FE test suite and validates both
   directions of the guard ⟷ `ACCESS_ROUTES` mapping automatically — no new test needs to be
   written for this fix; the existing test is the acceptance gate for FR-3/NFR-3.

No backend data flow changes: `AnalyticsController`'s `[FeatureAuthorize]` remains the
authoritative server-side check regardless of frontend state, consistent with the project's
existing "frontend gates for UX, backend gates for enforcement" model.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Hand-editing a `*.generated.*` file instead of regenerating breaks the "no manual edits" invariant and can silently drift from `access-matrix.json` on the next real regeneration | Medium | Always edit `access-matrix.json` only, then rebuild `Anela.Heblo.API` (Debug) or run `AccessMatrixGen` directly; diff all five generated files before committing to confirm only additive changes (FR-4) |
| `dotnet build`/`dotnet test` hangs on stale MSBuild/VBCSCompiler node-reuse servers when `GenerateAccessMatrix` runs | Low (known, documented) | Use the `dotnet build-server shutdown` + `MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DISABLE_BUILD_SERVERS=1 -nodeReuse:false -p:UseSharedCompilation=false` sequence from `memory/gotchas/dotnet-build-hangs-nodereuse-accessmatrixgen.md` if it occurs |
| Placing the two new `menuPaths` entries in a way that reorders unrelated existing entries in the generated files, tripping FR-4's "additive diff only" check | Low | Append/insert only — never reorder existing array entries in `access-matrix.json`; review the generated diffs before committing |
| Forgetting one half of the two-part fix (matrix entry without `guard()`, or vice versa) reintroduces exactly the bug or breaks `accessMatrixConsistency.test.ts` | Medium (this is the actual root cause of the original bug) | The consistency test is the safety net — CI/local test run will fail loudly on either half being missing; treat a green `accessMatrixConsistency.test.ts` run as a hard precondition for calling FR-1–FR-3 done |
| Dashboard tiles (`invoiceimportstatistics`, `bankstatementimportstatistics`) still declare `RequiredPermissions => Array.Empty<string>()`, so unauthorized users will still see the tiles and click through — now redirected gracefully instead of 403'ing, but the tile itself is a minor UX dead-end | Low (explicitly out of scope per spec) | Confirmed as a pre-existing, codebase-wide pattern (essentially every dashboard tile has empty `RequiredPermissions`); not this fix's job to change — noted as a candidate follow-up only |

## Specification Amendments

None required — the spec (`spec.r1.md`) is architecturally sound and precisely grounded in the
actual code and tooling behavior verified during this review. Two non-blocking observations for
the implementer, not changes to scope:

1. **Insertion position in `access-matrix.json`.** The spec's FR-1 code sample doesn't say
   where in the `menuPaths` array to place the two new entries. Per Decision 2 above, place them
   near their related/same-feature entries (`/analytics/product-margin-summary` and
   `/customer/bank-statements-overview` respectively) for readability. This has no functional
   effect — `AccessMatrixGen` treats `menuPaths` as an unordered-by-meaning list — so any
   placement satisfies FR-4 as long as no existing entry is reordered.

2. **Follow-up worth logging (not part of this fix):** the two dashboard tiles that link to
   these routes have `RequiredPermissions => Array.Empty<string>()`, so they remain visible to
   users who will now be redirected on click. The spec already correctly places this out of
   scope (matches a codebase-wide pattern, not a regression introduced or fixed here). Recommend
   filing it separately if the team wants tile-visibility parity with route-gating across the
   whole dashboard — that is a systemic change touching most tiles, not a two-route fix.

## Prerequisites

None beyond what already exists in the repository:
- `Finance_MarginAnalysis` feature and its grants to `Vedeni`/`Ucetni`/`Spravce` already exist
  in `access-matrix.json` — no new feature, role, or `seedGroups` change needed.
- `AnalyticsController`'s `[FeatureAuthorize(Feature.Finance_MarginAnalysis)]` is already in
  place and correct — no backend code change.
- No database migration, no new environment configuration, no Key Vault secret, no new
  dependency. This is a two-source-file change (`access-matrix.json`, `App.tsx`) plus mechanical
  regeneration of five derived files via existing, already-wired tooling.
