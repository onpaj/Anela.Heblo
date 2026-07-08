# Design: Fix Transport Box E2E failures (E2E permission gap + broken nav fallback)

No user-facing UI/UX surface is introduced or redesigned by this feature (per `arch-review.r1.md`, `Skip Design: true`). FR-1 and FR-2 are backend-claims and test-helper changes with zero visual surface. FR-3 touches one existing component's render branch but reuses its current error-panel look verbatim, only repositioning where it renders — no new screens, layouts, components, or visual decisions. Accordingly this document omits UX/UI Design and specifies component boundaries/contracts and data shapes only.

## Component Design

### 1. `E2ESessionService.CreateSyntheticUserClaims` (backend)

**File:** `backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ESessionService.cs`

**Responsibility:** Builds the flat `Claim[]` used to construct the synthetic `ClaimsPrincipal` signed in for E2E test sessions (Staging/Development only, via `E2ETestController`).

**Contract change:** Extend the existing inline claim list with two additional `ClaimTypes.Role` claims, following the exact pattern already used for `AccessRoles.FinanceFinancialOverviewRead`:

```csharp
new Claim(ClaimTypes.Role, AccessRoles.WarehouseLogisticsRead),
new Claim(ClaimTypes.Role, AccessRoles.WarehouseLogisticsWrite),
```

Place these adjacent to the Finance claim, with a comment naming the specs that need them (`box-creation`, `box-receive`) so a future permission-gap investigation can trace the origin quickly.

**Interface stability:**
- `Claim[] CreateSyntheticUserClaims(string environmentName)` signature is unchanged.
- No new constants are introduced; the two new claims reference existing generated constants (`AccessRoles.WarehouseLogisticsRead` = `"warehouse.logistics.read"`, `AccessRoles.WarehouseLogisticsWrite` = `"warehouse.logistics.write"` — confirmed in `AccessRoles.generated.cs`).
- Do not introduce a config-driven or list-constant role set — the method must keep returning a flat, hand-written array, matching every other claim in this method.
- Zero changes to `access-matrix.json`, `AccessRoles.generated.cs`, `AccessMatrix.generated.cs`, or `accessMatrix.generated.ts` — this service only *consumes* those generated constants, it does not define authorization requirements.

**Consumers unaffected by signature:** `E2ETestController` (caller of `CreateE2EAuthenticationSessionAsync`), `FeatureAuthorizeAttribute` (reads `ClaimsPrincipal.IsInRole(...)` against the resulting claims — no changes needed there), `PermissionsContext`/`GET /api/auth/me` (reads the same role claims at session query time — no changes needed there).

### 2. `e2e-auth-helper.ts` — `navigateToTransportBoxReceive`

**File:** `frontend/test/e2e/helpers/e2e-auth-helper.ts`

**Responsibility:** E2E test helper that navigates to the Transport Box Receive page, first via UI (Sklad menu → "Příjem boxů"), falling back to direct URL navigation if the UI path fails or times out.

**Contract change:** The fallback `page.goto(...)` target (currently line 314) changes from:
```
`${baseUrl}/warehouse/transport-box-receive`   // no matching <Route> in App.tsx — renders nothing
```
to:
```
`${baseUrl}/logistics/receive-boxes`           // matches App.tsx's registered route and Sidebar.tsx's href
```

**Interface stability:** Function signature `navigateToTransportBoxReceive(page): Promise<void>` and its "try UI nav, fall back to direct URL" control flow are unchanged — only the literal fallback URL string is corrected. `navigateToTransportBoxes`'s existing fallback (`/logistics/transport-boxes`, line 221) is already correct and is not modified. No other `navigateTo*` helper in this file targets a stale URL (spot-checked `navigateToCatalog`, `navigateToStockOperations` — both target valid registered routes).

### 3. `TransportBoxList` (frontend component)

**File:** `frontend/src/components/pages/TransportBoxList.tsx`

**Responsibility:** Renders the Transport Box list page: header, action bar (create/refresh), collapsible filter/summary controls, and the results table, backed by `useTransportBoxesQuery`.

**Render contract (structural, not visual — same JSX/classes reused, only re-scoped):**

Today the component has a single early return for the `error` case (lines 269–291) that replaces the *entire* page body with a standalone red alert box, and a separate success-path render (from line 293) that includes the header (`<h1>Transportní boxy</h1>`, lines 298–303) and the action bar (`Otevřít nový box` / `Obnovit` buttons, lines 480–497) nested deep inside the collapsible controls block.

