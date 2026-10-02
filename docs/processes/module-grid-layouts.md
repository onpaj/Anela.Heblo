---
process: module-grid-layouts
kind: module
module: grid-layouts
summary: Remembers, per user, how they arranged the columns of Heblo's large analysis tables (order, width, hidden columns) so the layout survives reloads and devices.
owns: []
verified_at: "f68c439ec"
related: []
---

# Grid layouts (personal table column layouts)

## Purpose
Some Heblo tables have many columns. Each person can reorder, resize and hide columns; Heblo saves
that arrangement to the server under the user's account so the table looks the same tomorrow and
on another computer. Nothing about the table's *data* is stored — only the column layout.

Used today by two tables:

| Grid key | Page |
|---|---|
| `manufacturing-stock-analysis` | Manufacturing stock analysis (`ManufacturingStockAnalysis.tsx`) |
| `purchase-stock-analysis` | Purchase stock analysis (`PurchaseStockAnalysis.tsx`) |

## Users & screens
Every signed-in user of the pages above, via the column chooser and draggable/resizable headers
(`frontend/src/features/grid-layout`: `useGridLayout`, `ColumnChooser`, `GridHeader`).

Behaviour in the browser (`useGridLayout`):
- On page open: `GET` the saved layout. Saved columns are merged with the columns the page
  currently defines — new columns appear at their default position, removed columns are
  dropped, columns marked `canHide: false` are always visible; order is renumbered 0..n.
  No saved layout (or a load error) → default order and widths.
- On every change (hide/show, reorder, finished resize): `PUT` the full layout after a 500 ms
  debounce. Save errors are swallowed silently — the screen already shows the change.
- "Reset" deletes the saved layout.

API (`GridLayoutsController`, `/api/GridLayouts/{gridKey}`):
- `GET` → `{ gridKey, columns: [{ id, order, width, hidden }], lastModified }` or `null`.
- `PUT` body `{ columns: [...] }` → upsert (`INSERT … ON CONFLICT ("UserId","GridKey") DO UPDATE`).
- `DELETE` → remove the row (no-op when absent).
- Database errors return HTTP 500 with `ErrorCodes.DatabaseError`.

No MCP tool, no dashboard tile.

## Processes
None — plain per-user CRUD, no jobs, no external side effects.

## Data owned
- `public."GridLayouts"` — one row per (user, grid): `Id`, `UserId` (≤255; the caller's Entra
  object id, or e-mail if no id claim), `GridKey` (≤100), `LayoutJson` (`{"columns":[{"id","order",
  "width","hidden"}]}`), `LastModified` (UTC). Unique index on (`UserId`, `GridKey`). Rows are
  never cleaned up (no retention, not removed when a user leaves).

## External systems
None.

## Dependencies
- Reads Users (`ICurrentUserService`) for the user key.
- Read by: the two stock-analysis pages (Manufacture and Purchase modules' frontends).

## Known quirks
- `GridLayoutsController` has no `[Authorize]` attribute and there is no global fallback policy;
  an unauthenticated call has no id/e-mail claim and the handler throws
  `InvalidOperationException` (HTTP 500) rather than returning 401. No data is exposed.
- The `gridKey` path segment is not validated; a key longer than 100 characters fails at the
  database and returns 500.
- A `LayoutJson` that cannot be parsed is logged as a warning and treated as "no saved layout"
  (the next save overwrites it).
- Silent save failures mean a user can believe a layout is saved when it is not; it then
  reverts on the next page load.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/` — Get / Save / Reset handlers
- `backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs` — raw-SQL upsert
- `backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutConfiguration.cs` — table mapping
- `backend/src/Anela.Heblo.API/Controllers/GridLayoutsController.cs` — endpoints
- `frontend/src/features/grid-layout/useGridLayout.ts` — merge, debounce, save
