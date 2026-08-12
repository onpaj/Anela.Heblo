## Module
Packaging (Baleni) Workflow — module-map part #9

## Finding
`ScanPackingOrderHandler.Handle` and `ResetOrderShipmentHandler.Handle` carry a near-verbatim shipment-creation block:

- `maxPackages = 10` + `InvalidPackageCount` guard — `ScanPackingOrderHandler.cs:48-50` vs `ResetOrderShipmentHandler.cs:34-36`
- zero-weight fallback + warning log — Scan `:120-127` vs Reset `:62-69`
- `perPackageWeightGrams = Math.Max(total / n, MinPackageWeightGrams)` — Scan `:130` vs Reset `:72`
- `GetShippingOptionsAsync` + `ShipmentCarrierNotResolved` — Scan `:132-134` vs Reset `:74-76`
- identical `CreateShipmentCommand` construction — Scan `:136-148` vs Reset `:78-90`
- `CreateShipmentAsync` try/catch → `ShipmentCreationFailed` — Scan `:150-158` vs Reset `:92-100`
- `GetLabelsByOrderCodeAsync` + pad-to-`n` label mapping — Scan `:165-180` vs Reset `:104-120`

The copies have already drifted. `ScanPackingOrderHandler` finishes the create path by calling `PersistPackagesAsync(...)`, which writes `Package` rows through `IPackageRepository.ReplacePackagesForOrderAsync`, and validates packer eligibility. `ResetOrderShipmentHandler` contains **no reference to `IPackageRepository`** — it returns the newly created shipment without persisting any `Package` rows. The frontend confirms reset is the terminal step: `frontend/src/components/baleni/PackingShipmentCreator.tsx:69-79` (`handleInvalidateAndNew`) hands the reset result straight to the printer and does not re-scan.

## Rule
`docs/architecture/development_guidelines.md` — Module Independence / DRY intent. The same duplication concern for this exact pair of concerns is already an accepted arch-review outcome: #3194 *ShoptetOrders: packing eligibility warning strings duplicated across two handlers* (CLOSED/COMPLETED).

## Why it matters
Because Reset skips the persistence step its copied-from twin performs, an order whose shipment is invalidated-and-recreated leaves the old (now-cancelled) `Package` rows in the table and writes **no** rows for the replacement shipment. Every downstream read keyed on `Package` rows then silently misreports for reset orders: `GetPackingStatistics` (per-packer / carrier / tracking-coverage figures), `GetPackingDashboard` "packed today", and `FillTrackingNumbersJob`'s tracking backfill all run against the stale/cancelled rows and never see the real replacement shipment. Separately, any future change to the weight / package-count / carrier logic must be hand-mirrored across both blocks or they drift again.

## Suggested direction
Extract the shared "create carrier shipment → map to `n` packages → persist `Package` rows" orchestration into a single collaborator that both handlers invoke, so the persistence and package-mapping logic cannot diverge between the scan and reset paths. Do not implement the fix here — this issue only records the finding.

---
_Filed by arch-review of module-map part #9._

<!-- harness-issue:tsk_6edff05d887a4b8a:a1d8ab83 -->
