# Application Module Map

**Purpose:** a stable, exhaustive partition of the Anela Heblo codebase into **52 analysis units** ("parts").
Each part is small enough that a single focused analysis session can hold it in context, and large enough that the
analysis produces something meaningful. The map is designed to be **iterated**: pick one part, analyse it, move on.

> This document describes *cuts*, not architecture. For architecture read
> `docs/📘 Architecture Documentation – MVP Work.md` and `docs/architecture/filesystem.md`.

---

## How the cut was made

**Cut rules applied, in priority order:**

1. **Vertical slice first.** A domain part owns its Domain entities + Application use cases + Persistence config
   + Controller + frontend page(s) + hooks + tests. The codebase already follows Vertical Slice organisation, so
   the natural seam is `Features/<Name>` across `Domain` / `Application` / `Persistence` + the matching
   controller + the matching frontend route.
2. **Split when a folder is too big for one sitting.** `Application/Features/Catalog` (9.8k LOC) and
   `Application/Features/Manufacture` (7.4k LOC) are each split into 3–5 parts along their `UseCases/` boundaries.
3. **Merge when a folder is too thin.** Sub-1k-LOC features are grouped with the neighbour they actually
   collaborate with (e.g. `CarrierCooling` + `ExpeditionList` + `ExpeditionListArchive` + `ShipmentLabels`
   + `ShoptetCustomers` → one *Expedition & Shipping* part).
4. **Adapters follow their consumer when 1:1, otherwise stand alone.** `Adapters.Plaud` is owned by
   *Meeting Tasks*; `Adapters.Flexi` is consumed by ~10 parts, so it is its own part.
5. **Non-domain concerns get explicit parts.** Auth, background execution, persistence core, telemetry,
   client generation, CI/CD, tests and docs are parts too — they are where a lot of the risk lives.

**Sizing target:** roughly 1.5k–6k LOC of hand-written code per part (excluding generated code and migrations).

**Notation:**
- `BE` = backend LOC (`.cs`, hand-written), `FE` = frontend LOC (`.ts`/`.tsx`, excluding `__tests__`).
- Paths are repo-relative. A trailing `/` means the whole subtree.
- Sizes are approximate — measured at the time of writing, meant for triage not accounting.

---

## Summary table

### A. Business domain parts (33)

| ID | Part | Approx. size | Primary route(s) |
|----|------|--------------|------------------|
| A01 | Catalog Core & Data Aggregation | BE ~3.6k | — (background) |
| A02 | Catalog Browsing & Product Detail | BE ~2.0k / FE ~4.2k | `/catalog` |
| A03 | Product Costing & Margin Calculation | BE ~1.9k | — (engine) |
| A04 | Margin Analytics & Reporting | BE ~2.2k / FE ~1.2k | `/products/margins`, `/analytics/product-margin-summary` |
| A05 | Warehouse Inventory & Stock Taking | BE ~2.2k / FE ~2.0k | `/logistics/inventory` |
| A06 | Stock-Up Operations | BE ~1.2k / FE ~0.9k | `/stock-up-operations` |
| A07 | Transport Boxes | BE ~3.0k / FE ~3.5k | `/logistics/transport-boxes`, `/logistics/receive-boxes` |
| A08 | Warehouse Terminal | BE ~0.5k / FE ~2.8k | `/terminal/*` |
| A09 | Packaging (Baleni) Workflow | BE ~2.4k / FE ~2.7k | `/baleni/*` |
| A10 | Packing Materials | BE ~2.0k / FE ~1.8k | `/logistics/packing-materials` |
| A11 | Expedition & Shipping | BE ~1.6k / FE ~0.9k | `/logistics/expedition-archive`, `/customer/expedition-settings` |
| A12 | Gift Packages | BE ~0.9k / FE ~1.7k | `/logistics/gift-package-manufacturing` |
| A13 | Manufacture Orders & Calendar | BE ~2.2k / FE ~2.7k | `/manufacturing/orders` |
| A14 | Batch Planning & Batch Calculator | BE ~2.5k / FE ~1.7k | `/manufacturing/batch-planning`, `/manufacturing/batch-calculator` |
| A15 | Manufacture Execution & Output | BE ~1.9k / FE ~0.6k | `/manufacturing/output` |
| A16 | Manufacturing Stock Analysis | BE ~1.2k / FE ~1.6k | `/manufacturing/stock-analysis` |
| A17 | Manufacture Inventory, Lots & Settings | BE ~1.8k / FE ~1.6k | `/manufacturing/inventory`, `/manufacturing/product-inventory`, `/manufacturing/material-containers` |
| A18 | Purchase Orders | BE ~1.6k / FE ~2.0k | `/purchase/orders` |
| A19 | Purchase Stock Analysis & Suppliers | BE ~1.1k / FE ~1.1k | `/purchase/stock-analysis` |
| A20 | Issued Invoices & Invoice Import | BE ~2.2k / FE ~1.2k | `/customer/issued-invoices` |
| A21 | Invoice Classification | BE ~1.8k / FE ~1.1k | `/purchase/invoice-classification` |
| A22 | Financial Overview & Bank Statements | BE ~2.2k / FE ~1.4k | `/finance/overview`, `/finance/bank-statements` |
| A23 | Marketing Calendar & Marketing Invoices | BE ~2.3k / FE ~1.9k | `/marketing/calendar` |
| A24 | Photobank | BE ~3.3k / FE ~2.3k | `/marketing/photobank` |
| A25 | Leaflet Generator | BE ~1.5k / FE ~1.2k | `/leaflet-generator` |
| A26 | AI Articles | BE ~2.2k / FE ~0.7k | `/articles` |
| A27 | Knowledge Base (RAG) | BE ~2.5k / FE ~1.4k | `/knowledge-base` |
| A28 | Meeting Tasks (Plaud) | BE ~3.2k / FE ~1.2k | `/automation/meeting-tasks` |
| A29 | Customer Support (Smartsupp) | BE ~4.9k / FE ~2.2k | `/customer/smartsupp` |
| A30 | E-shop Orders & Customers (Shoptet) | BE ~4.9k | — (integration) |
| A31 | Journal | BE ~1.5k / FE ~0.8k | `/journal` |
| A32 | Dashboard & Tiles | BE ~1.9k / FE ~2.1k | `/` |
| A33 | Data Quality | BE ~2.0k / FE ~0.8k | `/automation/data-quality` |

### B. Platform & cross-cutting parts (10)

