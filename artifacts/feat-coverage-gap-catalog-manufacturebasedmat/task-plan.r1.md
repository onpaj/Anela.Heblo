Plan saved to `docs/superpowers/plans/2026-06-15-manufacturebased-material-cost-provider-tests.md`. Self-review pass:

- **Spec coverage:** all FRs and NFRs mapped to tasks (table at the end of the plan).
- **No placeholders:** every step shows the actual code or command an engineer would run.
- **Type consistency:** uses `ManufactureHistoryRecord` (not the spec's stale `CatalogManufactureRecord`), `ErpPrice = new ProductPriceErp { … }` everywhere `PurchasePriceWithVat` is needed, and `DefaultHistoryDays = 4000` consistently across all date-anchored tests.

16 tasks, one new file. The plan is self-contained; the engineer can execute it task-by-task without referring back to the spec.