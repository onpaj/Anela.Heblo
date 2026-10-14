---
process: module-reporting
kind: module
module: reporting
summary: Read-only access for Claude (Heblo MCP) to the month-grain reporting views over GA4 traffic, Shoptet orders and Flexi costs, gated by one permission per source.
owns: []
verified_at: "108621436"
related: [sync-ga4-aggregates, sync-shoptet-orders, sync-flexi-analytics]
---

# Reporting data (MCP)

## Purpose
Lets staff ask Claude questions like "how did e-shop revenue develop this year vs last?",
"which channels bring traffic?" or "what did we spend on Meta ads per month?" without Metabase.
It reads the same curated `v_*` views that Metabase reads; it never writes.

## Users & screens
No screens (ADR-007: reports are not rendered in the Heblo UI). MCP tools `ListReportDatasets`,
`DescribeReportDataset`, `QueryReportDataset`. Permissions: `Reporting_WebAnalytics` (GA4),
`Reporting_Eshop` (Shoptet), `Reporting_Accounting` (Flexi), read only, checked in the handlers.

## Processes
None of its own. Data is produced by `sync-ga4-aggregates`, `sync-shoptet-orders` and
`sync-flexi-analytics`; freshness shown to the agent comes from their `sync_state` tables.

## Data owned
None. Reads `ga4_agg.v_monthly_*`, `shoptet_raw.v_*` (8 views) and `flexi_raw.v_*` (4 matviews).

## External systems
None directly.

## Dependencies
Reads the reporting schemas through the shared `NpgsqlDataSource` from `PersistenceModule`.

## Known quirks
- `v_product_pair_monthly` is not exposed: as a plain view it takes ~17 s for one month on the B1ms server.
- Payroll (`v_payroll_monthly`), `v_posting`, raw tables and `v_customer_repeat_purchase` (md5 of e-mail per customer) are never exposed.
- Queries run with raw ADO.NET, not EF, so the Polly strategy cannot retry a slow statement.
- A new permission does not reach existing production groups automatically; grant it in `/admin/access`.
- Freshness (`sync_state`) lookup failures degrade to status `UNKNOWN` with a logged warning instead of failing the response; reader failures other than the timeout return `ExternalServiceError` with a generic reason (details only in the log).

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Reporting/Catalog/ReportDatasetCatalog.cs` — what is exposed
- `backend/src/Anela.Heblo.Application/Features/Reporting/Query/ReportSqlBuilder.cs` — query → SQL
- `backend/src/Anela.Heblo.Application/Features/Reporting/Data/NpgsqlReportingDataReader.cs` — execution
- `backend/src/Anela.Heblo.API/MCP/Tools/ReportingMcpTools.cs` — MCP surface