The fix hoists the two elements that must survive any query state — the header block (298–303) and the action-bar button pair (480–497, specifically the always-visible `div` at line 352 containing both buttons) — so they render in a shell shared by all three query states (`isLoading`, `error`, success), and shrinks the `error` branch to swap in only the message region in place of the table/summary content:

```
render():
  <PageShell>                                  # always rendered
    <h1>Transportní boxy</h1>                  # unconditional
    <ActionBar>                                 # unconditional
      <button onClick={handleOpenNewBox}>Otevřít nový box</button>
      <button onClick={() => refetch()}>Obnovit</button>
    </ActionBar>

    {error
      ? <ErrorPanel message={...} onRetry={refetch} />   # "Zkusit znovu", same classes as today
      : <FiltersAndTable ... />}                          # existing isLoading/table/summary logic, untouched
  </PageShell>
```

**Explicitly out of scope for this change** (per architecture review's Decision 3 / risk mitigation): the collapsible filters block, summary cards, state-filter chips, search field, and the `isLoading` / empty-results (`data.items.length === 0`) branches are not restructured — only the `error` branch's returned JSX and the position of the header/action-bar JSX change.

**Contract guarantees consumers rely on (must not regress):**
- `<h1>` text remains exactly `"Transportní boxy"`.
- The create-box button's accessible text still matches `/Otevřít nový box/` (used by `box-creation.spec.ts`, `boxes-basic.spec.ts`, `box-management.spec.ts`, and `TransportBoxList.test.tsx`).
- The retry button remains labeled `"Zkusit znovu"` and still calls the same `refetch()` from `useTransportBoxesQuery`.
- `handleOpenNewBox`, `refetch`, and all other handlers keep their existing signatures — only their JSX position moves.

## Data Schemas

No database schema, migration, or persisted-entity changes anywhere in this fix.

### Claim set shape (`E2ESessionService.CreateSyntheticUserClaims`)

Not exposed as API response data — internal to `ClaimsPrincipal` construction — but its effective content changes:

**Before:**
```json
[
  "ClaimTypes.NameIdentifier: e2e-test-user-id",
  "... (identity claims unchanged) ...",
  "ClaimTypes.Role: heblo_user",
  "scp: access_as_user",
  "ClaimTypes.Role: finance.financial_overview.read"
]
```

**After:**
```json
[
  "ClaimTypes.NameIdentifier: e2e-test-user-id",
  "... (identity claims unchanged) ...",
  "ClaimTypes.Role: heblo_user",
  "scp: access_as_user",
  "ClaimTypes.Role: finance.financial_overview.read",
  "ClaimTypes.Role: warehouse.logistics.read",
  "ClaimTypes.Role: warehouse.logistics.write"
]
```

### Downstream effect on `GET /api/auth/me` → `PermissionsContext`

No shape change to the endpoint's response contract — `permissions: string[]` already supports arbitrary role strings. Effective content changes:

**Before:** `permissions` excludes any `warehouse.*` entry → `RequireMenuPath`/`Sidebar.hasPermission` checks against `Feature.Warehouse_Logistics` fail.

**After:** `permissions` includes `"warehouse.logistics.read"` and `"warehouse.logistics.write"` → the same checks pass, matching what a real Warehouse_Logistics user's session already contains today.

### `TransportBoxController` request/response shapes

Unchanged. No endpoint, route, request DTO, or response DTO is added, removed, or modified — the fix only changes whether the existing `[FeatureAuthorize(Feature.Warehouse_Logistics, Read|Write)]` check passes for the E2E identity, not the shape of `GET /api/transport-boxes`, `POST /api/transport-boxes`, `POST /api/transport-boxes/open-by-code`, `PUT .../state`, `POST .../items`, or `PUT .../description`.

### Test data shape (new backend test)

`backend/test/Anela.Heblo.Tests/Authorization/E2ESessionServiceTests.cs` (new file) asserts against the claim array shape above:

```csharp
var claims = sut.CreateSyntheticUserClaims("Staging");

claims.Should().Contain(c =>
    c.Type == ClaimTypes.Role && c.Value == AccessRoles.WarehouseLogisticsRead);
claims.Should().Contain(c =>
    c.Type == ClaimTypes.Role && c.Value == AccessRoles.WarehouseLogisticsWrite);
// existing roles (Base, FinanceFinancialOverviewRead) must still be present — regression guard
```
