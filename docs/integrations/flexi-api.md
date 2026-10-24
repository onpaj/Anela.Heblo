# ABRA Flexi API findings

No sandbox — every call hits the live company. Record verified behaviour here before relying on it.

## Ceník purchase price (`cenik.nakupCena`)

- Verified 2026-09-24: `PUT /c/{company}/cenik/{id}.json` with `{"winstrom":{"cenik":{"nakupCena":"<decimal>"}}}`
  updates the purchase price, excluding VAT, in the item's primary unit (`mj1`). Address by numeric id only —
  a PUT by `code:` creates a new item.
- Stored precision: 6 decimals (AKL097 written as `0.311901`, read back `0.311901`). The sync's
  `PurchasePriceTolerance` of 0.0001 therefore does not cause nightly re-writes.
- Written nightly for materials and goods by `purchase-price-recalculation` (see
  `docs/superpowers/specs/2026-09-23-purchase-price-sync-design.md`).

## BoM purchase price roll-up (`prepocti-nakupni-cenu`)

- `PUT /c/{company}/kusovnik.json` with `{"winstrom":{"kusovnik":{"id":<root row id>,"@action":"prepocti-nakupni-cenu"}}}`
  (root row = the `hladina=1` row of the item's kusovník).
- Sums components' **ceník `nakupCena`** — not the stock valuation.
- Does **not** recurse: verified 2026-09-24 on DEZ001100 (root 5654) / DEZ001001M (root 5641). Recalculating
  DEZ001100 alone left it at 214.067552; recalculating DEZ001001M first (2.369467 → 0.316947) and then DEZ001100
  gave 42.571871. Semi-products must be recalculated before the products that contain them.
- Semi-products nested in semi-products: 0; products nested in products: 0 (3,215 kusovník rows, 2026-09-24).
- Sets (`SET*` / `BAL*`) have no kusovník in Flexi (2026-09-24), so phase 3's products-before-sets order is a
  safeguard only.

## Stock to date (`stav-skladu-k-datu`)

- `prumCena` is the average stock price per `mj1` for the requested `sklad` and `datum`, **rounded to 2 decimals**
  (AKL097: `prumCena` 0.31 while `tuz / stavMJ` = 22219.47 / 71238.961591 = 0.311901, matching the 2026
  `skladova-karta.prumCenaTuz`). The exact average is `tuz / stavMJ`; `tuz` is the stock value in CZK.
  Measured 2026-09-24 over 530 materials and goods: 504 within 1 %, 23 off by 1–5 %, 3 off by more than 5 %
  (cheap per-gram materials such as AKL027: 0.01 vs 0.012551).
- Warehouses: 5 = materials, 20 = semi-products, 4 = products and goods.
- Financial Overview values stock from these rows (`FinancialOverviewStockValueAdapter`: Σ `Stock × Price` per
  warehouse and date, i.e. Σ `tuz`), not from ceník `nakupCena` (changed 2026-09-24). `Price` is `tuz / stavMJ`
  only when both are positive; otherwise it falls back to the rounded `prumCena`, so the Σ `tuz` equivalence
  does not hold for rows with zero or negative quantity or value.
- `skladova-karta` holds one card per accounting year (`ucetObdobi`); `prumCenaTuz` there is unrounded.

## Known slow endpoints

Application Insights dependency `petra-tesarikova.flexibee.eu` (P7D, 2026-07-28 to 2026-08-04, issue #3858; earlier
filings #2987, #3759 treated it as one aggregate and did not move p95/p99):

| Endpoint | Calls | p50 | p95 | p99 |
|---|---|---|---|---|
| `POST .../ucetni-denik/query` (accounting journal) | 2,954 | 2.6 s | 24.0 s | 28.7 s |
| `GET .../sarze-expirace.json` (batch/expiration) | 171 | 6.0 s | 19.2 s | 31.5 s |
| `POST .../stav-skladu-k-datu/query` (stock to date, highest volume) | 10,409 | 0.5 s | 3.0 s | 6.3 s |
| `GET .../uzivatelsky-dotaz/21/call.json` | 171 | 4.1 s | 10.3 s | 18.7 s |

`ucetni-denik/query` alone drives the dependency's overall p95/p99.

**Measures taken (#3858)**
- `LedgerService` single-flights identical `ucetni-denik` queries, so concurrent callers share one FlexiBee call.
- Ledger cache TTL is 6 h for ranges ending before the current month and 15 min otherwise
  (`FlexiLedger:ClosedPeriodCacheMinutes` / `FlexiLedger:OpenPeriodCacheMinutes`).
- The shared ledger call is no longer cancelled when one caller aborts.
- New log events: `FlexiLedger.QueryCompleted` (from, to, spanDays, debit, credit, dept, rows, elapsedMs) and
  `Catalog.LotsRefreshCompleted` (rows, elapsedMs); timeout/cancel warnings carry `elapsedMs`.

**FlexiBee-side, not fixable from Heblo:** the per-request cost of `ucetni-denik/query` over a wide date span and of
`sarze-expirace.json`. Heblo can only call them less often or with narrower ranges.

**Follow-up option:** read ledger rows from the nightly `flexi_raw.ledger_entry` copy (see
`docs/processes/sync-flexi-analytics.md`) instead of the live API for closed periods.

**Re-measure** (Application Insights, per endpoint):

```kusto
dependencies
| where timestamp > ago(7d) and target has "flexibee.eu"
| summarize calls=count(), failed=countif(success == false),
            p50=percentile(duration, 50), p95=percentile(duration, 95), p99=percentile(duration, 99)
  by name
| order by p95 desc
```

**Decision rule:** after one release cycle, if `ucetni-denik/query` p95 is still above 10 s, close the signal as an
accepted third-party latency ceiling instead of re-filing a fourth time.
