# Stock Analysis Export — Design Spec

**Date:** 2026-03-12
**Feature:** XLSX export for Analýza skladových zásob and Řízení zásob výrobků
**Status:** Approved

---

## Overview

Add working XLSX export to two stock analysis pages that currently have placeholder export buttons (`handleExport` logs to console only):

- **Analýza skladových zásob** (`PurchaseStockAnalysis.tsx`) — purchase materials stock analysis
- **Řízení zásob výrobků** (`ManufacturingStockAnalysis.tsx`) — manufactured products stock management

Export behaviour:
- Exports **all rows** matching the current active filters (not just the visible page)
- **XLSX format only** (no CSV option)
- **All columns** included, in the same order as the table
- Filename includes page name and date: `purchase-stock-analysis-2026-03-12.xlsx` / `manufacturing-stock-analysis-2026-03-12.xlsx`
- No row-count guard needed — datasets are hundreds of rows, well within in-memory limits

---

## Architecture

### 1. Backend — Export Flag on Existing Endpoints

**No new endpoints.** Both existing GET endpoints accept a new `bool Export` query parameter.

When `Export = true`, the handler bypasses pagination (`Skip`/`Take`) and returns all matching records. Response DTO is unchanged.

**Affected request classes:**
- `GetPurchaseStockAnalysisRequest` — add `bool Export = false` (add last to minimise generated client churn)
- `GetManufacturingStockAnalysisRequest` — add `bool Export = false` (add last)

**Affected handlers:**
- `GetPurchaseStockAnalysisHandler` — skip pagination when `Export == true`
- `GetManufacturingStockAnalysisHandler` — skip pagination when `Export == true`

Example request:
```
GET /api/purchase-stock-analysis?export=true&fromDate=2025-01-01&stockStatus=Critical
```

The OpenAPI TypeScript client regenerates automatically on next build, exposing the `export` param to the frontend.

---

### 2. Frontend — Shared XLSX Utility

**New file:** `frontend/src/utils/exportToXlsx.ts`

```typescript
exportToXlsx<T>(
  rows: T[],
  columns: { header: string; value: (row: T) => unknown }[],
  filename: string
): void
```

- Uses the `xlsx` (SheetJS) npm package — `npm install xlsx`
- Creates a single-sheet workbook
- Maps column definitions to header row + data rows using the `value` accessor (supports nested fields like `row.lastPurchase?.supplierName`)
- Triggers browser file download via `XLSX.writeFile`
- Commit both `package.json` and `package-lock.json`

---

### 3. Frontend — Export Button Wiring (per page)

The export is a **one-time imperative API call** (not a React Query `useQuery`). Each page calls the API directly with current filter state + `export: true`, then passes the result to `exportToXlsx`.

**Flow on button click:**
1. Set `isExporting = true` (shows spinner on button)
2. Call the API directly with current filter state + `export: true` (see per-page details below)
3. Pass returned items + column definitions + filename to `exportToXlsx()`
4. Trigger browser download
5. Set `isExporting = false`

**Error handling:** On failure, set `isExporting = false` and surface an error toast (using the existing toast pattern in the codebase).

#### PurchaseStockAnalysis — generated client

Uses the generated API client method. After OpenAPI regeneration, `export` appears as the last positional argument:

```typescript
const apiClient = getAuthenticatedApiClient();
const result = await apiClient.purchaseStockAnalysis_GetStockAnalysis(
  filters.fromDate ?? null,
  filters.toDate ?? null,
  filters.stockStatus,
  filters.onlyConfigured,
  filters.searchTerm ?? null,
  undefined, // pageNumber — skipped on export
  undefined, // pageSize — skipped on export
  filters.sortBy,
  filters.sortDescending,
  true,       // export
);
```

#### ManufacturingStockAnalysis — manual URLSearchParams fetch

Uses the existing manual fetch pattern from `useManufacturingStockAnalysis.ts`. Add `params.append('export', 'true')` to the URLSearchParams builder and omit pagination params. Reuse the same `baseUrl` + absolute URL construction pattern already in the hook.

---

### 4. Column Definitions

#### PurchaseStockAnalysis — `StockAnalysisItemDto`

