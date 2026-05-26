# Full UI Screen Usage Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Instrument every user-facing screen and every meaningful in-page branch (tabs, view-modes, wizard steps) with a single uniform `ScreenViewed` event, plus a coverage matrix doc that makes gaps visible.

**Architecture:** Add a `useScreenView(module, screen, subScreen?)` hook on top of the existing `useTelemetry()`. The hook fires `trackEvent('ScreenViewed', {module, screen, subScreen})` on mount and whenever `subScreen` changes. Auto-tracked `pageView` continues for performance metrics; semantic usage analysis routes exclusively through `ScreenViewed` so KQL is uniform. A new coverage doc (`docs/features/usage-analytics-coverage.md`) is the canonical checklist.

**Tech Stack:** React 18 + TypeScript, `@microsoft/applicationinsights-web`, `@microsoft/applicationinsights-react-js`, Jest + React Testing Library, react-scripts.

**Scope decisions (locked in):**
1. Single generic event `ScreenViewed` with `{module, screen, subScreen?}` — not per-screen named events.
2. Branches tracked: tabs, view-mode switches, wizard steps. NOT modals/drawers, NOT feature actions (those remain per-action events).
3. Terminal and Baleni sub-apps included; they get dedicated `module` values.

---

## Conventions used throughout

- **`module`**: one of the literal strings from the `ScreenModule` union (defined in Task 0): `Dashboard | Finance | Catalog | Journal | Purchase | Manufacturing | Logistics | Marketing | Customer | Automation | Knowledge | Admin | Terminal | Baleni`.
- **`screen`**: PascalCase, matches the component name (e.g., `CatalogList`, `ManufactureOrderDetail`). One value per route.
- **`subScreen`**: optional. PascalCase for tab/view-mode/wizard-step identifiers (e.g., `MarginsTab`, `CalendarView`, `AddItemsStep`). Omit if the screen has no branches.
- **Hook placement**: directly after the `useState` declarations in the screen component, before the data-fetching hooks. Place once per screen.
- **For branched screens**: pass the state variable that holds the current tab/view-mode/step (mapped to a stable PascalCase string) as `subScreen`. The hook re-fires on change.

## TDD scope

Task 0 (the foundation hook) is fully TDD: write failing tests first, implement, watch them pass.

Tasks 1–14 (module instrumentation) are **mechanical**: each adds a single `useScreenView(...)` call per screen. Adding unit tests for each of ~50 screens just to assert the hook was called is ceremony; the hook itself is covered by Task 0's tests, the component build catches typos, and the KQL verification in Task 15 reveals any wiring gap empirically. Each module task includes a build/lint gate as the verification step.

---

## Task 0: Foundation — `useScreenView` hook, types, smoke test, coverage doc skeleton

**Files:**
- Create: `frontend/src/telemetry/screenModules.ts`
- Create: `frontend/src/telemetry/useScreenView.ts`
- Create: `frontend/src/telemetry/__tests__/useScreenView.test.tsx`
- Create: `docs/features/usage-analytics-coverage.md`
- Modify: `frontend/src/telemetry/events.ts`
- Modify: `docs/features/usage-analytics.md`

### Step 0.1: Write the failing tests for `useScreenView`

- [ ] Create `frontend/src/telemetry/__tests__/useScreenView.test.tsx`:

```tsx
import { renderHook } from '@testing-library/react';
import type { ApplicationInsights } from '@microsoft/applicationinsights-web';
import { useScreenView } from '../useScreenView';
import * as appInsightsModule from '../appInsights';

const mockTrackEvent = jest.fn();
const mockAI = { trackEvent: mockTrackEvent };

describe('useScreenView', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    jest.spyOn(appInsightsModule, 'getAppInsights').mockReturnValue(
      mockAI as unknown as ApplicationInsights,
    );
  });

  it('fires ScreenViewed on mount with module and screen', () => {
    renderHook(() => useScreenView('Dashboard', 'Dashboard'));

    expect(mockTrackEvent).toHaveBeenCalledTimes(1);
    expect(mockTrackEvent).toHaveBeenCalledWith(
      { name: 'ScreenViewed' },
      { module: 'Dashboard', screen: 'Dashboard' },
    );
  });

  it('includes subScreen when provided', () => {
    renderHook(() => useScreenView('Catalog', 'CatalogDetail', 'MarginsTab'));

    expect(mockTrackEvent).toHaveBeenCalledWith(
      { name: 'ScreenViewed' },
      { module: 'Catalog', screen: 'CatalogDetail', subScreen: 'MarginsTab' },
    );
  });

  it('re-fires when subScreen changes', () => {
    const { rerender } = renderHook(
      ({ tab }: { tab: string }) => useScreenView('Catalog', 'CatalogDetail', tab),
      { initialProps: { tab: 'BasicTab' } },
    );

    expect(mockTrackEvent).toHaveBeenCalledTimes(1);

    rerender({ tab: 'MarginsTab' });

    expect(mockTrackEvent).toHaveBeenCalledTimes(2);
    expect(mockTrackEvent).toHaveBeenLastCalledWith(
      { name: 'ScreenViewed' },
      { module: 'Catalog', screen: 'CatalogDetail', subScreen: 'MarginsTab' },
    );
  });

  it('does not re-fire when subScreen is unchanged across renders', () => {
    const { rerender } = renderHook(
      ({ tab }: { tab: string }) => useScreenView('Catalog', 'CatalogDetail', tab),
      { initialProps: { tab: 'BasicTab' } },
    );
    rerender({ tab: 'BasicTab' });

    expect(mockTrackEvent).toHaveBeenCalledTimes(1);
  });

  it('does not throw when AI instance is null', () => {
    jest.spyOn(appInsightsModule, 'getAppInsights').mockReturnValue(null);

    expect(() =>
      renderHook(() => useScreenView('Dashboard', 'Dashboard')),
    ).not.toThrow();
  });
});
```

### Step 0.2: Run tests — verify they fail

- [ ] Run:

```bash
cd frontend && npx react-scripts test --watchAll=false src/telemetry/__tests__/useScreenView.test.tsx
```

Expected: FAIL with `Cannot find module '../useScreenView'`.

### Step 0.3: Create the `ScreenModule` union

- [ ] Create `frontend/src/telemetry/screenModules.ts`:

```ts
export type ScreenModule =
  | 'Dashboard'
  | 'Finance'
  | 'Catalog'
  | 'Journal'
  | 'Purchase'
  | 'Manufacturing'
  | 'Logistics'
  | 'Marketing'
  | 'Customer'
  | 'Automation'
  | 'Knowledge'
  | 'Admin'
  | 'Terminal'
  | 'Baleni';
```

### Step 0.4: Add `'ScreenViewed'` to the event-name union

- [ ] Modify `frontend/src/telemetry/events.ts` to:

