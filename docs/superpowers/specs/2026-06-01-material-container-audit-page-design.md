# Material Container Audit Page ("Šarže") — Design

**Date:** 2026-06-01
**Status:** Approved design — ready for implementation planning
**Related:** `docs/superpowers/plans/2026-05-28-material-container-tracking.md` (backend entity, API, and Terminal scan workflow that *creates* these containers)

## Purpose & scope

A **read-only** page to browse and look up the physical material containers scanned in
the Terminal "Identifikace šarže" workflow. It answers the day-to-day question:
*"which container (`Mxxxxxxxx`) holds which material + supplier lot, and who scanned it when?"*

The page is purely an audit/lookup lens. The Terminal workflow owns container creation;
this page never mutates data.

**Usage shape:** a filterable, paginated table (browse by material/lot) that also supports
looking up a single container by its code — code lookup is one of the table filters, not a
separate screen.

## Placement & routing

- **Navigation:** new item **"Šarže"** in the **Výroba** section of
  `frontend/src/components/Layout/Sidebar.tsx`.
- **Route:** registered in `frontend/src/App.tsx`, lazy-loaded like peer pages. Route path
  follows the existing route-naming convention used by sibling pages (confirm at
  implementation time; e.g. `/material-containers`).
- **Component:** new page under `frontend/src/components/pages/` (e.g.
  `MaterialContainerList.tsx`), mirroring `InventoryList.tsx` and `PurchaseOrderList.tsx`.

> Naming note: the **Sklad** section already has "Sledování materiálů"
> (`PackingMaterialsPage`, for shipping/packing materials). This new page is unrelated —
> hence the distinct name "Šarže" under **Výroba**, not **Sklad**.

## Data & API

### Backend change (small)

Add an optional `code` filter to the existing list endpoint so container-code lookup uses
the same uniform, paginated, sortable code path as the other filters:

- `ListMaterialContainersRequest`: add `string? Code`.
- `MaterialContainersController` GET: add `[FromQuery] string? code` param, pass through.
- `IMaterialContainerRepository.GetPaginatedAsync` / `MaterialContainerRepository`: when
  `code` is provided, filter `x.Code == code` (exact match); ignore when null/whitespace.
- Regenerate the TypeScript client (auto-generated on `dotnet build`).

No DTO changes. No new endpoint.

### Frontend read hook

Add a list query hook to the existing `frontend/src/api/hooks/useMaterialContainers.ts`:

- `useMaterialContainersQuery({ materialCode, lotCode, code, page, pageSize })` wrapping
  `apiClient.materialContainers_GetMaterialContainers(...)`, following the `useInventoryQuery`
  pattern (returns `{ data, isLoading, error, refetch }`).
- Existing mutation/lookup hooks in this file (`useCreateMaterialContainers`,
  `useMaterialContainerByCode`, `useLastUsedLotForMaterial`) remain unchanged.

### DTO fields available

`MaterialContainerDto`: `id, code, materialCode, lotCode, amount, unit, createdAt,
createdBy, purchaseOrderLineId`. There is **no `status`** field exposed — Assigned/Discarded
is intentionally not surfaced (see Out of scope).

## UI

Mirror the established list-page convention (`InventoryList` / `PurchaseOrderList`):
separate input state vs. applied-filter state, React Query data fetching, server-side
pagination.

- **Filters** (local input state → applied on Enter or a "Filtrovat" button; applying resets
  to page 1):
  - Materiál → `materialCode`
  - Šarže → `lotCode`
  - Kód kontejneru → `code`
  - A "Vymazat" (clear) action resets all filters.
- **Table columns:**
  | Header | Source |
  |---|---|
  | Kód kontejneru | `code` |
  | Materiál | `materialCode` |
  | Šarže | `lotCode` |
  | Množství | `amount` + `unit` (blank when both null) |
  | Vytvořeno | `createdAt` |
  | Kdo | `createdBy` |
- **Ordering:** fixed **newest-first** (server already orders by Id descending). No sortable
  headers in v1 — sortable columns would require a backend sort param, which is out of scope.
- **Pagination:** server-side, with a page-size selector (20 / 50 / 100), same as
  `InventoryList`.
- **States:** loading spinner, empty state ("Žádné kontejnery"), and error message — per
  existing page convention.
- **No row detail modal** — every available field is shown in the row.
- **Telemetry:** `useScreenView('Manufacturing', 'MaterialContainers')`, matching peers.

`purchaseOrderLineId` is **not** shown (no human-meaningful value in an audit list). Can be
added later if a need appears.

## Testing

- **Backend:** extend `ListMaterialContainersHandler` / repository tests for the new `code`
  filter — filters by exact code when provided, ignored when null/whitespace; existing
  material/lot filter behavior unchanged.
- **Frontend:** component tests for the list page (Jest + React Testing Library, mirroring
  existing page tests): renders rows from a mocked query, applies each filter (material, lot,
  code) and resets to page 1, pagination controls, and loading / empty / error states.

## Out of scope (YAGNI)

- Discard / retire a container (Status mutation)
- Manual create / print container codes (Terminal owns creation)
- Status (Assigned/Discarded) display or filter
- Sortable columns
- CSV / export
- Row detail modal