| ID | Part | Approx. size |
|----|------|--------------|
| B01 | Authorization & Access Management | BE ~2.4k / FE ~1.6k |
| B02 | Users, Identity & Org Chart | BE ~1.2k / FE ~1.1k |
| B03 | Feature Flags, Configuration & Grid Layouts | BE ~1.1k / FE ~0.7k |
| B04 | Background Execution (Hangfire + Refresh/Hydration) | BE ~2.9k / FE ~0.4k |
| B05 | Persistence Core & Migrations | BE ~1.5k (+ ~367k generated migrations) |
| B06 | API Host & Composition Root | BE ~1.9k |
| B07 | Telemetry, Health & Diagnostics | BE ~1.0k / FE ~0.2k |
| B08 | Documents, File Storage & Printing | BE ~2.1k |
| B09 | API Contract & Client Generation | generated + ~0.3k tooling |
| B10 | MCP Server | BE ~1.0k |

### C. Integration adapters (4)

| ID | Part | Approx. size |
|----|------|--------------|
| C01 | FlexiBee ERP Adapter | BE ~4.4k |
| C02 | Microsoft 365 & Azure Adapters | BE ~1.5k |
| C03 | AI / LLM & Web Search Adapters | BE ~0.5k |
| C04 | Ancillary External Adapters | BE ~1.5k |

### D. Delivery & tooling (5)

| ID | Part | Approx. size |
|----|------|--------------|
| D01 | Frontend Shell, Layout & Navigation | FE ~2.5k |
| D02 | Frontend Shared UI, Hooks & Utilities | FE ~4.0k |
| D03 | Automated Test Suites & Test Infrastructure | ~40k test LOC |
| D04 | CI/CD, Docker & Deployment | ~2k scripts/YAML |
| D05 | Documentation & Agent Tooling | ~docs only |

---

# A. Business domain parts

## A01 — Catalog Core & Data Aggregation

**Purpose:** the merge engine that assembles the unified product catalog from ERP, e-shop, manufacture, purchase and
analytics sources; caching, scheduled refresh and resilience around it.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/`
- `backend/src/Anela.Heblo.Application/Features/Catalog/Cache/`
- `backend/src/Anela.Heblo.Domain/Features/Catalog/`
- `backend/src/Anela.Heblo.Persistence/Catalog/`
- `frontend/src/api/hooks/useManualCatalogRefresh.ts`

**Key entry points:** `CatalogMergeService`, `CatalogMergeScheduler`, `CatalogCacheStore`, `CatalogDataRefreshService`,
`CatalogResilienceService`, the `*SourceAdapter` classes.

**Depends on:** C01 (Flexi), A30 (Shoptet), B04 (refresh scheduling).
**Consumed by:** A02–A06, A13–A19, A32.

**Analysis notes:** this is the single highest fan-in part in the app. The `*Adapter` files here are inbound
anti-corruption for other modules — a good place to look for hidden coupling.

---

## A02 — Catalog Browsing & Product Detail

**Purpose:** the catalog list/detail UI and the queries behind it — filtering, paging, product composition, usage.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogList/`
- `.../Catalog/UseCases/GetCatalogDetail/`
- `.../Catalog/UseCases/GetProductComposition/`
- `.../Catalog/UseCases/GetProductUsage/`
- `.../Catalog/UseCases/UpdateProductCompositionOrder/`
- `.../Catalog/UseCases/RecalculateProductWeight/`
- `.../Catalog/Contracts/`, `.../Catalog/Validators/`
- `.../Catalog/Services/ProductCatalogQueryService.cs`, `ProductWeightRecalculationService.cs`, `EshopStockDomainService.cs`
- `backend/src/Anela.Heblo.API/Controllers/CatalogController.cs`
- `frontend/src/components/pages/CatalogList.tsx`, `CatalogDetail.tsx`
- `frontend/src/components/catalog/`
- `frontend/src/api/hooks/useCatalog.ts`, `useCatalogAutocomplete.ts`, `useProductUsage.ts`, `useUpdateProductCompositionOrder.ts`

**Depends on:** A01.

---

## A03 — Product Costing & Margin Calculation

**Purpose:** the costing engine — how a product's manufacture, material and sales costs are derived, and the
margin maths built on top.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/`
- `.../Catalog/Services/MarginCalculationService.cs`, `SafeMarginCalculator.cs`, `SalesCost.cs`
- `.../Catalog/UseCases/GetProductMargins/`
- `backend/src/Anela.Heblo.API/Controllers/ProductMarginsController.cs`
- `frontend/src/api/hooks/useProductMargins.ts`
- `docs/features/margins_v2/`

**Depends on:** A01, A13 (manufacture cost inputs).

**Analysis notes:** four competing cost providers (`Direct`, `Flat`, `ManufactureBasedMaterial`, `Sales`) —
worth checking which one actually wins in which scenario.

---

## A04 — Margin Analytics & Reporting

**Purpose:** aggregated margin reports, product margin summary, invoice/bank import statistics.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/Analytics/`
- `backend/src/Anela.Heblo.Domain/Features/Analytics/`
- `backend/src/Anela.Heblo.Persistence.Analytics/`
- `backend/src/Anela.Heblo.API/Controllers/AnalyticsController.cs`
- `frontend/src/components/pages/ProductMarginSummary.tsx`, `ProductMarginsList.tsx`
- `frontend/src/components/pages/automation/InvoiceImportStatistics.tsx`
- `frontend/src/api/hooks/useProductMarginSummary.ts`, `useInvoiceImportStatistics.ts`
- `frontend/src/utils/timePeriod/`, `backend/src/Anela.Heblo.Application/Common/TimePeriods/`

**Depends on:** A03, A20, A22.

**Analysis notes:** has its **own DbContext and migration chain** (`Persistence.Analytics`) — separate from B05.

---

## A05 — Warehouse Inventory & Stock Taking

**Purpose:** physical inventory counting for warehouse stock, stock-taking history and warehouse statistics.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/`
- `.../Catalog/UseCases/SubmitStockTaking/`, `GetStockTakingHistory/`, `GetWarehouseStatistics/`
- `backend/src/Anela.Heblo.Persistence/Catalog/Inventory/`, `backend/src/Anela.Heblo.Persistence/Catalog/Stock/`
- `backend/src/Anela.Heblo.Persistence/Logistics/StockTaking/`
- `backend/src/Anela.Heblo.Domain/Features/Logistics/StockTaking/`
- `backend/src/Anela.Heblo.API/Controllers/StockTakingController.cs`, `WeatherForecastController.cs` *(see B03)*
- `frontend/src/components/pages/InventoryList.tsx`, `WarehouseStatistics.tsx`
- `frontend/src/components/inventory/`
- `frontend/src/api/hooks/useInventory.ts`, `useStockTaking.ts`, `useWarehouseStatistics.ts`

**Depends on:** A01, C01.

---

## A06 — Stock-Up Operations

**Purpose:** the queue of stock-up (replenishment) operations pushed to the ERP/e-shop, with retry and accept-failure
handling.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetStockUpOperations/`, `GetStockUpOperationsSummary/`,
  `RetryStockUpOperation/`, `AcceptStockUpOperation/`