```ts
export type TelemetryEventName =
  | 'DashboardTileClicked'
  | 'PhotobankBulkTagApplied'
  | 'ManufactureOrderCreated'
  | 'PurchaseOrderSubmitted'
  | 'FeatureFlagToggled'
  | 'ScreenViewed';
```

### Step 0.5: Implement `useScreenView`

- [ ] Create `frontend/src/telemetry/useScreenView.ts`:

```ts
import { useEffect } from 'react';
import { useTelemetry } from './useTelemetry';
import type { ScreenModule } from './screenModules';

export function useScreenView(
  module: ScreenModule,
  screen: string,
  subScreen?: string,
): void {
  const { trackEvent } = useTelemetry();

  useEffect(() => {
    const properties: Record<string, string> = { module, screen };
    if (subScreen !== undefined) {
      properties.subScreen = subScreen;
    }
    trackEvent('ScreenViewed', properties);
  }, [module, screen, subScreen]); // trackEvent is stable per useTelemetry contract; intentionally excluded
}
```

### Step 0.6: Run tests — verify they pass

- [ ] Run:

```bash
cd frontend && npx react-scripts test --watchAll=false src/telemetry/__tests__/useScreenView.test.tsx
```

Expected: PASS (5 tests).

### Step 0.7: Run the full telemetry test suite to confirm no regression

- [ ] Run:

```bash
cd frontend && npx react-scripts test --watchAll=false src/telemetry/
```

Expected: PASS (existing useTelemetry + appInsights tests + new useScreenView tests).

### Step 0.8: Create the coverage matrix document

- [ ] Create `docs/features/usage-analytics-coverage.md`:

````markdown
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
| Dashboard | `/` | `components/pages/Dashboard.tsx` | — | `[ ] base` |

## Finance

| Screen | Route | Component | Branches | Coverage |
|---|---|---|---|---|
| FinancialOverview | `/finance/overview` | `components/pages/FinancialOverview.tsx` | — | `[ ] base` |
| BankStatementImport | `/finance/bank-statements` | `components/pages/BankStatementImportChart.tsx` | — | `[ ] base` |
| ProductMarginSummary | `/analytics/product-margin-summary` | `components/pages/ProductMarginSummary.tsx` | — | `[ ] base` |

## Catalog

| Screen | Route | Component | Branches | Coverage |
|---|---|---|---|---|
| CatalogList | `/catalog` | `components/pages/CatalogList.tsx` | — | `[ ] base` |
| CatalogDetail | (modal from list) | `components/pages/CatalogDetail.tsx` | tabs: Basic, History, Margins, Composition, Journal, Usage, Documents, Pif; chartTabs: Input, Output | `[ ] base` `[ ] BasicTab` `[ ] HistoryTab` `[ ] MarginsTab` `[ ] CompositionTab` `[ ] JournalTab` `[ ] UsageTab` `[ ] DocumentsTab` `[ ] PifTab` `[ ] ChartInput` `[ ] ChartOutput` |
| ProductMargins | `/products/margins` | `components/pages/ProductMarginsList.tsx` | — | `[ ] base` |

## Journal

| Screen | Route | Component | Branches | Coverage |
|---|---|---|---|---|
| JournalList | `/journal` | `components/pages/Journal/JournalList.tsx` | — | `[ ] base` |
| JournalEntryNew | `/journal/new` | `components/pages/JournalEntryNew.tsx` | — | `[ ] base` |
| JournalEntryEdit | `/journal/:id/edit` | `components/pages/JournalEntryEdit.tsx` | — | `[ ] base` |

## Purchase

| Screen | Route | Component | Branches | Coverage |
|---|---|---|---|---|
| PurchaseOrderList | `/purchase/orders` | `components/pages/PurchaseOrderList.tsx` | — | `[ ] base` |
| PurchaseStockAnalysis | `/purchase/stock-analysis` | `components/pages/PurchaseStockAnalysis.tsx` | — | `[ ] base` |
| InvoiceClassification | `/purchase/invoice-classification` | `pages/InvoiceClassification/InvoiceClassificationPage.tsx` | tabs: Invoices, Rules | `[ ] base` `[ ] InvoicesTab` `[ ] RulesTab` |

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
| MarketingCalendar | `/marketing/calendar` | `components/marketing/pages/MarketingCalendarPage.tsx` | viewModes: FiveWeeks, TwoWeeks, List | `[ ] base` `[ ] FiveWeeksView` `[ ] TwoWeeksView` `[ ] ListView` |
| Photobank | `/marketing/photobank` | `components/marketing/photobank/pages/PhotobankPage.tsx` | viewModes: Tiles, List | `[ ] base` `[ ] TilesView` `[ ] ListView` |
| PhotobankSettings | `/marketing/photobank/settings` | `components/marketing/photobank/pages/PhotobankSettingsPage.tsx` | — | `[ ] base` |
| LeafletGenerator | `/leaflet-generator` | `features/leaflet-generator/LeafletGeneratorPage.tsx` | — | `[ ] base` |
| Articles | `/articles` | `pages/ArticlesPage.tsx` | tabs: New, List | `[ ] base` `[ ] NewTab` `[ ] ListTab` |
| MarketingFeedback | `/marketing/feedback` | `pages/MarketingFeedbackPage.tsx` | — | `[ ] base` |

## Customer

| Screen | Route | Component | Branches | Coverage |
|---|---|---|---|---|
| IssuedInvoices | `/customer/issued-invoices` | `pages/customer/IssuedInvoicesPage.tsx` | — | `[ ] base` |
| BankStatementsOverview | `/customer/bank-statements-overview` | `pages/customer/BankStatementsOverviewPage.tsx` | — | `[ ] base` |
| SmartsuppChats | `/customer/smartsupp` | `components/customer-support/smartsupp/pages/SmartsuppChatsPage.tsx` | — | `[ ] base` |
| ExpeditionSettings | `/customer/expedition-settings` | `pages/customer/ExpeditionSettingsPage.tsx` | tabs: Cooling, Gifts | `[ ] base` `[ ] CoolingTab` `[ ] GiftsTab` |

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
| KnowledgeBase | `/knowledge-base` | `pages/KnowledgeBasePage.tsx` | tabs: Search, Documents, Upload | `[ ] base` `[ ] SearchTab` `[ ] DocumentsTab` `[ ] UploadTab` |
| KnowledgeBaseFeedback | `/knowledge-base/feedback` | `pages/KnowledgeBaseFeedbackPage.tsx` | — | `[ ] base` |

## Admin

| Screen | Route | Component | Branches | Coverage |
|---|---|---|---|---|
| OrgChart | `/orgchart` | `pages/OrgChartPage.tsx` | — | `[ ] base` |
| FeatureFlagsAdmin | `/admin/feature-flags` | `pages/FeatureFlagsAdminPage.tsx` | — | `[ ] base` |

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
````

### Step 0.9: Update `usage-analytics.md`

