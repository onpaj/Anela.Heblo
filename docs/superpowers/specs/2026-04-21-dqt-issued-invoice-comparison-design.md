# DQT — Issued Invoice Comparison (Shoptet ↔ Abra Flexi)

## Context

Invoices are imported from Shoptet and synced to Abra Flexi, but there is no automated check that both systems agree on invoice existence, totals, and line items. This feature adds an in-app Data Quality Test (DQT) that compares issued invoices from both systems for a defined timeframe and surfaces mismatches on the dashboard and a dedicated detail page.

The design uses **shared scaffolding with typed results** — a shared `DqtRun` entity tracks all DQT executions across test types, while each test type owns its own typed result entity. Adding a future DQT (e.g. stock levels) means adding a new result entity + comparison service + registering it in the module. Run tracking, scheduling, dashboard tile, and the detail page infrastructure are reused.

## External Dependency

The FlexiBee SDK currently only supports `GetAsync(string code)` for individual invoice lookup. A date-range list method is being added to the SDK separately and is **out of scope** for this feature. The comparison service will consume it once available. Until then, the comparison logic can be implemented and unit-tested with mocks, but integration tests against Flexi will be blocked.

---

## Domain Model

### DqtRun (shared across all DQT types)

| Field | Type | Notes |
|---|---|---|
| Id | Guid | PK |
| TestType | `DqtTestType` enum | `IssuedInvoiceComparison = 1` |
| DateFrom | DateOnly | Start of compared period |
| DateTo | DateOnly | End of compared period |
| Status | `DqtRunStatus` enum | `Running = 1`, `Completed = 2`, `Failed = 3` |
| StartedAt | DateTime | UTC |
| CompletedAt | DateTime? | UTC, null while running |
| TriggerType | `DqtTriggerType` enum | `Scheduled = 1`, `Manual = 2` |
| TotalChecked | int | Invoices compared |
| TotalMismatches | int | Invoices with issues |
| ErrorMessage | string? | Populated when Status = Failed |

### InvoiceDqtResult (typed for invoice comparison)

| Field | Type | Notes |
|---|---|---|
| Id | Guid | PK |
| DqtRunId | Guid | FK → DqtRun |
| InvoiceCode | string | Shared identifier between Shoptet and Flexi |
| MismatchType | `InvoiceMismatchType` flags enum | See below |
| ShoptetValue | string? | JSON snapshot of Shoptet invoice (totals + items) |
| FlexiValue | string? | JSON snapshot of Flexi invoice (totals + items) |
| Details | string? | Human-readable mismatch summary |

### Enums

```csharp
public enum DqtTestType
{
    IssuedInvoiceComparison = 1
}

public enum DqtRunStatus
{
    Running = 1,
    Completed = 2,
    Failed = 3
}

public enum DqtTriggerType
{
    Scheduled = 1,
    Manual = 2
}

[Flags]
public enum InvoiceMismatchType
{
    None = 0,
    MissingInFlexi = 1,
    MissingInShoptet = 2,
    TotalWithVatDiffers = 4,
    TotalWithoutVatDiffers = 8,
    ItemsDiffer = 16
}
```

---

## Comparison Logic

### IInvoiceDqtComparer

Single method:

```csharp
Task<List<InvoiceDqtResult>> CompareAsync(DateOnly from, DateOnly to, CancellationToken ct)
```

**Algorithm:**

1. Fetch all invoices from Shoptet via `IIssuedInvoiceSource.GetAllAsync(query)` (REST adapter, querying by date range).
2. Fetch all invoices from Flexi via `IIssuedInvoiceClient.GetAllAsync(from, to, ct)` (new method, wrapping SDK).
3. Build lookup dictionaries keyed by `InvoiceCode`.
4. For invoices only in Shoptet → `MissingInFlexi`.
5. For invoices only in Flexi → `MissingInShoptet`.
6. For matched pairs, compare:
   - `Price.WithVat` — tolerance ±0.02m → flag `TotalWithVatDiffers`
   - `Price.WithoutVat` — tolerance ±0.02m → flag `TotalWithoutVatDiffers`
   - Items: match by `ProductCode`, compare `Amount`, `ItemPrice.WithVat`, `ItemPrice.WithoutVat` (tolerance ±0.02m) → flag `ItemsDiffer`
7. Return only mismatches. Matching invoices are counted in `TotalChecked` but not stored.

The ±0.02m tolerance follows the convention established in the existing Shoptet parity integration tests.

### Flexi Adapter Extension

`IIssuedInvoiceClient` needs a new method:

```csharp
Task<List<IssuedInvoiceDetail>> GetAllAsync(DateOnly from, DateOnly to, CancellationToken ct)
```