- `.../Catalog/Services/StockUpProcessingService.cs`, `IStockUpProcessingService.cs`, `StockUpOperationResult.cs`
- `backend/src/Anela.Heblo.API/Controllers/StockUpOperationsController.cs`
- `frontend/src/pages/StockOperationsPage.tsx`
- `frontend/src/api/hooks/useStockUpOperations.ts`
- `frontend/test/e2e/stock-operations/`

**Depends on:** A01, C01, A30.

---

## A07 — Transport Boxes

**Purpose:** the transport box lifecycle — create, fill, move between states, receive at destination.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/` (all `*TransportBox*`, `AddItemToBox`,
  `RemoveItemFromBox`, `OpenOrResumeBoxByCode`)
- `.../Logistics/Contracts/`, `.../Logistics/Services/`, `.../Logistics/Infrastructure/`, `.../Logistics/Picking/`
- `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/`
- `backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxes/`
- `backend/src/Anela.Heblo.API/Controllers/TransportBoxController.cs`, `LogisticsController.cs`
- `frontend/src/components/pages/TransportBoxList.tsx`, `TransportBoxDetail.tsx`, `TransportBoxReceive.tsx`,
  `AddItemToBoxModal.tsx`, `LocationSelectionModal.tsx`
- `frontend/src/components/transport/`
- `frontend/src/api/hooks/useTransportBoxes.ts`, `useTransportBoxReceive.ts`, `useTransportBoxTransitions.ts`
- `frontend/test/e2e/transport/`

**Depends on:** A01, A05.

**Analysis notes:** the state machine (`TransportBoxState`, `TransportBoxTransition`, `TransportBoxStateNode`) is the
core artefact here.

---

## A08 — Warehouse Terminal

**Purpose:** the touch-optimised shop-floor terminal — box check, box fill, receive, lot identification against
purchase orders.

**Owns:**
- `frontend/src/components/terminal/`
- `frontend/src/components/transport/touch/`
- `frontend/src/api/hooks/useBoxFill.ts`, `useLastAddedItem.ts`
- `backend/src/Anela.Heblo.API/Controllers/LotsController.cs` *(shared with A17)*
- `frontend/test/e2e/terminal/`
- Routes: `/terminal/*` in `frontend/src/App.tsx`

**Depends on:** A07, A17, A18.

**Analysis notes:** distinct UX layer (`TerminalLayout`, `shell/`) with its own navigation model — analyse for
offline/latency behaviour rather than for business rules.

---

## A09 — Packaging (Baleni) Workflow

**Purpose:** the order-packing workstation — scan order, complete packing, print labels, packing statistics and
per-user dashboards.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/Packaging/`
- `backend/src/Anela.Heblo.Domain/Features/Packaging/`
- `backend/src/Anela.Heblo.Persistence/Features/Packaging/`, `backend/src/Anela.Heblo.Persistence/Repositories/Packaging/`
- `backend/src/Anela.Heblo.API/Controllers/PackagingController.cs`
- `frontend/src/components/baleni/`
- `frontend/src/api/hooks/useScanPackingOrder.ts`, `useCompletePackingOrder.ts`, `usePackages.ts`,
  `usePackingDashboard.ts`, `usePackingStatistics.ts`, `useOrderTrackingNumber*.ts`, `useResetOrderShipment.ts`
- `frontend/test/e2e/baleni/`
- Routes: `/baleni/*`

**Depends on:** A30 (order data), A11 (labels).

**Analysis notes:** `IPackingOrderClient` is declared in `ShoptetOrders` but consumed here — a known boundary smell
(see latest commit on this branch).

---

## A10 — Packing Materials

**Purpose:** stock and consumption tracking for packaging materials (boxes, filler, tape).

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/`
- `backend/src/Anela.Heblo.Domain/Features/PackingMaterials/`
- `backend/src/Anela.Heblo.Persistence/PackingMaterials/`
- `backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs`
- `frontend/src/components/packing-materials/`, `frontend/src/pages/PackingMaterialsPage.tsx`
- `frontend/src/api/hooks/usePackingMaterials.ts`

**Depends on:** A01, A09.

---

## A11 — Expedition & Shipping

**Purpose:** expedition (picking) lists and their archive, shipment label generation, carrier cooling rules and
customer-facing expedition settings.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/`
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/`
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/`
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/`
- `backend/src/Anela.Heblo.Application/Features/ShoptetCustomers/`
- `backend/src/Anela.Heblo.Domain/Features/Logistics/CarrierCoolingSetting.cs`, `Carriers.cs`,
  `CarrierExtensions.cs`, `DeliveryHandling.cs`, `IShippingMethodCatalog.cs`, `ICarrierCoolingRepository.cs`,
  `Weather/`
- `backend/src/Anela.Heblo.Persistence/Logistics/CarrierCooling/`
- `backend/src/Anela.Heblo.API/Controllers/ExpeditionListController.cs`, `ExpeditionListArchiveController.cs`,
  `ShipmentLabelsController.cs`, `CarrierCoolingController.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/`, `.../Shipments/`
- `frontend/src/pages/ExpeditionListArchivePage.tsx`, `frontend/src/pages/customer/ExpeditionSettingsPage.tsx`
- `frontend/src/components/customer/cooling/`, `frontend/src/components/customer/expeditionSettings/`
- `frontend/src/api/hooks/useExpeditionList.ts`, `useExpeditionListArchive.ts`, `useCarrierCooling.ts`

**Depends on:** A30, C04 (OpenMeteo for cooling), B08 (PDF).

---

## A12 — Gift Packages

**Purpose:** assembly and disassembly of gift package products, plus the gift settings that drive it.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/`
- `.../Logistics/UseCases/GiftSettings/`
- `backend/src/Anela.Heblo.Domain/Features/Logistics/GiftPackageManufacture/`, `.../GiftSettings/`
- `backend/src/Anela.Heblo.Persistence/Logistics/GiftPackageManufacture/`, `.../GiftSettings/`
- `backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs`
- `frontend/src/components/pages/GiftPackageManufacturing/`
- `frontend/src/api/hooks/useGiftPackageManufacturing.ts`, `useGiftSetting.ts`

**Depends on:** A01, A15.

---

## A13 — Manufacture Orders & Calendar

**Purpose:** the manufacture order aggregate — create, duplicate, update, schedule, status transitions, calendar view,
production protocol.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CreateManufactureOrder/`,
  `UpdateManufactureOrder/`, `UpdateManufactureOrderSchedule/`, `UpdateManufactureOrderStatus/`,
  `DuplicateManufactureOrder/`, `GetManufactureOrder/`, `GetManufactureOrders/`, `GetCalendarView/`,
  `GetManufactureProtocol/`
- `.../Manufacture/Contracts/`, `.../Manufacture/Validators/`, `.../Manufacture/ErrorFilters/`,
  `.../Manufacture/Configuration/`, `.../Manufacture/Infrastructure/`
- `backend/src/Anela.Heblo.Domain/Features/Manufacture/`
- `backend/src/Anela.Heblo.Persistence/Manufacture/`
- `backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs`
- `frontend/src/components/manufacture/` (`list/`, `detail/`, `calendar/`, `pages/`, `shared/`)
- `frontend/src/api/hooks/useManufactureOrders.ts`
- `frontend/test/e2e/manufacturing/`

**Depends on:** A01, A14.

---

## A14 — Batch Planning & Batch Calculator

**Purpose:** batch size maths — plan a batch from demand, calculate by ingredient, distribute residues, adjust BoM
ingredient amounts.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculateBatchPlan/`,
  `CalculateBatchByIngredient/`, `CalculatedBatchSize/`, `UpdateBoMIngredientAmount/`
- `.../Manufacture/Services/BatchPlanningService.cs`, `BatchDistributionCalculator.cs`,
  `ResidueDistributionCalculator.cs`, `ConsumptionRateCalculator.cs`, `ProductBatch.cs`, `ProductVariant.cs`
  (+ their interfaces)
- `backend/src/Anela.Heblo.API/Controllers/ManufactureBatchController.cs`
- `frontend/src/components/pages/ManufactureBatchPlanning.tsx`, `ManufactureBatchCalculator.tsx`
- `frontend/src/api/hooks/useBatchPlanning.ts`, `useManufactureBatch.ts`

**Depends on:** A01, C01 (BoM from Flexi).

**Analysis notes:** the densest pure-computation part in the codebase — highest value target for property/edge-case
analysis.

---

## A15 — Manufacture Execution & Output

**Purpose:** recording what was actually produced — semi-product and final-product confirmation, manual action
resolution, output listing, recipe PDF.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/`,
  `ConfirmSemiProductManufacture/`, `ConfirmProductCompletion/`, `ResolveManualAction/`,
  `GetManufactureOutput/`, `GetSemiproductRecipePdf/`
- `.../Manufacture/Services/Workflows/`, `ManufactureConditionsCaptureService.cs`,
  `ProductionActivityAnalyzer.cs`, `SubmitManufactureRequestItem.cs`
- `backend/src/Anela.Heblo.API/Controllers/ManufactureOutputController.cs`
- `frontend/src/components/pages/ManufactureOutput.tsx`, `ManufactureOutputModal.tsx`
- `frontend/src/api/hooks/useManufactureOutput.ts`, `useSemiproductRecipePdf.ts`, `useLastManufacturedItems.ts`

**Depends on:** A13, C01, B08.

---

## A16 — Manufacturing Stock Analysis

**Purpose:** "what do we need to manufacture" — severity scoring, consumption rates, filtering of manufacturable items.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetStockAnalysis/`
- `.../Manufacture/Services/ManufactureSeverityCalculator.cs`, `ItemFilterService.cs`,
  `ManufactureAnalysisMapper.cs`, `ProductNameFormatter.cs` (+ interfaces)
- `.../Manufacture/DashboardTiles/`
- `backend/src/Anela.Heblo.API/Controllers/ManufacturingStockAnalysisController.cs`
- `frontend/src/components/pages/ManufacturingStockAnalysis.tsx`
- `frontend/src/api/hooks/useManufacturingStockAnalysis.ts`

**Depends on:** A01, A14, A32.

---

## A17 — Manufacture Inventory, Lots & Settings

**Purpose:** manufacture-side inventory (semi-products, manufactured products), manufacture stock taking, material
containers/lots, difficulty settings and manufacture configuration.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufacturedInventory/`,
  `CreateManufacturedInventoryItem/`, `UpdateManufacturedInventoryItem/`, `DeleteManufacturedInventoryItem/`,
  `SubmitManufactureStockTaking/`, `GetManufactureStockTakingHistory/`, `GetManufactureSettings/`
- `.../Manufacture/Services/ManufactureInventoryWriteDownService.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/CreateManufactureDifficulty/`,
  `UpdateManufactureDifficulty/`, `DeleteManufactureDifficulty/`, `GetManufactureDifficultySettings/`
- `backend/src/Anela.Heblo.Persistence/Manufacture/Inventory/`,
  `backend/src/Anela.Heblo.Persistence/Catalog/ManufactureDifficulty/`
- `backend/src/Anela.Heblo.API/Controllers/ManufacturedProductInventoryController.cs`,
  `ManufactureStockTakingController.cs`, `ManufactureSettingsController.cs`, `MaterialContainersController.cs`,
  `LotsController.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Lots/`
- `frontend/src/components/pages/ManufactureInventoryList.tsx`, `ManufacturedInventoryPage.tsx`,
  `MaterialContainerList.tsx`, `LotLabelPrintModal.tsx`
- `frontend/src/components/ManufactureDifficultyModal.tsx`
- `frontend/src/api/hooks/useManufactureInventory.ts`, `useManufacturedProductInventory.ts`,
  `useManufactureStockTaking.ts`, `useManufactureSettings.ts`, `useManufactureDifficulty.ts`,
  `useMaterialContainers.ts`, `useMaterials.ts`

**Depends on:** A01, A05, C01.

---

## A18 — Purchase Orders

**Purpose:** purchase order lifecycle, lines, supplier assignment, purchase price recalculation.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/`, `UpdatePurchaseOrder/`,
  `UpdatePurchaseOrderStatus/`, `UpdatePurchaseOrderInvoiceAcquired/`, `GetPurchaseOrders/`,
  `GetPurchaseOrderById/`, `GetPurchaseOrderHistory/`, `RecalculatePurchasePrice/`
- `.../Purchase/Contracts/`, `.../Purchase/Services/`, `.../Purchase/Infrastructure/`, `.../Purchase/DashboardTiles/`
- `backend/src/Anela.Heblo.Domain/Features/Purchase/`
- `backend/src/Anela.Heblo.Persistence/Purchase/`
- `backend/src/Anela.Heblo.API/Controllers/PurchaseOrdersController.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Purchase/`
- `frontend/src/components/pages/PurchaseOrderList.tsx`, `PurchaseOrderDetail.tsx`, `PurchaseOrderForm.tsx`
- `frontend/src/components/purchase-orders/`
- `frontend/src/api/hooks/usePurchaseOrders.ts`, `useRecalculatePurchasePrice.ts`

**Depends on:** A01, C01.

---

## A19 — Purchase Stock Analysis & Suppliers

**Purpose:** "what do we need to buy" analysis, material-for-purchase queries, supplier master data.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetMaterialForPurchase/`
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/`, `SearchSuppliers/`
- `backend/src/Anela.Heblo.API/Controllers/PurchaseStockAnalysisController.cs`, `SuppliersController.cs`
- `frontend/src/components/pages/PurchaseStockAnalysis.tsx`
- `frontend/src/api/hooks/usePurchaseStockAnalysis.ts`, `useSuppliers.ts`

**Depends on:** A01, A18.

---

## A20 — Issued Invoices & Invoice Import

**Purpose:** issued-invoice list/detail, the async import pipeline from the e-shop into the ERP, and sync statistics.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/Invoices/`
- `backend/src/Anela.Heblo.Domain/Features/Invoices/`
- `backend/src/Anela.Heblo.Persistence/Invoices/`
- `backend/src/Anela.Heblo.API/Controllers/InvoicesController.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/IssuedInvoices/`
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Invoices/`
- `frontend/src/pages/customer/IssuedInvoicesPage.tsx`
- `frontend/src/components/invoices/`, `frontend/src/components/customer/IssuedInvoiceDetailModal.tsx`
- `frontend/src/api/hooks/useIssuedInvoices.ts`, `useIssuedInvoiceSyncStats.ts`, `useAsyncInvoiceImport.ts`
- `frontend/test/e2e/issued-invoices/`

**Depends on:** A30, C01, B04.

---

## A21 — Invoice Classification

**Purpose:** rule-based classification of received invoices into accounting categories, with history and stats.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/`
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/`
- `backend/src/Anela.Heblo.Persistence/InvoiceClassification/`
- `backend/src/Anela.Heblo.API/Controllers/InvoiceClassificationController.cs`
- `frontend/src/pages/InvoiceClassification/`
- `frontend/src/api/hooks/useInvoiceClassification.ts`

**Depends on:** C01, A22.

---

## A22 — Financial Overview & Bank Statements

**Purpose:** the finance dashboard (income/expense/stock value over time, comparisons) and bank statement import.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/FinancialOverview/`
- `backend/src/Anela.Heblo.Application/Features/Bank/`
- `backend/src/Anela.Heblo.Domain/Features/FinancialOverview/`, `.../Features/Bank/`, `.../Features/CashRegister/`
- `backend/src/Anela.Heblo.Domain/Accounting/`
- `backend/src/Anela.Heblo.Persistence/Features/Bank/`
- `backend/src/Anela.Heblo.API/Controllers/FinancialOverviewController.cs`, `BankStatementsController.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Bank/`, `.../Flexi/Accounting/`
- `backend/src/Adapters/Anela.Heblo.Adapters.Comgate/`
- `frontend/src/components/pages/FinancialOverview.tsx`, `frontend/src/components/pages/financial-overview/`
- `frontend/src/pages/customer/BankStatementImportPage.tsx`, `frontend/src/pages/customer/BankStatementsOverviewPage.tsx`
- `frontend/src/components/customer/tabs/` (`StatisticsTab.tsx`, `ImportTab.tsx`)
- `frontend/src/api/hooks/useFinancialOverview.ts`, `useFinancialComparison.ts`, `useBankStatements.ts`
- `frontend/test/e2e/finance/`

**Depends on:** C01, A01 (stock value), A04.

---

## A23 — Marketing Calendar & Marketing Invoices

**Purpose:** the marketing action calendar (with Outlook import) and marketing spend invoice import.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/Marketing/`
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/`
- `backend/src/Anela.Heblo.Domain/Features/Marketing/`, `.../Features/MarketingInvoices/`
- `backend/src/Anela.Heblo.Persistence/Marketing/`, `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/`
- `backend/src/Anela.Heblo.API/Controllers/MarketingCalendarController.cs`
- `frontend/src/components/marketing/calendar/`, `detail/`, `list/`, `pages/`
- `frontend/src/pages/MarketingFeedbackPage.tsx`
- `frontend/src/api/hooks/useMarketingCalendar.ts`
- `frontend/test/e2e/marketing/`

**Depends on:** C02 (Outlook via Graph), A20.

---

## A24 — Photobank

**Purpose:** product photo library — sync from SharePoint/OneDrive, auto-tagging, search, settings.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/Photobank/`
- `backend/src/Anela.Heblo.Domain/Features/Photobank/`
- `backend/src/Anela.Heblo.Persistence/Photobank/`
- `backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Photobank/`
- `frontend/src/components/marketing/photobank/`
- `frontend/src/api/hooks/usePhotobank.ts`, `usePhotobankSettings.ts`

**Depends on:** C02, C03 (auto-tagging), A01.

---

## A25 — Leaflet Generator

**Purpose:** generation of product leaflets/labels as printable documents.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/Leaflet/`
- `backend/src/Anela.Heblo.Domain/Features/Leaflet/`
- `backend/src/Anela.Heblo.Persistence/Features/Leaflet/`
- `backend/src/Anela.Heblo.API/Controllers/LeafletController.cs`
- `frontend/src/features/leaflet-generator/`
- `frontend/src/api/hooks/useLeaflet.ts`
- `frontend/test/e2e/leaflet-generator/`

**Depends on:** A01, B08.

---

## A26 — AI Articles

**Purpose:** AI-assisted article/content generation pipeline with tracing and admin surface.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/Article/`
- `backend/src/Anela.Heblo.Domain/Features/Article/`
- `backend/src/Anela.Heblo.Persistence/Features/Article/`
- `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs`
- `frontend/src/features/articles/`, `frontend/src/pages/ArticlesPage.tsx`
- `frontend/src/api/hooks/useArticles.ts`, `useArticleTrace.ts`

**Depends on:** C03, A27.

---

## A27 — Knowledge Base (RAG)

**Purpose:** the retrieval-augmented knowledge base — ingestion pipeline, embeddings, retrieval, feedback loop.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/`
- `backend/src/Anela.Heblo.Application/Shared/Rag/`
- `backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/`, `.../Features/Rag/`, `.../Shared/Rag/`
- `backend/src/Anela.Heblo.Persistence/KnowledgeBase/`, `backend/src/Anela.Heblo.Persistence/Rag/`
- `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs`
- `frontend/src/components/knowledge-base/`, `frontend/src/pages/KnowledgeBasePage.tsx`,
  `frontend/src/pages/KnowledgeBaseFeedbackPage.tsx`
- `frontend/src/api/hooks/useKnowledgeBase.ts`

**Depends on:** C03, B05 (pgvector storage).

---

## A28 — Meeting Tasks (Plaud)

**Purpose:** meeting recordings → transcript → extracted tasks, with review workflow and per-task access control.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/`
- `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/`
- `backend/src/Anela.Heblo.Persistence/MeetingTasks/`
- `backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/`
- `frontend/src/components/pages/automation/` (`MeetingTasksPage.tsx`, `MeetingTaskDetailPage.tsx`,
  `MeetingReviewLeaveDialog.tsx`, `access/`, `explain/`)
- `frontend/src/api/hooks/useMeetingTasks.ts`

**Depends on:** C03, B01.

---

## A29 — Customer Support (Smartsupp)

**Purpose:** live-chat integration — webhook ingestion, conversation storage, agent presence, AI-assisted replies,
webhook audit.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/`
- `backend/src/Anela.Heblo.Domain/Features/Smartsupp/`
- `backend/src/Anela.Heblo.Persistence/Smartsupp/`
- `backend/src/Anela.Heblo.API/Controllers/SmartsuppController.cs`, `SmartsuppWebhookController.cs`,
  `SmartsuppWebhookAuditController.cs`
- `backend/src/Anela.Heblo.API/Webhooks/Smartsupp/`
- `backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/`
- `backend/tools/SmartsuppWebhookReplay/`
- `frontend/src/components/customer-support/`
- `frontend/src/api/hooks/useSmartsupp.ts`

**Depends on:** C03, A30.

---

## A30 — E-shop Orders & Customers (Shoptet)

**Purpose:** everything that reads or writes the Shoptet e-shop — orders, customers, stock sync, payments, plus the
application-level order feature on top.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/` (`Orders/`, `Customers/`, `Stock/`, `ShoptetPay/`,
  `EshopUrl/`) — *`Expedition/` and `Shipments/` belong to A11, `IssuedInvoices/` to A20*
- `backend/src/Adapters/Anela.Heblo.Adapters.Shoptet/` (feed/price import)
- `backend/src/Anela.Heblo.API/Controllers/ShoptetOrdersController.cs`
- `docs/integrations/shoptet-api.md`

**Depends on:** —
**Consumed by:** A01, A06, A09, A11, A20, A29.

**Analysis notes:** no sandbox exists — every call hits the live store. Findings must be written to
`docs/integrations/shoptet-api.md` before being relied on.

---

## A31 — Journal

**Purpose:** free-form operational journal entries linked to products/dates.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/Journal/`
- `backend/src/Anela.Heblo.Domain/Features/Journal/`
- `backend/src/Anela.Heblo.Persistence/Journal/`
- `backend/src/Anela.Heblo.API/Controllers/JournalController.cs`
- `frontend/src/components/pages/Journal/`, `JournalEntryEdit.tsx`, `JournalEntryNew.tsx`
- `frontend/src/components/JournalEntryForm.tsx`, `JournalEntryModal.tsx`
- `frontend/src/api/hooks/useJournal.ts`

**Depends on:** A01, B02.

---

## A32 — Dashboard & Tiles

**Purpose:** the tile registry/framework and the home dashboard that composes tiles contributed by other modules.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/Dashboard/`
- `backend/src/Anela.Heblo.Domain/Features/Dashboard/`
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/`
- `backend/src/Anela.Heblo.Persistence/Dashboard/`
- `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs`
- `frontend/src/components/dashboard/`, `frontend/src/components/pages/Dashboard.tsx`
- `frontend/src/api/hooks/useDashboard.ts`

**Contributed to by:** `Features/*/DashboardTiles/` in A01, A09, A13, A16 — analyse those as *inputs*, not as owned code.
**Depends on:** B03 (grid layouts), B01.

---

## A33 — Data Quality

**Purpose:** automated consistency checks across stock, lots, operations and stock-taking; the issues dashboard.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/DataQuality/`
- `backend/src/Anela.Heblo.Domain/Features/DataQuality/`
- `backend/src/Anela.Heblo.Persistence/DataQuality/`
- `backend/src/Anela.Heblo.API/Controllers/DataQualityController.cs`
- `backend/src/Anela.Heblo.API/HealthChecks/DataQuality/`
- `frontend/src/components/data-quality/`
- `frontend/src/api/hooks/useDataQuality.ts`

