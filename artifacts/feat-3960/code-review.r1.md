## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

Reviewed diff: two added tests in `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs` (null `Price` excluded without detail fetch; empty list yields a single empty batch). Both match the behavior of `ShoptetApiInvoiceSource.GetAllAsync` (`Price?.CurrencyCode` null-safe filter; empty list loop). No production code changed.
