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