**Depends on:** A01, A05, A06, A17.

---

# B. Platform & cross-cutting parts

## B01 — Authorization & Access Management

**Purpose:** the permission model — roles, groups, menu-path guards, Entra group mapping, the access matrix and its
generator/seeder.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/Authorization/`
- `backend/src/Anela.Heblo.Domain/Features/Authorization/`
- `backend/src/Anela.Heblo.Persistence/Features/Authorization/`
- `backend/src/Anela.Heblo.API/Controllers/AuthorizationController.cs`
- `backend/src/Anela.Heblo.API/Infrastructure/Authentication/PermissionAuthorizationResultHandler.cs`,
  `PermissionClaimsTransformation.cs`
- `backend/tools/Anela.Heblo.AccessMatrixGen/`, `backend/tools/Anela.Heblo.AuthorizationSeeder/`
- `access-matrix.json`, `access-matrix.generated.json`, `access-matrix-entra.generated.json`
- `scripts/seed-authorization.sh`, `scripts/sync-entra-access.sh`, `scripts/cleanup-entra.sh`
- `frontend/src/components/access-management/`, `frontend/src/components/pages/access/`
- `frontend/src/pages/AccessManagementPage.tsx`
- `frontend/src/pages/GroupDetailPage.tsx`, `UserDetailPage.tsx`
- `frontend/src/api/hooks/useAccessManagement.ts`, `usePermissions.ts`

**Depends on:** B02, C02.
**Consumed by:** every routed page (`guard()` / `RequireMenuPath` in `frontend/src/App.tsx`).

---

## B02 — Users, Identity & Org Chart

**Purpose:** authentication (Entra ID / MSAL), current-user resolution, user management, departments and org chart.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/UserManagement/`
- `backend/src/Anela.Heblo.Application/Features/OrgChart/`
- `backend/src/Anela.Heblo.Application/Shared/Users/`
- `backend/src/Anela.Heblo.Domain/Features/Users/`
- `backend/src/Anela.Heblo.API/Features/Users/`
- `backend/src/Anela.Heblo.API/Controllers/AuthController.cs`, `UserManagementController.cs`,
  `OrgChartController.cs`, `DepartmentsController.cs`
