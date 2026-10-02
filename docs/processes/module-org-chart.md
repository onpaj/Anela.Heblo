---
process: module-org-chart
kind: module
module: org-chart
summary: Read-only company org chart (Organigram) — Heblo fetches an external JSON file of positions and employees on every request and draws it as a tree; nothing is stored.
owns: []
verified_at: "5e993f9e2"
related: []
---

# Org chart (Organigram)

## Purpose
Shows Anela's organisational structure — which positions exist, who reports to whom, which
department each position belongs to and who holds it — as an interactive tree (Organigram).
It answers "who does what and who is whose manager" for any employee.

Heblo is **not** the source of truth for the structure. The structure is maintained as a single
JSON file outside Heblo (location given by config key `OrgChart:DataSourceUrl`); Heblo only
reads that file and renders it. Changing the org chart means editing that file, not anything
in Heblo.

## Users & screens
- **Sidebar → Anela → Struktura** (menu key `#org-chart`) opens the **external** site
  `https://orgchart.anela.cz` in a new tab (`Sidebar.tsx` → `openOrgChart`). It does **not**
  open Heblo's own page. Shown only to users with permission `anela.org_chart.read`.
- **`/orgchart`** (`OrgChartPage.tsx`) — Heblo's own rendering of the same data: a tree of
  position cards (title, department, description, holders; primary holder highlighted),
  filters by department and by depth level, zoom, and counters (positions, employees).
  Position titles link to `position.url` (e.g. a SharePoint job description) and employee
  names to `employee.url` (e.g. an Entra profile) when the JSON provides them. The route has
  no menu entry and no frontend permission guard; it is reachable only by typing the URL, and
  the API call behind it still requires `anela.org_chart.read`.
- No dashboard tile, no MCP tool.

## Processes
None. The module has no scheduled job, no background refresh and no writes anywhere — every
page load is a single live read, documented here:

- `GET /api/OrgChart` (`OrgChartController.GetOrganizationStructure`, feature
  `Anela_OrgChart`, access level Read = permission `anela.org_chart.read`) →
  MediatR `GetOrganizationStructureRequest` → `GetOrganizationStructureHandler` →
  `IOrgChartService.GetOrganizationStructureAsync` (implemented by `OrgChartService` in the
  `Anela.Heblo.Adapters.OrgChart` adapter).
- `OrgChartService` does a plain anonymous HTTP `GET` of `OrgChart:DataSourceUrl` (typed
  `HttpClient`, no auth header, default .NET 100 s timeout, no retry policy), deserialises it
  case-insensitively and maps it 1:1 to `OrgChartResponse` → `OrganizationDto` →
  `PositionDto[]` → `EmployeeDto[]`. Missing strings become `""`, missing lists become empty.
- The frontend hook `useOrgChart` caches the response for the browser session
  (`staleTime` 30 min, `gcTime` 1 h). There is **no server-side cache**: each cache miss in any
  browser re-downloads the JSON.

Expected JSON shape (from `Models/OrgChartJsonModel.cs`):

| Path | Field | Notes |
|---|---|---|
| `organization` | `name`, `positions[]` | |
| `positions[]` | `id`, `title`, `description`, `level`, `parentPositionId`, `department`, `url`, `employees[]` | `parentPositionId` empty or unknown → shown as a root of the tree |
| `employees[]` | `id`, `name`, `email`, `startDate`, `isPrimary`, `url` | `startDate` is passed through as a string, not parsed |

Plain CRUD: none — the module is read-only.

## Data owned
None. No tables, no schema, no cache keys, no blob containers. The org chart data lives only in
the external JSON file and in each browser's React Query cache.

## External systems
- **Org-structure JSON file** at `OrgChart:DataSourceUrl` — read only, HTTP GET, on demand.
  The repo default in `backend/src/Anela.Heblo.API/appsettings.json` is the placeholder
  `https://example.com/organization-structure.json`; the real URL is supplied per environment
  (by the Key Vault convention it would be secret `OrgChart--DataSourceUrl`, but the actual
  value and host cannot be determined from the repo). Because the request carries no
  credentials, the URL must be publicly readable or carry its own token (e.g. a SAS query
  string).
- **`https://orgchart.anela.cz`** — separate external site the sidebar links to; Heblo does
  not call it and its relation to the JSON file is not visible in the repo.

## Dependencies
- Reads from no other Heblo module. Uses only the shared authorization (`FeatureAuthorize`,
  generated access matrix) and `BaseResponse` / `ErrorCodes`.
- No other module reads from it.

## Known quirks
- **Two org charts.** The menu item "Struktura" opens the external `orgchart.anela.cz`, not
  Heblo's `/orgchart` page, so Heblo's own page is effectively hidden (reachable only by URL).
  Users and Claude may therefore be looking at different renderings; whether both read the
  same file is not visible in the repo.
- **`level` from the JSON is ignored by the UI.** `calculateLevels()` in
  `frontend/src/pages/orgChartUtils.ts` recomputes every position's level from the
  `parentPositionId` chain (root = 1) and overwrites the API value; the "level" filter uses
  the recomputed value. A cycle in `parentPositionId` is logged to the browser console and the
  position is treated as level 1.
- **Department filter keeps ancestors.** Filtering by a department also shows every parent
  position up the chain (from other departments) so the tree stays connected.
- **Startup fails without the URL.** `OrgChartOptions.DataSourceUrl` is
  `[Required(AllowEmptyStrings = false)]` with `ValidateOnStart()`; an empty value stops the
  whole app from booting. A wrong-but-non-empty URL (such as the repo placeholder) boots fine
  and fails only when someone opens the page.
- **Any fetch error is an opaque 500.** HTTP failures and malformed JSON are wrapped in
  `InvalidOperationException`; the controller logs it ("Error fetching organizational
  structure") and returns HTTP 500 with `ErrorCodes.InternalServerError` — the response never
  says which URL or why (deliberately, to avoid leaking the URL). The page then shows
  "Nepodařilo se načíst data". Check the API logs for the real cause. A JSON body of literal
  `null` gives the same 500. Exception: a source that hangs past the 100 s `HttpClient`
  timeout raises `TaskCanceledException`, which the controller rethrows as a cancellation
  instead of mapping it to this 500.
- **Edits appear with delay.** After the JSON file is changed, a browser that already loaded
  the chart keeps showing the old version for up to 30 minutes (React Query `staleTime`)
  unless the page is hard-reloaded.

## Code entry points
- `backend/src/Anela.Heblo.API/Controllers/OrgChartController.cs` — the only endpoint, permission gate, 500 mapping
- `backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs` — options binding + startup validation
- `backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartOptions.cs` — `OrgChart:DataSourceUrl`
- `backend/src/Anela.Heblo.Application/Features/OrgChart/UseCases/GetOrganizationStructure/GetOrganizationStructureHandler.cs` — MediatR handler
- `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartService.cs` — HTTP fetch, deserialisation, mapping
- `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/Models/OrgChartJsonModel.cs` — expected JSON shape
- `frontend/src/pages/OrgChartPage.tsx`, `frontend/src/pages/orgChartUtils.ts`, `frontend/src/components/OrgChart/PositionCard.tsx` — rendering, level recomputation, filters
- `frontend/src/components/Layout/Sidebar.tsx` — "Struktura" link to `orgchart.anela.cz`