| Column Header (CZ) | Accessor |
|---|---|
| Kód produktu | `row.productCode` |
| Název produktu | `row.productName` |
| Typ produktu | `row.productType` |
| Dostupný sklad | `row.availableStock` |
| Objednané | `row.orderedStock` |
| Efektivní sklad | `row.effectiveStock` |
| Min. úroveň skladu | `row.minStockLevel` |
| Optimální sklad | `row.optimalStockLevel` |
| Spotřeba v období | `row.consumptionInPeriod` |
| Denní spotřeba | `row.dailyConsumption` |
| Dní do vyčerpání | `row.daysUntilStockout` |
| Efektivita skladu (%) | `row.stockEfficiencyPercentage` |
| Závažnost | `row.severity` |
| Min. objednací množství | `row.minimalOrderQuantity` |
| Poslední nákup – datum | `row.lastPurchase?.date` |
| Poslední nákup – dodavatel | `row.lastPurchase?.supplierName` |
| Poslední nákup – množství | `row.lastPurchase?.amount` |
| Poslední nákup – jedn. cena | `row.lastPurchase?.unitPrice` |
| Poslední nákup – celk. cena | `row.lastPurchase?.totalPrice` |
| Dodavatel | `row.supplier` |
| Doporučené objednací množství | `row.recommendedOrderQuantity` |
| Nakonfigurováno | `row.isConfigured` |

#### ManufacturingStockAnalysis — `ManufacturingStockItemDto`

| Column Header (CZ) | Accessor |
|---|---|
| Kód | `row.code` |
| Název | `row.name` |
| Sklad aktuální | `row.currentStock` |
| Sklad ERP | `row.erpStock` |
| Sklad E-shop | `row.eshopStock` |
| Sklad transport | `row.transportStock` |
| Primární zdroj skladu | `row.primaryStockSource` |
| Rezervace | `row.reserve` |
| Plánováno | `row.planned` |
| Prodeje v období | `row.salesInPeriod` |
| Denní prodeje | `row.dailySalesRate` |
| Optimální dny (nastavení) | `row.optimalDaysSetup` |
| Dní skladu | `row.stockDaysAvailable` |
| Minimální sklad | `row.minimumStock` |
| Přebytečné (%) | `row.overstockPercentage` |
| Velikost dávky | `row.batchSize` |
| Produktová rodina | `row.productFamily` |
| Závažnost | `row.severity` |
| Nakonfigurováno | `row.isConfigured` |

---

## File Naming

| Page | Filename pattern |
|---|---|
| Analýza skladových zásob | `purchase-stock-analysis-YYYY-MM-DD.xlsx` |
| Řízení zásob výrobků | `manufacturing-stock-analysis-YYYY-MM-DD.xlsx` |

Date is the current local date at time of export.

---

## Testing

**Backend — extend existing handler tests:**
- `GetPurchaseStockAnalysisHandlerTests` — add case: `Export = true` bypasses pagination, all filtered items returned
- `GetManufacturingStockAnalysisHandlerTests` — same

**Frontend — extend existing component tests:**
- `PurchaseStockAnalysis.test.tsx` — export button sets loading state, calls API with `export=true`, calls `exportToXlsx` with correct filename and all rows
- `ManufacturingStockAnalysis.test.tsx` — same
- `exportToXlsx.test.ts` — unit tests: correct headers, data rows, nested accessor works, browser download triggered

---

## Files to Create / Modify

### New files
- `frontend/src/utils/exportToXlsx.ts` — shared XLSX utility

### Modified files
**Backend:**
- `GetPurchaseStockAnalysisRequest.cs` — add `bool Export = false`
- `GetPurchaseStockAnalysisHandler.cs` — skip pagination when `Export == true`
- `GetManufacturingStockAnalysisRequest.cs` — add `bool Export = false`
- `GetManufacturingStockAnalysisHandler.cs` — skip pagination when `Export == true`

**Frontend:**
- `frontend/package.json` + `frontend/package-lock.json` — add `xlsx` dependency
- `frontend/src/components/pages/PurchaseStockAnalysis.tsx` — wire export button (generated client)
- `frontend/src/components/pages/ManufacturingStockAnalysis.tsx` — wire export button (manual fetch)

---

## Out of Scope

- CSV format (XLSX only)
- Column selection UI
- Backend-generated XLSX
- Export for InventoryList or WarehouseStatistics pages
- Scheduled/async export jobs