- `backend/src/Anela.Heblo.API/Infrastructure/Authentication/` (mock handler, E2E session, SP token validator)
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/`
- `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/`
- `frontend/src/auth/`, `frontend/src/components/auth/`
- `frontend/src/pages/OrgChartPage.tsx`, `orgChartUtils.ts`, `frontend/src/components/OrgChart/`
- `frontend/src/api/hooks/useUserManagement.ts`, `useDepartments.ts`, `useOrgChart.ts`

**Depends on:** C02.

---

## B03 — Feature Flags, Configuration & Grid Layouts

**Purpose:** runtime toggles, app configuration exposure, and persisted per-user grid/table layouts. Also holds the
`WeatherForecast` sample endpoint.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/`, `.../Features/Configuration/`,
  `.../Features/GridLayouts/`, `.../Features/WeatherForecast/`
- `backend/src/Anela.Heblo.Domain/Features/FeatureFlags/`, `.../Features/Configuration/`, `.../Features/GridLayouts/`
- `backend/src/Anela.Heblo.Persistence/FeatureFlags/`, `backend/src/Anela.Heblo.Persistence/GridLayouts/`
- `backend/src/Anela.Heblo.API/Controllers/FeatureFlagsController.cs`, `ConfigurationController.cs`,
  `GridLayoutsController.cs`, `WeatherForecastController.cs`