- [ ] Modify `docs/features/usage-analytics.md` — add a row to the Event catalogue table (between the existing rows and the "Naming conventions" section). Insert this row at the end of the events table (after the `FeatureFlagToggled` row, before the `## Naming conventions` heading):

```
| `ScreenViewed` | `frontend/src/telemetry/useScreenView.ts` (called from every screen component) | `module: string`, `screen: string`, `subScreen?: string` | — | Which screens and sub-screens (tabs, view-modes, wizard steps) do users actually visit, and how often? See [usage-analytics-coverage.md](./usage-analytics-coverage.md) for the canonical list. |
```

Then add a new query under `## How to query`, immediately after the `### Funnel: users who visited Dashboard but never opened Photobank` block:

````
### Screen + sub-screen usage in last 30 days

```kusto
customEvents
| where timestamp > ago(30d) and name == "ScreenViewed"
| summarize hits=count(),
            users=dcount(user_AuthenticatedId)
            by module=tostring(customDimensions.module),
               screen=tostring(customDimensions.screen),
               subScreen=tostring(customDimensions.subScreen)
| order by hits desc
```

Cross-reference distinct `(module, screen, subScreen)` tuples against `usage-analytics-coverage.md` to find wiring gaps (rows checked but never seen in telemetry) and doc drift (events seen but missing from the doc).
````

### Step 0.10: Run frontend lint + build

- [ ] Run:

```bash
cd frontend && npm run lint && npm run build
```

Expected: both PASS.

### Step 0.11: Commit Task 0

- [ ] Run:

```bash
git add frontend/src/telemetry/screenModules.ts \
        frontend/src/telemetry/useScreenView.ts \
        frontend/src/telemetry/__tests__/useScreenView.test.tsx \
        frontend/src/telemetry/events.ts \
        docs/features/usage-analytics.md \
        docs/features/usage-analytics-coverage.md
git commit -m "feat(telemetry): add useScreenView hook and coverage matrix doc"
```

---

## Module instrumentation tasks (1–14)

Each module task follows the same shape:

1. Add `import { useScreenView } from '<relative-path>/telemetry/useScreenView';` to each listed file.
2. Add the listed `useScreenView(...)` call directly after the screen's `useState` declarations.
3. Tick the corresponding boxes in `docs/features/usage-analytics-coverage.md`.
4. Run `cd frontend && npm run lint && npm run build`.
5. Commit.

Relative import path depends on file depth — examples:
- `components/pages/Dashboard.tsx` → `'../../telemetry/useScreenView'`
- `components/marketing/pages/MarketingCalendarPage.tsx` → `'../../../telemetry/useScreenView'`
- `pages/ArticlesPage.tsx` → `'../telemetry/useScreenView'`

---

## Task 1: Dashboard module

**Files:**
- Modify: `frontend/src/components/pages/Dashboard.tsx`

### Step 1.1: Wire `useScreenView` in Dashboard

- [ ] Add to `frontend/src/components/pages/Dashboard.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
```

After the `useState` declarations near the top of the `Dashboard` component, add:

```tsx
useScreenView('Dashboard', 'Dashboard');
```

### Step 1.2: Tick coverage boxes

- [ ] In `docs/features/usage-analytics-coverage.md`, under the `## Dashboard` table, change `[ ] base` to `[x] base` for the `Dashboard` row.

### Step 1.3: Build + lint

- [ ] Run:

```bash
cd frontend && npm run lint && npm run build
```

Expected: PASS.

### Step 1.4: Commit

- [ ] Run:

```bash
git add frontend/src/components/pages/Dashboard.tsx docs/features/usage-analytics-coverage.md
git commit -m "feat(telemetry): instrument Dashboard screen"
```

---

## Task 2: Admin module

**Files:**
- Modify: `frontend/src/pages/FeatureFlagsAdminPage.tsx`
- Modify: `frontend/src/pages/OrgChartPage.tsx`

### Step 2.1: Wire `useScreenView` in FeatureFlagsAdminPage

- [ ] Add to `frontend/src/pages/FeatureFlagsAdminPage.tsx`:

```tsx
import { useScreenView } from '../telemetry/useScreenView';
```

After the existing `useState` declarations in the component, add:

```tsx
useScreenView('Admin', 'FeatureFlagsAdmin');
```

### Step 2.2: Wire `useScreenView` in OrgChartPage

- [ ] Add to `frontend/src/pages/OrgChartPage.tsx`:

```tsx
import { useScreenView } from '../telemetry/useScreenView';
```

After the existing `useState` declarations (or at the top of the component if none), add:

```tsx
useScreenView('Admin', 'OrgChart');
```

### Step 2.3: Tick coverage boxes

- [ ] In `docs/features/usage-analytics-coverage.md`, under `## Admin`, change `[ ] base` → `[x] base` on both rows (`OrgChart`, `FeatureFlagsAdmin`).

### Step 2.4: Build + lint + commit

- [ ] Run:

```bash
cd frontend && npm run lint && npm run build
git add frontend/src/pages/FeatureFlagsAdminPage.tsx \
        frontend/src/pages/OrgChartPage.tsx \
        docs/features/usage-analytics-coverage.md
git commit -m "feat(telemetry): instrument Admin screens"
```

---

## Task 3: Knowledge module

**Files:**
- Modify: `frontend/src/pages/KnowledgeBasePage.tsx`
- Modify: `frontend/src/pages/KnowledgeBaseFeedbackPage.tsx`

### Step 3.1: Wire `useScreenView` in KnowledgeBasePage (with tab branching)

`KnowledgeBasePage.tsx:12` declares `const [activeTab, setActiveTab] = useState<Tab>('search')`, with `activeTab` values `'search' | 'documents' | 'upload'`.

- [ ] Add to `frontend/src/pages/KnowledgeBasePage.tsx`:

```tsx
import { useScreenView } from '../telemetry/useScreenView';
```

After the `useState` declaration on line 12, add:

```tsx
const subScreen =
  activeTab === 'search' ? 'SearchTab' :
  activeTab === 'documents' ? 'DocumentsTab' :
  'UploadTab';
useScreenView('Knowledge', 'KnowledgeBase', subScreen);
```

### Step 3.2: Wire `useScreenView` in KnowledgeBaseFeedbackPage

- [ ] Add to `frontend/src/pages/KnowledgeBaseFeedbackPage.tsx`:

```tsx
import { useScreenView } from '../telemetry/useScreenView';
```

After the existing `useState` declarations, add:

```tsx
useScreenView('Knowledge', 'KnowledgeBaseFeedback');
```

### Step 3.3: Tick coverage boxes

- [ ] In `docs/features/usage-analytics-coverage.md`, under `## Knowledge`, tick:
  - `KnowledgeBase`: `[x] base`, `[x] SearchTab`, `[x] DocumentsTab`, `[x] UploadTab`
  - `KnowledgeBaseFeedback`: `[x] base`

### Step 3.4: Build + lint + commit

