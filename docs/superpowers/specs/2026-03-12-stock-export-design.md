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

---

## Architecture

### 1. Backend — Export Flag on Existing Endpoints

**No new endpoints.** Both existing GET endpoints accept a new `bool Export` query parameter.

When `Export = true`, the handler bypasses pagination (`Skip`/`Take`) and returns all matching records. Response DTO is unchanged.

**Affected request classes:**
- `GetPurchaseStockAnalysisRequest` — add `bool Export = false`
- `GetManufacturingStockAnalysisRequest` — add `bool Export = false`

**Affected handlers:**
- `GetPurchaseStockAnalysisHandler` — skip pagination when `Export == true`
- `GetManufacturingStockAnalysisHandler` — skip pagination when `Export == true`

Example request:
```
GET /api/purchase-stock-analysis?export=true&fromDate=2025-01-01&severity=Critical
```

The OpenAPI TypeScript client regenerates automatically on next build, exposing the `export` param to the frontend.

---

### 2. Frontend — Shared XLSX Utility

**New file:** `frontend/src/utils/exportToXlsx.ts`

```typescript
exportToXlsx<T>(
  rows: T[],
  columns: { key: keyof T; header: string }[],
  filename: string
): void
```

- Uses the `xlsx` (SheetJS) npm package
- Creates a single-sheet workbook
- Maps column definitions to header row + data rows
- Triggers browser file download via `XLSX.writeFile`

**Install:** `npm install xlsx` in the frontend package.

---

### 3. Frontend — Export Button Wiring (per page)

Each page implements the export flow independently, calling the shared utility.

**Flow on button click:**
1. Set `isExporting = true` (shows spinner on button)
2. Call existing API hook/function with current filter state + `export: true`
3. Pass returned rows + column definitions + filename to `exportToXlsx()`
4. Trigger browser download
5. Set `isExporting = false`

**Error handling:** On failure, set `isExporting = false` and surface an error toast (using the existing toast pattern in the codebase).

---

### 4. Column Definitions

#### PurchaseStockAnalysis — `StockAnalysisItemDto`

| Column Header (CZ) | DTO field |
|---|---|
| Kód produktu | `productCode` |
| Název produktu | `productName` |
| Typ produktu | `productType` |
| Dostupný sklad | `availableStock` |
| Objednané | `orderedStock` |
| Efektivní sklad | `effectiveStock` |
| Min. úroveň skladu | `minStockLevel` |
| Optimální sklad | `optimalStockLevel` |
| Spotřeba v období | `consumptionInPeriod` |
| Denní spotřeba | `dailyConsumption` |
| Dní do vyčerpání | `daysUntilStockout` |
| Efektivita skladu (%) | `stockEfficiencyPercentage` |
| Závažnost | `severity` |
| Poslední nákup – datum | `lastPurchase.date` |
| Poslední nákup – dodavatel | `lastPurchase.supplier` |
| Poslední nákup – množství | `lastPurchase.amount` |
| Poslední nákup – cena | `lastPurchase.price` |
| Dodavatel | `supplier` |
| Doporučené objednací množství | `recommendedOrderQuantity` |
| Nakonfigurováno | `isConfigured` |

#### ManufacturingStockAnalysis — `ManufacturingStockItemDto`

| Column Header (CZ) | DTO field |
|---|---|
| Kód | `code` |
| Název | `name` |
| Sklad aktuální | `currentStock` |
| Sklad ERP | `erpStock` |
| Sklad E-shop | `eshopStock` |
| Sklad transport | `transportStock` |
| Primární zdroj skladu | `primaryStockSource` |
| Rezervace | `reserve` |
| Plánováno | `planned` |
| Prodeje v období | `salesInPeriod` |
| Denní prodeje | `dailySalesRate` |
| Optimální dny (nastavení) | `optimalDaysSetup` |
| Dní skladu | `stockDaysAvailable` |
| Minimální sklad | `minimumStock` |
| Přebytečné (%) | `overstockPercentage` |
| Velikost dávky | `batchSize` |
| Produktová rodina | `productFamily` |
| Závažnost | `severity` |
| Nakonfigurováno | `isConfigured` |

---

## File Naming

| Page | Filename pattern |
|---|---|
| Analýza skladových zásob | `purchase-stock-analysis-YYYY-MM-DD.xlsx` |
| Řízení zásob výrobků | `manufacturing-stock-analysis-YYYY-MM-DD.xlsx` |

Date is the current local date at time of export.

---

## Files to Create / Modify

### New files
- `frontend/src/utils/exportToXlsx.ts` — shared XLSX utility

### Modified files
**Backend:**
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisRequest.cs` — add `bool Export`
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs` — skip pagination when `Export == true`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetStockAnalysis/GetManufacturingStockAnalysisRequest.cs` — add `bool Export`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetStockAnalysis/GetManufacturingStockAnalysisHandler.cs` — skip pagination when `Export == true`

**Frontend:**
- `frontend/package.json` — add `xlsx` dependency
- `frontend/src/components/pages/PurchaseStockAnalysis.tsx` — wire export button
- `frontend/src/components/pages/ManufacturingStockAnalysis.tsx` — wire export button

---

## Out of Scope

- CSV format (XLSX only)
- Column selection UI
- Backend-generated XLSX
- Export for InventoryList or WarehouseStatistics pages
- Scheduled/async export jobs
