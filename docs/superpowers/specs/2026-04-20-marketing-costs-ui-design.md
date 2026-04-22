# Marketing Costs UI — Design Spec

## Context

The marketing invoice import system (Google Ads + Meta Ads adapters) already persists transaction data to `imported_marketing_transactions`, but there is no UI to view this data. This spec defines a new "Náklady" page under a "Marketing" menu group, allowing users to browse and inspect imported marketing transactions.

## Scope

- New "Marketing" menu group in sidebar (after Finance, role-gated by `marketing_reader`)
- "Náklady" list page at `/marketing/costs`
- Detail modal on row click
- Backend API endpoints (list + detail)
- Schema migration to persist additional fields (Description, Currency, RawData)

## Data Model Changes

Add three nullable columns to `imported_marketing_transactions`:

| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| `description` | `varchar(500)` | yes | Transaction description from platform |
| `currency` | `varchar(10)` | yes | Currency code (CZK, USD, etc.) |
| `raw_data` | `text` | yes | Full JSON response from platform API |

Update `ImportedMarketingTransaction` entity and `MarketingInvoiceImportService` to persist these from the `MarketingTransaction` source DTO.

## Backend API

Controller: `MarketingCostsController`

### GET /api/marketing-costs

Paginated list with filtering and sorting.

**Query parameters:**
- `platform` (string, optional) — filter by "GoogleAds" or "MetaAds"
- `dateFrom` (DateTime, optional) — TransactionDate >= dateFrom
- `dateTo` (DateTime, optional) — TransactionDate <= dateTo
- `isSynced` (bool?, optional) — filter by sync status
- `pageNumber` (int, default 1)
- `pageSize` (int, default 20)
- `sortBy` (string, optional) — column name: "amount", "transactionDate", "importedAt"
- `sortDescending` (bool, default true)

**Response DTO (`MarketingCostListItemDto`):**
- `Id` (int)
- `TransactionId` (string)
- `Platform` (string)
- `Amount` (decimal)
- `Currency` (string?)
- `TransactionDate` (DateTime)
- `ImportedAt` (DateTime)
- `IsSynced` (bool)

**Paginated response:** `{ items, totalCount, pageNumber, pageSize, totalPages }`

### GET /api/marketing-costs/{id}

Full detail for a single transaction.

**Response DTO (`MarketingCostDetailDto`):**
- All fields from `MarketingCostListItemDto`
- `Description` (string?)
- `ErrorMessage` (string?)
- `RawData` (string?)

## Frontend

### Navigation

- New "Marketing" section in `Sidebar.tsx` `navigationSections` array
- Position: after "Finance" section
- Icon: `Megaphone` (from lucide-react)
- Role gate: `marketing_reader`
- Items: `{ id: "naklady", name: "Náklady", href: "/marketing/costs" }`

### Route

Add to `App.tsx`: `<Route path="/marketing/costs" element={<MarketingCostsList />} />`

### List Page (`MarketingCostsList.tsx`)

Follows `CatalogList.tsx` pattern:
- Full-height flex layout using `PAGE_CONTAINER_HEIGHT`
- Filter bar: Platform select, date range inputs, sync status select, "Filtrovat" button
- Filters apply on button click (separate input vs applied state)
- URL-synced via `useSearchParams()` (filters, pagination, sorting)
- Sticky table header with sortable columns (Amount, TransactionDate, ImportedAt)
- Platform shown as colored badge (blue = GoogleAds, purple = MetaAds)
- Sync status: green ✓ / red ✗
- Rows clickable → opens detail modal
- Shared `<Pagination>` component at bottom

### Detail Modal (`MarketingCostDetail.tsx`)

Follows `CatalogDetail.tsx` pattern:
- Props: `{ item: MarketingCostListItemDto | null, isOpen: boolean, onClose: () => void }`
- Fetches full detail via `useMarketingCostDetail(id)` on open
- Full-screen overlay, max-w-[700px], backdrop click / Escape to close
- Layout:
  - Header: "Detail transakce" + platform badge + close button
  - 2-column grid: TransactionId, Platform, Amount+Currency (large), TransactionDate, ImportedAt, Sync status badge
  - Description: grey box (only if present)
  - Error message: red warning box (only if ErrorMessage non-null)
  - Raw data: collapsible `<details>` with dark-themed `<pre>` block (formatted JSON)

### API Hook (`useMarketingCosts.ts`)

- `useMarketingCostsQuery(filters)` — React Query hook for list endpoint
- `useMarketingCostDetail(id)` — React Query hook for detail endpoint
- Uses `getAuthenticatedApiClient()` with absolute URLs

## Role

New role `marketing_reader` required in Microsoft Entra ID app registration. Sidebar section visibility gated by `hasRole('marketing_reader')`.

## Testing

- **Backend unit tests**: MediatR handler tests for list query (filtering, pagination, sorting) and detail query
- **Frontend**: Component renders correctly with mock data, filters update URL params
- **E2E**: Not in initial scope (requires role setup + test data seeding)

## Verification

1. Run `dotnet build` — backend compiles
2. Run `dotnet test` — new handler tests pass
3. Run `npm run build` — frontend compiles
4. Run `npm start` — navigate to /marketing/costs, verify page renders with empty state
5. Manual: after import jobs run, verify data appears in list and detail modal works
