# UI Screen Usage Coverage

Canonical checklist of every user-facing screen and in-page branch in the app, and whether `useScreenView(...)` is wired up.

## Contract

- "Covered" = `useScreenView(module, screen, subScreen?)` is called for the surface AND its checkbox is ticked below.
- Every PR that adds, removes, or changes a screen/branch updates this doc in the same PR. Reviewers reject PRs that don't.
- KQL verification (see [usage-analytics.md](./usage-analytics.md#how-to-query)) is run weekly; gaps between this doc and observed `ScreenViewed` events are filed as bugs.
- Modals, drawers, and feature actions (button clicks, filter applies, exports) are **out of scope** for `ScreenViewed`. Modal-as-full-screen views (e.g., `CatalogDetail` opened from `CatalogList`) ARE in scope.
- "Coming Soon" placeholder screens are listed but not checked until content lands.

## Legend

`[x]` = wired and reaching production. `[ ]` = not yet wired.

## Dashboard

| Screen | Route | Component | Branches | Coverage |
|---|---|---|---|---|
| Dashboard | `/` | `components/pages/Dashboard.tsx` | — | `[x] base` |

## Finance

| Screen | Route | Component | Branches | Coverage |
|---|---|---|---|---|
| FinancialOverview | `/finance/overview` | `components/pages/FinancialOverview.tsx` | — | `[x] base` |
| BankStatementImport | `/finance/bank-statements` | `components/pages/BankStatementImportChart.tsx` | — | `[x] base` |
| ProductMarginSummary | `/analytics/product-margin-summary` | `components/pages/ProductMarginSummary.tsx` | — | `[x] base` |

## Catalog

| Screen | Route | Component | Branches | Coverage |
|---|---|---|---|---|
| CatalogList | `/catalog` | `components/pages/CatalogList.tsx` | — | `[x] base` |
| CatalogDetail | (modal from list) | `components/pages/CatalogDetail.tsx` | tabs: Basic, History, Margins, Composition, Journal, Usage, Documents, Pif; chartTabs: Input, Output | `[x] base` `[x] BasicTab` `[x] HistoryTab` `[x] MarginsTab` `[x] CompositionTab` `[x] JournalTab` `[x] UsageTab` `[x] DocumentsTab` `[x] PifTab` `[x] ChartInput` `[x] ChartOutput` |
| ProductMargins | `/products/margins` | `components/pages/ProductMarginsList.tsx` | — | `[x] base` |

## Journal

| Screen | Route | Component | Branches | Coverage |
|---|---|---|---|---|
| JournalList | `/journal` | `components/pages/Journal/JournalList.tsx` | — | `[x] base` |
| JournalEntryNew | `/journal/new` | `components/pages/JournalEntryNew.tsx` | — | `[x] base` |
| JournalEntryEdit | `/journal/:id/edit` | `components/pages/JournalEntryEdit.tsx` | — | `[x] base` |

## Purchase

| Screen | Route | Component | Branches | Coverage |
|---|---|---|---|---|
| PurchaseOrderList | `/purchase/orders` | `components/pages/PurchaseOrderList.tsx` | — | `[x] base` |
| PurchaseStockAnalysis | `/purchase/stock-analysis` | `components/pages/PurchaseStockAnalysis.tsx` | — | `[x] base` |
| InvoiceClassification | `/purchase/invoice-classification` | `pages/InvoiceClassification/InvoiceClassificationPage.tsx` | tabs: Invoices, Rules | `[x] base` `[x] InvoicesTab` `[x] RulesTab` |

## Manufacturing

| Screen | Route | Component | Branches | Coverage |
|---|---|---|---|---|
| ManufacturingStockAnalysis | `/manufacturing/stock-analysis` | `components/pages/ManufacturingStockAnalysis.tsx` | — | `[ ] base` |
| ManufactureOutput | `/manufacturing/output` | `components/pages/ManufactureOutput.tsx` | — | `[ ] base` |
| ManufactureBatchCalculator | `/manufacturing/batch-calculator` | `components/pages/ManufactureBatchCalculator.tsx` | — | `[ ] base` |
| ManufactureBatchPlanning | `/manufacturing/batch-planning` | `components/pages/ManufactureBatchPlanning.tsx` | — | `[ ] base` |
| ManufactureOrderList | `/manufacturing/orders` | `components/manufacture/pages/ManufactureOrderList.tsx` | viewModes: Grid, Calendar, Weekly | `[ ] base` `[ ] GridView` `[ ] CalendarView` `[ ] WeeklyView` |
| ManufactureOrderDetail | `/manufacturing/orders/:id` | `components/manufacture/pages/ManufactureOrderDetail.tsx` | tabs: Info, Notes, Log, Conditions | `[ ] base` `[ ] InfoTab` `[ ] NotesTab` `[ ] LogTab` `[ ] ConditionsTab` |
| ManufactureInventory | `/manufacturing/inventory` | `components/pages/ManufactureInventoryList.tsx` | — | `[ ] base` |
| ManufacturedProductInventory | `/manufacturing/product-inventory` | `components/pages/ManufacturedInventoryPage.tsx` | — | `[ ] base` |

## Logistics

| Screen | Route | Component | Branches | Coverage |
|---|---|---|---|---|
| InventoryFinishedGoods | `/logistics/inventory` | `components/pages/InventoryList.tsx` | — | `[ ] base` |
| TransportBoxes | `/logistics/transport-boxes` | `components/pages/TransportBoxList.tsx` | — | `[ ] base` |
| TransportBoxReceive | `/logistics/receive-boxes` | `components/pages/TransportBoxReceive.tsx` | — | `[ ] base` |
| GiftPackageManufacturing | `/logistics/gift-package-manufacturing` | `components/pages/GiftPackageManufacturing.tsx` | — | `[ ] base` |
| PackingMaterials | `/logistics/packing-materials` | `pages/PackingMaterialsPage.tsx` | — | `[ ] base` |
| WarehouseStatistics | `/logistics/warehouse-statistics` | `components/pages/WarehouseStatistics.tsx` | — | `[ ] base` |
| ExpeditionArchive | `/logistics/expedition-archive` | `pages/ExpeditionListArchivePage.tsx` | — | `[ ] base` |

## Marketing

| Screen | Route | Component | Branches | Coverage |
|---|---|---|---|---|
| MarketingCalendar | `/marketing/calendar` | `components/marketing/pages/MarketingCalendarPage.tsx` | viewModes: FiveWeeks, TwoWeeks, List | `[x] base` `[x] FiveWeeksView` `[x] TwoWeeksView` `[x] ListView` |
| Photobank | `/marketing/photobank` | `components/marketing/photobank/pages/PhotobankPage.tsx` | viewModes: Tiles, List | `[x] base` `[x] TilesView` `[x] ListView` |
| PhotobankSettings | `/marketing/photobank/settings` | `components/marketing/photobank/pages/PhotobankSettingsPage.tsx` | — | `[x] base` |
| LeafletGenerator | `/leaflet-generator` | `features/leaflet-generator/LeafletGeneratorPage.tsx` | — | `[x] base` |
| Articles | `/articles` | `pages/ArticlesPage.tsx` | tabs: New, List | `[x] base` `[x] NewTab` `[x] ListTab` |
| MarketingFeedback | `/marketing/feedback` | `pages/MarketingFeedbackPage.tsx` | — | `[x] base` |

## Customer

| Screen | Route | Component | Branches | Coverage |
|---|---|---|---|---|
| IssuedInvoices | `/customer/issued-invoices` | `pages/customer/IssuedInvoicesPage.tsx` | — | `[x] base` |
| BankStatementsOverview | `/customer/bank-statements-overview` | `pages/customer/BankStatementsOverviewPage.tsx` | — | `[x] base` |
| SmartsuppChats | `/customer/smartsupp` | `components/customer-support/smartsupp/pages/SmartsuppChatsPage.tsx` | — | `[x] base` |
| ExpeditionSettings | `/customer/expedition-settings` | `pages/customer/ExpeditionSettingsPage.tsx` | tabs: Cooling, Gifts | `[x] base` `[x] CoolingTab` `[x] GiftsTab` |

## Automation

| Screen | Route | Component | Branches | Coverage |
|---|---|---|---|---|
| BackgroundTasks | `/automation/background-tasks` | `components/pages/automation/BackgroundTasks.tsx` | — | `[ ] base` |
| InvoiceImportStatistics | `/automation/invoice-import-statistics` | `components/pages/automation/InvoiceImportStatistics.tsx` | — | `[ ] base` |
| MeetingTasks | `/automation/meeting-tasks` | `components/pages/automation/MeetingTasksPage.tsx` | — | `[ ] base` |
| MeetingTaskDetail | `/automation/meeting-tasks/:id` | `components/pages/automation/MeetingTaskDetailPage.tsx` | — | `[ ] base` |
| StockOperations | `/stock-up-operations` | `pages/StockOperationsPage.tsx` | — | `[ ] base` |
| RecurringJobs | `/recurring-jobs` | `pages/RecurringJobsPage.tsx` | — | `[ ] base` |
| DataQuality | `/automation/data-quality` | `pages/customer/DataQualityPage.tsx` | — | `[ ] base` |

## Knowledge

| Screen | Route | Component | Branches | Coverage |
|---|---|---|---|---|
| KnowledgeBase | `/knowledge-base` | `pages/KnowledgeBasePage.tsx` | tabs: Search, Documents, Upload | `[x] base` `[x] SearchTab` `[x] DocumentsTab` `[x] UploadTab` |
| KnowledgeBaseFeedback | `/knowledge-base/feedback` | `pages/KnowledgeBaseFeedbackPage.tsx` | — | `[x] base` |

## Admin

| Screen | Route | Component | Branches | Coverage |
|---|---|---|---|---|
| OrgChart | `/orgchart` | `pages/OrgChartPage.tsx` | — | `[x] base` |
| FeatureFlagsAdmin | `/admin/feature-flags` | `pages/FeatureFlagsAdminPage.tsx` | — | `[x] base` |

## Terminal

| Screen | Route | Component | Branches | Coverage |
|---|---|---|---|---|
| TerminalHome | `/terminal` | `components/terminal/TerminalHome.tsx` | — | `[ ] base` |
| TerminalBoxCheck | `/terminal/box-check` | `components/terminal/TransportBoxCheck.tsx` | — | `[ ] base` |
| TerminalBoxFill | `/terminal/box-fill` | `components/terminal/box-fill/BoxFillWorkflow.tsx` | steps: Scan, AddItems | `[ ] base` `[ ] ScanStep` `[ ] AddItemsStep` |
| TerminalReceive | `/terminal/receive` | `components/terminal/TransportBoxReceive.tsx` | — | `[ ] base` |
| TerminalStocktake | `/terminal/stocktake` | (placeholder) | — | `[ ] base` (deferred — placeholder) |
| TerminalLotIdentification | `/terminal/lot-identification` | (placeholder) | — | `[ ] base` (deferred — placeholder) |

## Baleni

| Screen | Route | Component | Branches | Coverage |
|---|---|---|---|---|
| BaleniHome | `/baleni` | `components/baleni/BaleniHome.tsx` | — | `[ ] base` |
| BaleniPacking | `/baleni/baleni` | `components/baleni/BaleniPacking.tsx` | — | `[ ] base` |
| BaleniShipments | `/baleni/zasilky` | `components/baleni/zasilky/ZasilkyPage.tsx` | — | `[ ] base` |
| BaleniStatistics | `/baleni/statistiky` | (placeholder) | — | `[ ] base` (deferred — placeholder) |