- `frontend/src/features/feature-flags/`, `frontend/src/features/grid-layout/`
- `frontend/src/pages/FeatureFlagsAdminPage.tsx`
- `frontend/src/config/`, `frontend/src/constants/`
- `frontend/src/api/hooks/useConfiguration.ts`, `useFeatureFlagsAdmin.ts`
- `docs/development/feature-flags.md`

---

## B04 — Background Execution (Hangfire + Refresh/Hydration)

**Purpose:** all scheduled and deferred work — Hangfire recurring jobs and dashboard, plus the tiered background
refresh/hydration orchestrator that warms caches at startup.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/`, `.../Features/BackgroundRefresh/`
- `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/`
- `backend/src/Anela.Heblo.Persistence/BackgroundJobs/`
- `backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/`, `.../Services/BackgroundJobInfo.cs`,
  `.../Services/IBackgroundWorker.cs`, `.../Services/Concurrency/`
- `backend/src/Anela.Heblo.Xcc/HangfireOptions.cs`
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/`
- `backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs`, `BackgroundRefreshController.cs`
- `frontend/src/pages/RecurringJobsPage.tsx`
- `frontend/src/components/pages/automation/BackgroundTasks.tsx`, `frontend/src/components/BackgroundTasksCard.tsx`,
  `backgroundTasksHelpers.tsx`, `TaskHistoryModal.tsx`
