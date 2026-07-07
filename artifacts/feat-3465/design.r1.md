# Design: GetProductMarginSummaryHandler SRP Refactor

## Skip Design: true (per arch-review.r1.md)

N/A — backend-only refactor with no UI/UX surface. The architecture review
(`arch-review.r1.md`) set `Skip Design: true` because this change extracts
existing business logic (weighted-average margin aggregation and top-product
sorting) out of `GetProductMarginSummaryHandler` into DI-registered services
(`IMarginCalculator.GetGroupAggregatedMarginData` and a new
`ITopProductSorter`). No new or changed visual components, screens, layouts,
or API/contract shapes are involved. This file exists only to satisfy the
pipeline's artifact contract for the planning phase's inputs.