- [ ] Run:

```bash
cd frontend && npm run lint && npm run build
git add frontend/src/pages/KnowledgeBasePage.tsx \
        frontend/src/pages/KnowledgeBaseFeedbackPage.tsx \
        docs/features/usage-analytics-coverage.md
git commit -m "feat(telemetry): instrument Knowledge module screens"
```

---

## Task 4: Customer module

**Files:**
- Modify: `frontend/src/pages/customer/IssuedInvoicesPage.tsx`
- Modify: `frontend/src/pages/customer/BankStatementsOverviewPage.tsx`
- Modify: `frontend/src/components/customer-support/smartsupp/pages/SmartsuppChatsPage.tsx`
- Modify: `frontend/src/pages/customer/ExpeditionSettingsPage.tsx`

### Step 4.1: Wire `useScreenView` in IssuedInvoicesPage

- [ ] Add to `frontend/src/pages/customer/IssuedInvoicesPage.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
```

After the `useState` declarations, add:

```tsx
useScreenView('Customer', 'IssuedInvoices');
```

### Step 4.2: Wire `useScreenView` in BankStatementsOverviewPage

- [ ] Add to `frontend/src/pages/customer/BankStatementsOverviewPage.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
```

After the `useState` declarations, add:

```tsx
useScreenView('Customer', 'BankStatementsOverview');
```

### Step 4.3: Wire `useScreenView` in SmartsuppChatsPage

- [ ] Add to `frontend/src/components/customer-support/smartsupp/pages/SmartsuppChatsPage.tsx`:

```tsx
import { useScreenView } from '../../../../telemetry/useScreenView';
```

After the `useState` declarations, add:

```tsx
useScreenView('Customer', 'SmartsuppChats');
```

### Step 4.4: Wire `useScreenView` in ExpeditionSettingsPage (URL-driven tabs)

`ExpeditionSettingsPage.tsx:11` reads `activeTab` from URL: `(searchParams.get('tab') as Tab) ?? 'cooling'`, values `'cooling' | 'gifts'`.

- [ ] Add to `frontend/src/pages/customer/ExpeditionSettingsPage.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
```

After the `activeTab` derivation on line 11, add:

```tsx
useScreenView('Customer', 'ExpeditionSettings', activeTab === 'cooling' ? 'CoolingTab' : 'GiftsTab');
```

### Step 4.5: Tick coverage boxes

- [ ] In `docs/features/usage-analytics-coverage.md`, under `## Customer`, tick:
  - `IssuedInvoices`: `[x] base`
  - `BankStatementsOverview`: `[x] base`
  - `SmartsuppChats`: `[x] base`
  - `ExpeditionSettings`: `[x] base`, `[x] CoolingTab`, `[x] GiftsTab`

### Step 4.6: Build + lint + commit

- [ ] Run:

```bash
cd frontend && npm run lint && npm run build
git add frontend/src/pages/customer/IssuedInvoicesPage.tsx \
        frontend/src/pages/customer/BankStatementsOverviewPage.tsx \
        frontend/src/components/customer-support/smartsupp/pages/SmartsuppChatsPage.tsx \
        frontend/src/pages/customer/ExpeditionSettingsPage.tsx \
        docs/features/usage-analytics-coverage.md
git commit -m "feat(telemetry): instrument Customer module screens"
```

---

## Task 5: Journal module

**Files:**
- Modify: `frontend/src/components/pages/Journal/JournalList.tsx`
- Modify: `frontend/src/components/pages/JournalEntryNew.tsx`
- Modify: `frontend/src/components/pages/JournalEntryEdit.tsx`

### Step 5.1: Wire `useScreenView` in JournalList

- [ ] Add to `frontend/src/components/pages/Journal/JournalList.tsx`:

```tsx
import { useScreenView } from '../../../telemetry/useScreenView';
```

After the `useState` declarations, add:

```tsx
useScreenView('Journal', 'JournalList');
```

### Step 5.2: Wire `useScreenView` in JournalEntryNew

- [ ] Add to `frontend/src/components/pages/JournalEntryNew.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
```

After the `useState` declarations, add:

```tsx
useScreenView('Journal', 'JournalEntryNew');
```

### Step 5.3: Wire `useScreenView` in JournalEntryEdit

- [ ] Add to `frontend/src/components/pages/JournalEntryEdit.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
```

After the `useState` declarations, add:

```tsx
useScreenView('Journal', 'JournalEntryEdit');
```

### Step 5.4: Tick coverage boxes

- [ ] In `docs/features/usage-analytics-coverage.md`, under `## Journal`, tick `[x] base` for all three rows.

### Step 5.5: Build + lint + commit

- [ ] Run:

```bash
cd frontend && npm run lint && npm run build
git add frontend/src/components/pages/Journal/JournalList.tsx \
        frontend/src/components/pages/JournalEntryNew.tsx \
        frontend/src/components/pages/JournalEntryEdit.tsx \
        docs/features/usage-analytics-coverage.md
git commit -m "feat(telemetry): instrument Journal module screens"
```

---

## Task 6: Finance module

**Files:**
- Modify: `frontend/src/components/pages/FinancialOverview.tsx`
- Modify: `frontend/src/components/pages/BankStatementImportChart.tsx`
- Modify: `frontend/src/components/pages/ProductMarginSummary.tsx`

### Step 6.1: Wire `useScreenView` in FinancialOverview

- [ ] Add to `frontend/src/components/pages/FinancialOverview.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
```

After the `useState` declarations, add:

```tsx
useScreenView('Finance', 'FinancialOverview');
```

### Step 6.2: Wire `useScreenView` in BankStatementImportChart

- [ ] Add to `frontend/src/components/pages/BankStatementImportChart.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
```

After the `useState` declarations, add:

```tsx
useScreenView('Finance', 'BankStatementImport');
```

### Step 6.3: Wire `useScreenView` in ProductMarginSummary

- [ ] Add to `frontend/src/components/pages/ProductMarginSummary.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
```

After the `useState` declarations, add:

```tsx
useScreenView('Finance', 'ProductMarginSummary');
```

### Step 6.4: Tick coverage boxes

- [ ] In `docs/features/usage-analytics-coverage.md`, under `## Finance`, tick `[x] base` for all three rows.

### Step 6.5: Build + lint + commit

- [ ] Run:

```bash
cd frontend && npm run lint && npm run build
git add frontend/src/components/pages/FinancialOverview.tsx \
        frontend/src/components/pages/BankStatementImportChart.tsx \
        frontend/src/components/pages/ProductMarginSummary.tsx \
        docs/features/usage-analytics-coverage.md
git commit -m "feat(telemetry): instrument Finance module screens"
```

---

## Task 7: Catalog module

**Files:**
- Modify: `frontend/src/components/pages/CatalogList.tsx`
- Modify: `frontend/src/components/pages/CatalogDetail.tsx`
- Modify: `frontend/src/components/pages/ProductMarginsList.tsx`