`FlexiIssuedInvoiceClient` implements this by calling the forthcoming SDK list method and mapping results back to domain `IssuedInvoiceDetail` via AutoMapper (reverse mapping from `IssuedInvoiceDetailFlexiDto`).

---

## Scheduling & Execution

### InvoiceDqtJob (IRecurringJob)

- **Cron**: `"0 21 * * 1"` (Monday 23:00 CEST = 21:00 UTC)
- **TimeZone**: `"Europe/Prague"`
- **Logic**: Calculate last complete week (Monday→Sunday), create `DqtRun` with Status=Running, call `IInvoiceDqtComparer.CompareAsync`, persist results, update run to Completed (or Failed with ErrorMessage on exception).

### MediatR Handlers

| Handler | Request/Response | Purpose |
|---|---|---|
| `RunDqtHandler` | `RunDqtRequest` → `RunDqtResponse` | Manual trigger. Takes `TestType`, optional `DateFrom`/`DateTo` (defaults to last complete week). Enqueues background job via `IHangfireJobEnqueuer`. Returns `DqtRunId`. |
| `GetDqtRunsHandler` | `GetDqtRunsRequest` → `GetDqtRunsResponse` | Paginated list of runs. Filters: TestType, Status, date range. |
| `GetDqtRunDetailHandler` | `GetDqtRunDetailRequest` → `GetDqtRunDetailResponse` | Single run with paginated `InvoiceDqtResult` items. |

---

## API

### DataQualityController

| Endpoint | Method | Description |
|---|---|---|
| `/api/data-quality/runs` | GET | Paginated list of DQT runs |
| `/api/data-quality/runs/{id}` | GET | Run detail with paginated results |
| `/api/data-quality/runs` | POST | Manual trigger (body: TestType, optional DateFrom/DateTo) |

---

## Frontend

### Dashboard Tile — DataQualityTile

- **Size**: Medium (2-column span)
- **Content**: Last run status (pass/fail), date range covered, mismatch count
- **Color coding**: Green = 0 mismatches, orange/red = mismatches found
- **Click action**: Navigate to `/data-quality`

### Dedicated Page — /data-quality

**Top section:**
- Summary cards: last run status, total mismatches, coverage period
- "Run Now" button with optional date range picker

**Runs table:**
- Columns: date range, status, checked count, mismatch count, trigger type, timestamp
- Click a row to drill down

**Run detail (drill-down):**
- Per-invoice results table: InvoiceCode, MismatchType badges, Details text
- Expandable rows showing Shoptet/Flexi JSON snapshots side by side

---

## File Structure

### Backend

```
Domain/Features/DataQuality/
  DqtRun.cs
  DqtTestType.cs
  DqtRunStatus.cs
  DqtTriggerType.cs
  InvoiceDqtResult.cs
  InvoiceMismatchType.cs
  IDqtRunRepository.cs
  IInvoiceDqtResultRepository.cs

Application/Features/DataQuality/
  DataQualityModule.cs
  DataQualityMappingProfile.cs
  Contracts/
    DqtRunDto.cs
    InvoiceDqtResultDto.cs
  Services/
    IInvoiceDqtComparer.cs
    InvoiceDqtComparer.cs
  UseCases/
    RunDqt/
      RunDqtRequest.cs
      RunDqtHandler.cs
      RunDqtResponse.cs
    GetDqtRuns/
      GetDqtRunsRequest.cs
      GetDqtRunsHandler.cs
      GetDqtRunsResponse.cs
    GetDqtRunDetail/
      GetDqtRunDetailRequest.cs
      GetDqtRunDetailHandler.cs
      GetDqtRunDetailResponse.cs
  Infrastructure/Jobs/
    InvoiceDqtJob.cs

Persistence/DataQuality/
  DqtRunConfiguration.cs
  InvoiceDqtResultConfiguration.cs
  DqtRunRepository.cs
  InvoiceDqtResultRepository.cs

API/Controllers/
  DataQualityController.cs
```

### Frontend

```
frontend/src/
  api/hooks/useDataQuality.ts
  pages/customer/DataQualityPage.tsx
  components/dashboard/tiles/DataQualityTile.tsx
  components/data-quality/
    DqtRunsTable.tsx
    DqtRunDetail.tsx
    DqtSummaryCards.tsx
    RunDqtButton.tsx
```

---

## Verification

1. **Unit tests**: `InvoiceDqtComparer` with mocked Shoptet/Flexi sources — test all mismatch types (missing both directions, amount diffs within/outside tolerance, item-level diffs)
2. **Integration test**: Run comparer against real Shoptet + Flexi APIs for a known date range, verify results persist correctly
3. **Manual UI test**: Trigger DQT from `/data-quality` page, verify dashboard tile updates, drill into results
4. **Scheduled job test**: Verify job appears in recurring jobs page with correct cron, can be enabled/disabled
