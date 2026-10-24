# docs-and-process-docs r1
- `docs/integrations/flexi-api.md`: new "Known slow endpoints" section (endpoint table, measures taken, FlexiBee-side limits, follow-up option, App Insights query, decision rule).
- `calc-margins.md`, `calc-financial-overview.md`: ledger cache semantics (single-flight, 6 h closed / 15 min open, `FlexiLedger` config), Runtime facts entry, verified_at bumped.
- `sync-catalog-master-data.md`, `calc-dqt-lot-stock.md`: `Catalog.LotsRefreshCompleted` Runtime facts entry, verified_at bumped.
- `sync-catalog-prices.md`, `sync-flexi-analytics.md`: owners of changed code; verified_at bumped (no behaviour change for them).
- `check.py index` ok; `check.py check` OK, no stale entries for changed files (remaining stale item job-double-shot-order-swap is unrelated).
## Status
DONE