### Step 7.1: Wire `useScreenView` in CatalogList

- [ ] Add to `frontend/src/components/pages/CatalogList.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
```

After the `useState` declarations, add:

```tsx
useScreenView('Catalog', 'CatalogList');
```

### Step 7.2: Wire `useScreenView` in CatalogDetail (tabs + chart sub-tabs, gated by `isOpen`)

`CatalogDetail.tsx:48` declares `activeTab` (8 values: `"basic" | "history" | "margins" | "composition" | "journal" | "usage" | "documents" | "pif"`) and `activeChartTab:51` (`"input" | "output"`). The detail is a modal — only fire `useScreenView` when `isOpen` is true.

- [ ] Add to `frontend/src/components/pages/CatalogDetail.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
```

After the existing `useState` declarations (after line 59), add:

```tsx
const tabToSubScreen: Record<typeof activeTab, string> = {
  basic: 'BasicTab',
  history: 'HistoryTab',
  margins: 'MarginsTab',
  composition: 'CompositionTab',
  journal: 'JournalTab',
  usage: 'UsageTab',
  documents: 'DocumentsTab',
  pif: 'PifTab',
};
useScreenView('Catalog', 'CatalogDetail', isOpen ? tabToSubScreen[activeTab] : undefined);
useScreenView(
  'Catalog',
  'CatalogDetail',
  isOpen && activeTab === 'history' ? (activeChartTab === 'input' ? 'ChartInput' : 'ChartOutput') : undefined,
);
```

Rationale for the second call: chart tabs are only meaningful when the History tab is active. Passing `undefined` when not relevant means the second `ScreenViewed` fires only when the user actually views a chart sub-tab, producing two distinct events for the same screen but different `subScreen` values.

### Step 7.3: Wire `useScreenView` in ProductMarginsList

- [ ] Add to `frontend/src/components/pages/ProductMarginsList.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
```

After the `useState` declarations, add:

```tsx
useScreenView('Catalog', 'ProductMargins');
```

### Step 7.4: Tick coverage boxes

- [ ] In `docs/features/usage-analytics-coverage.md`, under `## Catalog`, tick:
  - `CatalogList`: `[x] base`
  - `CatalogDetail`: `[x] base` plus all 10 sub-screen boxes (`BasicTab`, `HistoryTab`, `MarginsTab`, `CompositionTab`, `JournalTab`, `UsageTab`, `DocumentsTab`, `PifTab`, `ChartInput`, `ChartOutput`)
  - `ProductMargins`: `[x] base`

### Step 7.5: Build + lint + commit

- [ ] Run:

```bash
cd frontend && npm run lint && npm run build
git add frontend/src/components/pages/CatalogList.tsx \
        frontend/src/components/pages/CatalogDetail.tsx \
        frontend/src/components/pages/ProductMarginsList.tsx \
        docs/features/usage-analytics-coverage.md
git commit -m "feat(telemetry): instrument Catalog module screens"
```

---

## Task 8: Purchase module

**Files:**
- Modify: `frontend/src/components/pages/PurchaseOrderList.tsx`
- Modify: `frontend/src/components/pages/PurchaseStockAnalysis.tsx`
- Modify: `frontend/src/pages/InvoiceClassification/InvoiceClassificationPage.tsx`

### Step 8.1: Wire `useScreenView` in PurchaseOrderList

- [ ] Add to `frontend/src/components/pages/PurchaseOrderList.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
```

After the `useState` declarations, add:

```tsx
useScreenView('Purchase', 'PurchaseOrderList');
```

### Step 8.2: Wire `useScreenView` in PurchaseStockAnalysis

- [ ] Add to `frontend/src/components/pages/PurchaseStockAnalysis.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
```

After the `useState` declarations, add:

```tsx
useScreenView('Purchase', 'PurchaseStockAnalysis');
```

### Step 8.3: Wire `useScreenView` in InvoiceClassificationPage (tabs)

`InvoiceClassificationPage.tsx:24` declares `const [activeTab, setActiveTab] = useState<TabType>('invoices')` with values `'invoices' | 'rules'`.

- [ ] Add to `frontend/src/pages/InvoiceClassification/InvoiceClassificationPage.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
```

After line 24, add:

```tsx
useScreenView('Purchase', 'InvoiceClassification', activeTab === 'invoices' ? 'InvoicesTab' : 'RulesTab');
```

### Step 8.4: Tick coverage boxes

- [ ] In `docs/features/usage-analytics-coverage.md`, under `## Purchase`, tick:
  - `PurchaseOrderList`: `[x] base`
  - `PurchaseStockAnalysis`: `[x] base`
  - `InvoiceClassification`: `[x] base`, `[x] InvoicesTab`, `[x] RulesTab`

### Step 8.5: Build + lint + commit

- [ ] Run:

```bash
cd frontend && npm run lint && npm run build
git add frontend/src/components/pages/PurchaseOrderList.tsx \
        frontend/src/components/pages/PurchaseStockAnalysis.tsx \
        frontend/src/pages/InvoiceClassification/InvoiceClassificationPage.tsx \
        docs/features/usage-analytics-coverage.md
git commit -m "feat(telemetry): instrument Purchase module screens"
```

---

## Task 9: Marketing module

**Files:**
- Modify: `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx`
- Modify: `frontend/src/components/marketing/photobank/pages/PhotobankPage.tsx`
- Modify: `frontend/src/components/marketing/photobank/pages/PhotobankSettingsPage.tsx`
- Modify: `frontend/src/features/leaflet-generator/LeafletGeneratorPage.tsx`
- Modify: `frontend/src/pages/ArticlesPage.tsx`
- Modify: `frontend/src/pages/MarketingFeedbackPage.tsx`

### Step 9.1: Wire `useScreenView` in MarketingCalendarPage (viewModes)

`MarketingCalendarPage.tsx:55` declares `const [viewMode, setViewMode] = useState<ViewMode>('fiveWeeks')`. `ViewMode` is defined on `MarketingCalendarPage.tsx:52` as `'fiveWeeks' | 'twoWeeks' | 'list'`.

- [ ] Add to `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx`:

```tsx
import { useScreenView } from '../../../telemetry/useScreenView';
```

After line 64 (last of the `useState` block), add:

```tsx
const viewModeSubScreen =
  viewMode === 'fiveWeeks' ? 'FiveWeeksView' :
  viewMode === 'twoWeeks' ? 'TwoWeeksView' :
  'ListView';
useScreenView('Marketing', 'MarketingCalendar', viewModeSubScreen);
```

### Step 9.2: Wire `useScreenView` in PhotobankPage (viewModes)

`PhotobankPage.tsx:60` declares `const [view, setView] = useState<ViewMode>(readViewMode)`. `ViewMode` is defined on `PhotobankPage.tsx:24` as `"tiles" | "list"`.

