Plan saved to `artifacts/feat-arch-review-shoptetorders-packingorderit/plan.r1.md`.

**Summary:**
- 5 tasks, ~30 bite-sized steps total, TDD flow throughout (reflection tests written first → confirm RED → implement → GREEN → commit).
- Task 1: Tighten internal `PackingOrderItem` doc comment (1 file, 1 line).
- Task 2: Introduce `PackingOrderItemDto` + wire `GetPackingOrderResponse` and `GetPackingOrderHandler`.
- Task 3: Introduce `ScanPackingOrderItemDto` + wire `ScanOrderData` and `ScanPackingOrderHandler` — with explicit warning that the weight-calc at line 102 must keep reading from `order.Items` (internal contract), not the projected DTO list.
- Task 4: Verify OpenAPI client regeneration drops `weightGrams`; `npm run build` + `npm run lint` succeed.
- Task 5: End-to-end validation gates + final grep-based boundary checks.

All four arch-review amendments are folded in: `sealed class` for DTOs, explicit projection-vs-weight-math separation in `ScanPackingOrderHandler`, the resolved OQ-1 noted in NFR-3, and the symmetric reflection test asserting internal `PackingOrderItem.WeightGrams` is retained. Self-review confirms every spec requirement maps to a task.