- `frontend/src/api/hooks/useRecurringJobs.ts`, `useBackgroundRefresh.ts`

**Registered by:** `Features/*/Infrastructure/Jobs/` in A01, A09, A20.

---

## B05 — Persistence Core & Migrations

**Purpose:** the main `DbContext`, EF conventions, resilience policies, repository base classes and the migration
history.

**Owns:**
- `backend/src/Anela.Heblo.Persistence/` root files, `Extensions/`, `Infrastructure/`, `Repositories/`, `Migrations/`
- `backend/src/Anela.Heblo.Xcc/Persistance/`, `backend/src/Anela.Heblo.Xcc/Domain/`
- `scripts/check-no-managed-tx.sh`, `scripts/migration-dryrun.sh`

**Analysis notes:** `Migrations/` is ~367k generated LOC — exclude it from any content analysis; analyse the
migration *sequence* and the manual-migration process instead (migrations are **not** automated in deployment).

---

## B06 — API Host & Composition Root

**Purpose:** `Program.cs`, DI wiring, middleware pipeline, MediatR behaviours, exception handling, model binders,
CORS/environment configuration.

**Owns:**
- `backend/src/Anela.Heblo.API/Program.cs`
- `backend/src/Anela.Heblo.API/Extensions/`
- `backend/src/Anela.Heblo.API/Middleware/`
- `backend/src/Anela.Heblo.API/Infrastructure/` (root files, `ExceptionHandling/`)
- `backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs`, `E2ETestController.cs`
- `backend/src/Anela.Heblo.Application/Common/Behaviors/`, `.../Common/Extensions/`
- `backend/src/Anela.Heblo.Application/Shared/Http/`, `.../Shared/Json/`
- `backend/src/Anela.Heblo.Xcc/` root files (`Check.cs`, `*Extensions.cs`, `XccModule.cs`), `Abo/`
- `docs/architecture/environments.md`

**Analysis notes:** every `*Module.cs` in `Application/Features/` registers here — the single place where module
boundaries are actually enforced or violated.

---

## B07 — Telemetry, Health & Diagnostics

**Purpose:** Application Insights wiring, sampling/cost filters, health checks, diagnostics endpoints, frontend
telemetry.

**Owns:**
- `backend/src/Anela.Heblo.API/Infrastructure/Telemetry/`
- `backend/src/Anela.Heblo.API/HealthChecks/` (except `DataQuality/` → A33)
- `backend/src/Anela.Heblo.API/Controllers/DiagnosticsController.cs`
- `backend/src/Anela.Heblo.Xcc/Telemetry/`
- `frontend/src/telemetry/`
- `frontend/src/api/hooks/useHealth.ts`
- `scripts/monitoring/`, `docs/routines/telemetry-anomaly/`

---

## B08 — Documents, File Storage & Printing

**Purpose:** blob/file storage, catalog document management, PDF generation and physical label printing.

**Owns:**
- `backend/src/Anela.Heblo.Application/Features/FileStorage/`, `.../Features/CatalogDocuments/`
- `backend/src/Anela.Heblo.Application/Shared/Printing/`
- `backend/src/Anela.Heblo.Domain/Features/FileStorage/`
- `backend/src/Anela.Heblo.API/PDFPrints/`
- `backend/src/Anela.Heblo.API/Controllers/FileStorageController.cs`, `CatalogDocumentsController.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Cups/`
- `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/`
- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/` (blob storage)
- `frontend/src/api/hooks/useCatalogDocuments.ts`

**Consumed by:** A11, A15, A17, A25.

---

## B09 — API Contract & Client Generation

**Purpose:** the OpenAPI contract and the two generated clients (C# and TypeScript), plus the DTO rules that keep
generation stable.

**Owns:**
- `backend/src/Anela.Heblo.API.Client/`
- `backend/src/Anela.Heblo.API/nswag-templates/`
- `frontend/src/api/generated/`, `frontend/src/services/generated/`
- `scripts/regenerate-api-client.sh`
- `docs/development/api-client-generation.md`

**Analysis notes:** generated code — analyse the *generation pipeline and its constraints* (DTOs must be classes,
never records; hooks must use absolute URLs), not the output.

---

## B10 — MCP Server

**Purpose:** the Model Context Protocol tool surface exposed by the API (20 tools) for AI clients.

**Owns:**
- `backend/src/Anela.Heblo.API/MCP/`
- `docs/integrations/mcp-server.md`

**Depends on:** most A-parts (tools are thin wrappers over their handlers).

---

# C. Integration adapters

## C01 — FlexiBee ERP Adapter

**Purpose:** the single largest external integration — products, stock, purchase, sales, invoices, accounting,
manufacture, lots, price lists.

**Owns:** `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/`
*(sub-namespaces already claimed elsewhere: `Lots/` → A17, `Purchase/` → A18, `Invoices/` → A20,
`Bank/` + `Accounting/` → A22. Analyse those here only as adapter mechanics.)*

**Also owns:** `backend/test/Anela.Heblo.Adapters.Flexi.Tests/`

---

## C02 — Microsoft 365 & Azure Adapters

**Purpose:** Graph access (users, groups, Outlook calendar, SharePoint/OneDrive files) and Azure services
(Key Vault, blob storage).

**Owns:**
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/`
- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/`
- `backend/src/Anela.Heblo.Application/Common/Graph/`

**Analysis notes:** Key Vault is the mandated secret store (`kv-heblo-stg` for staging, `--` as name separator);
App Settings must not hold secrets.

---

## C03 — AI / LLM & Web Search Adapters

**Purpose:** LLM providers and web search used by Articles, Knowledge Base, Photobank tagging and Smartsupp.

**Owns:**
- `backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/`
- `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/`
- `backend/src/Adapters/Anela.Heblo.Adapters.WebSearch/`
- `backend/src/Anela.Heblo.Application/Shared/WebSearch/`

---

## C04 — Ancillary External Adapters

**Purpose:** the long tail of small integrations, each < 700 LOC, grouped because none justifies its own pass.

**Owns:**
- `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/` (facility telemetry; has its own caching, resilience,
  health-check and telemetry sub-namespaces)
- `backend/src/Adapters/Anela.Heblo.Adapters.OpenMeteo/` (weather → carrier cooling)
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/`, `.../Adapters.MetaAds/` (ad spend)
- `backend/src/Adapters/Anela.Heblo.Adapters.SendGrid/` (mail), `backend/src/Anela.Heblo.Xcc/Services/Email/`
- `backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/`, `backend/test/Anela.Heblo.Adapters.OpenMeteo.Tests/`