- [ ] Add to `frontend/src/components/marketing/photobank/pages/PhotobankPage.tsx`:

```tsx
import { useScreenView } from '../../../../telemetry/useScreenView';
```

After line 64 (last of the `useState` block), add:

```tsx
useScreenView('Marketing', 'Photobank', view === 'tiles' ? 'TilesView' : 'ListView');
```

### Step 9.3: Wire `useScreenView` in PhotobankSettingsPage

- [ ] Add to `frontend/src/components/marketing/photobank/pages/PhotobankSettingsPage.tsx`:

```tsx
import { useScreenView } from '../../../../telemetry/useScreenView';
```

After the `useState` declarations, add:

```tsx
useScreenView('Marketing', 'PhotobankSettings');
```

### Step 9.4: Wire `useScreenView` in LeafletGeneratorPage

- [ ] Add to `frontend/src/features/leaflet-generator/LeafletGeneratorPage.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
```

After the `useState` declarations, add:

```tsx
useScreenView('Marketing', 'LeafletGenerator');
```

### Step 9.5: Wire `useScreenView` in ArticlesPage (tabs)

`ArticlesPage.tsx:11` declares `const [activeTab, setActiveTab] = useState<Tab>('new')` with values `'new' | 'list'`.

- [ ] Add to `frontend/src/pages/ArticlesPage.tsx`:

```tsx
import { useScreenView } from '../telemetry/useScreenView';
```

After line 11, add:

```tsx
useScreenView('Marketing', 'Articles', activeTab === 'new' ? 'NewTab' : 'ListTab');
```

### Step 9.6: Wire `useScreenView` in MarketingFeedbackPage

- [ ] Add to `frontend/src/pages/MarketingFeedbackPage.tsx`:

```tsx
import { useScreenView } from '../telemetry/useScreenView';
```

After the `useState` declarations, add:

```tsx
useScreenView('Marketing', 'MarketingFeedback');
```

### Step 9.7: Tick coverage boxes

- [ ] In `docs/features/usage-analytics-coverage.md`, under `## Marketing`, tick:
  - `MarketingCalendar`: `[x] base`, `[x] FiveWeeksView`, `[x] TwoWeeksView`, `[x] ListView`
  - `Photobank`: `[x] base`, `[x] TilesView`, `[x] ListView`
  - `PhotobankSettings`: `[x] base`
  - `LeafletGenerator`: `[x] base`
  - `Articles`: `[x] base`, `[x] NewTab`, `[x] ListTab`
  - `MarketingFeedback`: `[x] base`

### Step 9.8: Build + lint + commit

- [ ] Run:

```bash
cd frontend && npm run lint && npm run build
git add frontend/src/components/marketing/pages/MarketingCalendarPage.tsx \
        frontend/src/components/marketing/photobank/pages/PhotobankPage.tsx \
        frontend/src/components/marketing/photobank/pages/PhotobankSettingsPage.tsx \
        frontend/src/features/leaflet-generator/LeafletGeneratorPage.tsx \
        frontend/src/pages/ArticlesPage.tsx \
        frontend/src/pages/MarketingFeedbackPage.tsx \
        docs/features/usage-analytics-coverage.md
git commit -m "feat(telemetry): instrument Marketing module screens"
```

---

## Task 10: Logistics module

**Files:**
- Modify: `frontend/src/components/pages/InventoryList.tsx`
- Modify: `frontend/src/components/pages/TransportBoxList.tsx`
- Modify: `frontend/src/components/pages/TransportBoxReceive.tsx`
- Modify: `frontend/src/components/pages/GiftPackageManufacturing.tsx`
- Modify: `frontend/src/pages/PackingMaterialsPage.tsx`
- Modify: `frontend/src/components/pages/WarehouseStatistics.tsx`
- Modify: `frontend/src/pages/ExpeditionListArchivePage.tsx`

### Step 10.1: Wire `useScreenView` in each Logistics screen

For each file, add the import (use the relative path appropriate to depth — `../../telemetry/useScreenView` for `components/pages/*` and `pages/*`-flat files use `../telemetry/useScreenView`) and add the `useScreenView` call right after `useState` declarations.

- [ ] `InventoryList.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
// after useState declarations:
useScreenView('Logistics', 'InventoryFinishedGoods');
```

- [ ] `TransportBoxList.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
useScreenView('Logistics', 'TransportBoxes');
```

- [ ] `TransportBoxReceive.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
useScreenView('Logistics', 'TransportBoxReceive');
```

- [ ] `GiftPackageManufacturing.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
useScreenView('Logistics', 'GiftPackageManufacturing');
```

- [ ] `PackingMaterialsPage.tsx`:

```tsx
import { useScreenView } from '../telemetry/useScreenView';
useScreenView('Logistics', 'PackingMaterials');
```

- [ ] `WarehouseStatistics.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
useScreenView('Logistics', 'WarehouseStatistics');
```

- [ ] `ExpeditionListArchivePage.tsx`:

```tsx
import { useScreenView } from '../telemetry/useScreenView';
useScreenView('Logistics', 'ExpeditionArchive');
```

### Step 10.2: Tick coverage boxes

- [ ] In `docs/features/usage-analytics-coverage.md`, under `## Logistics`, tick `[x] base` for all 7 rows.

### Step 10.3: Build + lint + commit

- [ ] Run:

```bash
cd frontend && npm run lint && npm run build
git add frontend/src/components/pages/InventoryList.tsx \
        frontend/src/components/pages/TransportBoxList.tsx \
        frontend/src/components/pages/TransportBoxReceive.tsx \
        frontend/src/components/pages/GiftPackageManufacturing.tsx \
        frontend/src/pages/PackingMaterialsPage.tsx \
        frontend/src/components/pages/WarehouseStatistics.tsx \
        frontend/src/pages/ExpeditionListArchivePage.tsx \
        docs/features/usage-analytics-coverage.md
git commit -m "feat(telemetry): instrument Logistics module screens"
```

---

## Task 11: Manufacturing module

**Files:**
- Modify: `frontend/src/components/pages/ManufacturingStockAnalysis.tsx`
- Modify: `frontend/src/components/pages/ManufactureOutput.tsx`
- Modify: `frontend/src/components/pages/ManufactureBatchCalculator.tsx`
- Modify: `frontend/src/components/pages/ManufactureBatchPlanning.tsx`
- Modify: `frontend/src/components/manufacture/pages/ManufactureOrderList.tsx`
- Modify: `frontend/src/components/manufacture/pages/ManufactureOrderDetail.tsx`
- Modify: `frontend/src/components/pages/ManufactureInventoryList.tsx`
- Modify: `frontend/src/components/pages/ManufacturedInventoryPage.tsx`

### Step 11.1: Wire `useScreenView` in flat Manufacturing screens

- [ ] `ManufacturingStockAnalysis.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
useScreenView('Manufacturing', 'ManufacturingStockAnalysis');
```

