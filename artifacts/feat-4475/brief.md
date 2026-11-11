## Module / File
`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs`

## Coverage
Line coverage: 46% (filter threshold: 60%). The existing tests cover `SetAdditionalFieldAsync` (all validation guards), `AppendEshopRemarkAsync` (both paths), and `ListOrdersByStatusAsync`/`GetOrdersByStatusAsync`. `ListByExternalCodePrefixAsync` — which pages all orders and finds those with a matching `ExternalCode` prefix — is not unit-tested; it only appears as a utility call in a live-store integration hydration test.

## What's not tested
`ListByExternalCodePrefixAsync` contains three untested behaviors:

1. **Pagination termination**: the loop exits when `page >= data.Data.Paginator.PageCount`. If `PageCount` is 0 or the page increment is wrong, orders on later pages are silently skipped.
2. **Optional email filter**: when `emailFilter != null`, each page's candidates are filtered by `StringComparison.OrdinalIgnoreCase` before the per-order detail fetch. The filter being applied — or not applied — is never asserted against a deterministic response.
3. **ExternalCode prefix match**: only orders whose fetched detail returns an `ExternalCode` that starts with the given prefix (using `StringComparison.Ordinal`) are added to the result. A case-sensitivity mismatch between the filter and callers' assumptions would silently drop or include wrong orders.

## Why it matters
This method is the lookup mechanism used during test-environment seeding and potentially during order reconciliation. Silently dropping matching orders (wrong pagination stop) or including wrong ones (wrong prefix comparison) would produce incorrect reconciliation without any error raised.

## Suggested approach
Unit test with a mocked `HttpClient`:
- **Single-page, no email filter**: one page with orders A (prefix matches) and B (no match) — assert only A is returned.
- **Multi-page pagination**: two pages, prefix-matching order is on page 2 — assert it is found and pagination stops correctly.
- **Email filter active**: one page with two orders matching the prefix, one with matching email and one without — assert only the matching-email order is returned.
- **Empty result**: paginator returns `PageCount = 0` or `PageCount = 1` with no matching orders — assert empty list returned without error.

Effort: ~1–2 hours.

---
_Filed by weekly coverage-gap routine on 2026-10-12. Based on CI run #37593051210 (01ae6b757d0ee6facb6b3170eaba26b420c59288)._