**Analysis notes:** all of these are registered directly in `Program.cs` with no intermediate module — check whether
any has a consumer at all before analysing deeply.

---

# D. Delivery & tooling

## D01 — Frontend Shell, Layout & Navigation

**Purpose:** the app skeleton — routing table, menu, layout chrome, auth gating, global state and loading.

**Owns:**
- `frontend/src/App.tsx`, `frontend/src/index.tsx`, `frontend/src/index.css`, `frontend/src/i18n.ts`
- `frontend/src/components/Layout/`
- `frontend/src/components/AppInitializer.tsx`, `StatusBar.tsx`, `GlobalLoadingIndicator.tsx`
- `frontend/src/contexts/`
- `frontend/src/features/changelog/`
- `docs/design/layout_definition.md`

---

## D02 — Frontend Shared UI, Hooks & Utilities

**Purpose:** the design-system layer and shared client plumbing that every page depends on.

**Owns:**
- `frontend/src/components/ui/`, `common/`, `modals/`, `dialogs/`, `charts/`, `feedback/`, `test/`
- `frontend/src/hooks/`, `frontend/src/utils/`, `frontend/src/services/` (non-generated),
  `frontend/src/types/`, `frontend/src/test-utils/`
- `frontend/src/api/` root files (client factory, base URL handling) — *excluding* `generated/` (B09) and
  per-feature `hooks/*` (owned by their domain part)
- `docs/design/ui_design_document.md`

**Analysis notes:** the absolute-URL rule (`${apiClient.baseUrl}${relativeUrl}`) lives here and is a recurring
source of bugs.

---

## D03 — Automated Test Suites & Test Infrastructure

**Purpose:** all test harnesses, fixtures, helpers and the architecture-conformance tests.

**Owns:**
- `backend/test/Anela.Heblo.Tests/` — in particular `Architecture/`, `Helpers/`, `Common/`
- `backend/test/Anela.Heblo.Adapters.*.Tests/` root infrastructure
- `frontend/test/e2e/fixtures/`, `frontend/test/e2e/helpers/`, `frontend/test/auth/`, `frontend/test/utils/`,
  `frontend/test/api/`
- `frontend/src/test/`
- `scripts/run-playwright-tests.sh`, `scripts/seed-manufacture-orders-for-e2e.sh`
- `reportportal/`
- `docs/architecture/testing-strategy.md`, `docs/testing/`

**Analysis notes:** per-module E2E specs (`frontend/test/e2e/<module>/`) are listed under their domain part;
this part owns the *shared* machinery. E2E runs nightly, not in PR CI.

---

## D04 — CI/CD, Docker & Deployment

**Purpose:** how the single Docker image is built, versioned and deployed to Azure Web App for Containers.

**Owns:**
- `.github/workflows/`, `.github/`
- `Dockerfile`, `docker-compose.yml`, `GitVersion.yml`, `.codecov.yml`
- `scripts/` (deployment, Azure, secrets, start-* scripts) — excluding those claimed by B01/B05/D03
- `docs/architecture/infrastructure.md`, `docs/development/setup.md`, `docs/operations/`
- `.husky/`

---

## D05 — Documentation & Agent Tooling

**Purpose:** the written knowledge base and the AI-agent scaffolding around the repo.

**Owns:**
- `docs/` (everything not claimed above — `analysis/`, `features/`, `handoff/`, `implementation/`,
  `investigations/`, `plans/`, `superpowers/`, `tasks/`, `routines/`)
- `CLAUDE.md`, `memory/`
- `.claude/`, `.agents/`, `.conductor/`, `.context/`, `.pipeline/`, `.ralphrc`, `.ralph-tui/`
- `artifacts/`, `.artifacts/` (pipeline run outputs)

**Analysis notes:** `artifacts/` holds ~423 per-feature pipeline directories — treat as data, not source.

---

## Coverage & known gaps

**Deliberately unassigned (analyse only if a part points at them):**
- `backend/src/Anela.Heblo.Persistence/Migrations/` — generated, ~367k LOC (noted in B05)
- `frontend/src/api/generated/`, `backend/src/Anela.Heblo.API.Client/Generated/` — generated (noted in B09)
- Root-level scratch files: `answers.md`, `brief.md`, `design.md`, `convert_asserts*.sh`, `test_dto.exe`,
  `*.pdf`, `*.jpeg`, `e2e_ralph_prompt.txt` — leftovers, not part of the application
- `.idea/`, `.playwright-mcp/`, `.config/`, `.claire/`

**Overlaps to watch when iterating:**
- `Application/Features/Catalog` is split across **A01–A06** and **A17/A19** — read the folder listing, not the
  folder name, when deciding what belongs to the part you're analysing.
- `Application/Features/Manufacture` is split across **A13–A17**.
- `Application/Features/Logistics` is split across **A07** (transport) and **A12** (gift packages).
- `Adapters.ShoptetApi` is split across **A11**, **A20** and **A30**.
- `Adapters.Flexi` is split across **C01** (mechanics) and **A17/A18/A20/A22** (domain mapping).
- `Features/*/DashboardTiles/` folders stay with their feature; **A32** owns only the tile framework.

**Suggested iteration order** (dependency-light first, so later parts can lean on earlier findings):
B06 → B05 → B01 → A01 → A30 → C01 → then the A-parts in numeric order → remaining B/C → D.