- [ ] `ManufactureOutput.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
useScreenView('Manufacturing', 'ManufactureOutput');
```

- [ ] `ManufactureBatchCalculator.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
useScreenView('Manufacturing', 'ManufactureBatchCalculator');
```

- [ ] `ManufactureBatchPlanning.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
useScreenView('Manufacturing', 'ManufactureBatchPlanning');
```

- [ ] `ManufactureInventoryList.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
useScreenView('Manufacturing', 'ManufactureInventory');
```

- [ ] `ManufacturedInventoryPage.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
useScreenView('Manufacturing', 'ManufacturedProductInventory');
```

### Step 11.2: Wire `useScreenView` in ManufactureOrderList (viewModes)

`ManufactureOrderList.tsx:60` declares `const [viewMode, setViewMode] = useState<'grid' | 'calendar' | 'weekly'>(...)`.

- [ ] Add to `frontend/src/components/manufacture/pages/ManufactureOrderList.tsx`:

```tsx
import { useScreenView } from '../../../telemetry/useScreenView';
```

After line 60, add:

```tsx
const viewModeSubScreen =
  viewMode === 'grid' ? 'GridView' :
  viewMode === 'calendar' ? 'CalendarView' :
  'WeeklyView';
useScreenView('Manufacturing', 'ManufactureOrderList', viewModeSubScreen);
```

### Step 11.3: Wire `useScreenView` in ManufactureOrderDetail (tabs)

`ManufactureOrderDetail.tsx:74` declares `const [activeTab, setActiveTab] = useState<"info" | "notes" | "log" | "conditions">("info")`.

- [ ] Add to `frontend/src/components/manufacture/pages/ManufactureOrderDetail.tsx`:

```tsx
import { useScreenView } from '../../../telemetry/useScreenView';
```

After line 74, add:

```tsx
const tabSubScreen =
  activeTab === 'info' ? 'InfoTab' :
  activeTab === 'notes' ? 'NotesTab' :
  activeTab === 'log' ? 'LogTab' :
  'ConditionsTab';
useScreenView('Manufacturing', 'ManufactureOrderDetail', tabSubScreen);
```

### Step 11.4: Tick coverage boxes

- [ ] In `docs/features/usage-analytics-coverage.md`, under `## Manufacturing`, tick:
  - All flat screens: `[x] base`
  - `ManufactureOrderList`: `[x] base`, `[x] GridView`, `[x] CalendarView`, `[x] WeeklyView`
  - `ManufactureOrderDetail`: `[x] base`, `[x] InfoTab`, `[x] NotesTab`, `[x] LogTab`, `[x] ConditionsTab`

### Step 11.5: Build + lint + commit

- [ ] Run:

```bash
cd frontend && npm run lint && npm run build
git add frontend/src/components/pages/ManufacturingStockAnalysis.tsx \
        frontend/src/components/pages/ManufactureOutput.tsx \
        frontend/src/components/pages/ManufactureBatchCalculator.tsx \
        frontend/src/components/pages/ManufactureBatchPlanning.tsx \
        frontend/src/components/manufacture/pages/ManufactureOrderList.tsx \
        frontend/src/components/manufacture/pages/ManufactureOrderDetail.tsx \
        frontend/src/components/pages/ManufactureInventoryList.tsx \
        frontend/src/components/pages/ManufacturedInventoryPage.tsx \
        docs/features/usage-analytics-coverage.md
git commit -m "feat(telemetry): instrument Manufacturing module screens"
```

---

## Task 12: Automation module

**Files:**
- Modify: `frontend/src/components/pages/automation/BackgroundTasks.tsx`
- Modify: `frontend/src/components/pages/automation/InvoiceImportStatistics.tsx`
- Modify: `frontend/src/components/pages/automation/MeetingTasksPage.tsx`
- Modify: `frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx`
- Modify: `frontend/src/pages/StockOperationsPage.tsx`
- Modify: `frontend/src/pages/RecurringJobsPage.tsx`
- Modify: `frontend/src/pages/customer/DataQualityPage.tsx`

### Step 12.1: Wire `useScreenView` in each Automation screen

- [ ] `BackgroundTasks.tsx`:

```tsx
import { useScreenView } from '../../../telemetry/useScreenView';
useScreenView('Automation', 'BackgroundTasks');
```

- [ ] `InvoiceImportStatistics.tsx`:

```tsx
import { useScreenView } from '../../../telemetry/useScreenView';
useScreenView('Automation', 'InvoiceImportStatistics');
```

- [ ] `MeetingTasksPage.tsx`:

```tsx
import { useScreenView } from '../../../telemetry/useScreenView';
useScreenView('Automation', 'MeetingTasks');
```

- [ ] `MeetingTaskDetailPage.tsx`:

```tsx
import { useScreenView } from '../../../telemetry/useScreenView';
useScreenView('Automation', 'MeetingTaskDetail');
```

- [ ] `StockOperationsPage.tsx`:

```tsx
import { useScreenView } from '../telemetry/useScreenView';
useScreenView('Automation', 'StockOperations');
```

- [ ] `RecurringJobsPage.tsx`:

```tsx
import { useScreenView } from '../telemetry/useScreenView';
useScreenView('Automation', 'RecurringJobs');
```

- [ ] `DataQualityPage.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
useScreenView('Automation', 'DataQuality');
```

### Step 12.2: Tick coverage boxes

- [ ] In `docs/features/usage-analytics-coverage.md`, under `## Automation`, tick `[x] base` for all 7 rows.

### Step 12.3: Build + lint + commit

- [ ] Run:

```bash
cd frontend && npm run lint && npm run build
git add frontend/src/components/pages/automation/BackgroundTasks.tsx \
        frontend/src/components/pages/automation/InvoiceImportStatistics.tsx \
        frontend/src/components/pages/automation/MeetingTasksPage.tsx \
        frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx \
        frontend/src/pages/StockOperationsPage.tsx \
        frontend/src/pages/RecurringJobsPage.tsx \
        frontend/src/pages/customer/DataQualityPage.tsx \
        docs/features/usage-analytics-coverage.md
git commit -m "feat(telemetry): instrument Automation module screens"
```

---

## Task 13: Terminal module

**Files:**
- Modify: `frontend/src/components/terminal/TerminalHome.tsx`
- Modify: `frontend/src/components/terminal/TransportBoxCheck.tsx`
- Modify: `frontend/src/components/terminal/box-fill/BoxFillWorkflow.tsx`
- Modify: `frontend/src/components/terminal/TransportBoxReceive.tsx`

(`/terminal/stocktake` and `/terminal/lot-identification` are Coming Soon placeholders — skip; instrument when content lands.)

### Step 13.1: Wire `useScreenView` in TerminalHome

- [ ] Add to `frontend/src/components/terminal/TerminalHome.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
useScreenView('Terminal', 'TerminalHome');
```

### Step 13.2: Wire `useScreenView` in TransportBoxCheck

- [ ] Add to `frontend/src/components/terminal/TransportBoxCheck.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
useScreenView('Terminal', 'TerminalBoxCheck');
```

### Step 13.3: Wire `useScreenView` in BoxFillWorkflow (wizard steps)

`BoxFillWorkflow.tsx:10` declares `const [step, setStep] = useState<Step>("scan")` with values `"scan" | "add-items"`.

- [ ] Add to `frontend/src/components/terminal/box-fill/BoxFillWorkflow.tsx`:

```tsx
import { useScreenView } from '../../../telemetry/useScreenView';
```

After line 15 (last of the `useState` block), add:

```tsx
useScreenView('Terminal', 'TerminalBoxFill', step === 'scan' ? 'ScanStep' : 'AddItemsStep');
```

### Step 13.4: Wire `useScreenView` in TransportBoxReceive (terminal)

- [ ] Add to `frontend/src/components/terminal/TransportBoxReceive.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
useScreenView('Terminal', 'TerminalReceive');
```

### Step 13.5: Tick coverage boxes

- [ ] In `docs/features/usage-analytics-coverage.md`, under `## Terminal`, tick:
  - `TerminalHome`: `[x] base`
  - `TerminalBoxCheck`: `[x] base`
  - `TerminalBoxFill`: `[x] base`, `[x] ScanStep`, `[x] AddItemsStep`
  - `TerminalReceive`: `[x] base`
  - (Leave `TerminalStocktake` and `TerminalLotIdentification` unticked — deferred.)

### Step 13.6: Build + lint + commit

- [ ] Run:

```bash
cd frontend && npm run lint && npm run build
git add frontend/src/components/terminal/TerminalHome.tsx \
        frontend/src/components/terminal/TransportBoxCheck.tsx \
        frontend/src/components/terminal/box-fill/BoxFillWorkflow.tsx \
        frontend/src/components/terminal/TransportBoxReceive.tsx \
        docs/features/usage-analytics-coverage.md
git commit -m "feat(telemetry): instrument Terminal module screens"
```

---

## Task 14: Baleni module

**Files:**
- Modify: `frontend/src/components/baleni/BaleniHome.tsx`
- Modify: `frontend/src/components/baleni/BaleniPacking.tsx`
- Modify: `frontend/src/components/baleni/zasilky/ZasilkyPage.tsx`

(`/baleni/statistiky` is a placeholder — skip.)

### Step 14.1: Wire `useScreenView` in BaleniHome

- [ ] Add to `frontend/src/components/baleni/BaleniHome.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
useScreenView('Baleni', 'BaleniHome');
```

### Step 14.2: Wire `useScreenView` in BaleniPacking

- [ ] Add to `frontend/src/components/baleni/BaleniPacking.tsx`:

```tsx
import { useScreenView } from '../../telemetry/useScreenView';
useScreenView('Baleni', 'BaleniPacking');
```

### Step 14.3: Wire `useScreenView` in ZasilkyPage

- [ ] Add to `frontend/src/components/baleni/zasilky/ZasilkyPage.tsx`:

```tsx
import { useScreenView } from '../../../telemetry/useScreenView';
useScreenView('Baleni', 'BaleniShipments');
```

### Step 14.4: Tick coverage boxes

- [ ] In `docs/features/usage-analytics-coverage.md`, under `## Baleni`, tick `[x] base` for `BaleniHome`, `BaleniPacking`, `BaleniShipments`. Leave `BaleniStatistics` unticked.

### Step 14.5: Build + lint + commit

- [ ] Run:

```bash
cd frontend && npm run lint && npm run build
git add frontend/src/components/baleni/BaleniHome.tsx \
        frontend/src/components/baleni/BaleniPacking.tsx \
        frontend/src/components/baleni/zasilky/ZasilkyPage.tsx \
        docs/features/usage-analytics-coverage.md
git commit -m "feat(telemetry): instrument Baleni module screens"
```

---

## Task 15: End-to-end verification after deploy

This task runs **after all module PRs are merged and deployed to an environment that has the App Insights connection string configured**. Goal: confirm every checked-in coverage row produces real telemetry, and surface any drift.

### Step 15.1: Wait for traffic

- [ ] After deploy, wait 7 days so every routinely-used screen has been touched at least once by a real user.

### Step 15.2: Run the coverage KQL query

- [ ] In Azure Portal → Application Insights → Logs, run:

```kusto
customEvents
| where timestamp > ago(7d) and name == "ScreenViewed"
| summarize hits=count(),
            users=dcount(user_AuthenticatedId)
            by module=tostring(customDimensions.module),
               screen=tostring(customDimensions.screen),
               subScreen=tostring(customDimensions.subScreen)
| order by module asc, screen asc, subScreen asc
```

### Step 15.3: Diff observed tuples against the coverage doc

- [ ] Export the query result as CSV. For each row in `docs/features/usage-analytics-coverage.md` marked `[x]`, confirm a matching `(module, screen, subScreen)` tuple exists. Anything missing = wiring bug — file an issue linking to the screen file and the coverage row.
- [ ] For each `(module, screen, subScreen)` tuple in the CSV that does NOT appear in the coverage doc, decide: either add the row (doc drift) or fix the call site (wrong module/screen string).

### Step 15.4: Record verification outcome

- [ ] Append a brief note to the top of `docs/features/usage-analytics-coverage.md`:

```markdown
## Verification log

- YYYY-MM-DD: full sweep complete, 0/N rows missing telemetry, 0/M new tuples not yet documented.
```

(No commit step until issues are resolved; commit once the run is clean.)

---

## Self-review notes

**Spec coverage:** Every section of the spec maps to tasks:
- "Helper hook" → Task 0 (steps 0.1–0.7)
- "Coverage matrix document" → Task 0 (step 0.8)
- "Phased instrumentation" → Tasks 1–14 (one per module, in the spec's order)
- "Verification" → Task 15 + the smoke test in Task 0
- Naming conventions → enforced by `ScreenModule` type (Task 0.3) + per-task literal strings
- Update to `usage-analytics.md` → Task 0 (step 0.9)
- "Out of scope" — explicitly noted at the top of this plan and in the coverage doc's contract section

**Type consistency check:** `useScreenView(module: ScreenModule, screen: string, subScreen?: string)` signature is identical across Task 0 definition and every call site in Tasks 1–14. `module` literals always match the `ScreenModule` union members. `screen` values match the coverage doc's `Screen` column. `subScreen` values match the coverage doc's checkbox labels.

**Known external-state caveats:** None. All branch state types (`activeTab`, `viewMode`, `view`, `step`, `activeChartTab`) were verified against their actual definitions in the source files during plan authoring. If the implementer finds a discrepancy (e.g., a tab union has been extended since this plan was written), update the corresponding subScreen mapping and the coverage doc row in the same commit.
