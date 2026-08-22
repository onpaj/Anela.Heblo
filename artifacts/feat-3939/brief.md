## Module / File
`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/IssuedInvoices/ShoptetApiInvoiceSource.cs`

## Coverage
Line coverage: 18.4% (filter threshold: 60%)

## What's not tested
1. **QueryByInvoice single-fetch path** — when `query.QueryByInvoice == true`, the source fetches a single invoice directly. The branch is never exercised, including the sub-case where the single fetch returns null (should produce an empty invoice list, not a null-reference exception).
2. **In-memory currency filtering** — after fetching the full invoice list, the source filters by currency code using a case-insensitive comparison. No test verifies that invoices with a non-matching currency are excluded, or that the comparison is truly case-insensitive.
3. **Null individual detail guard** — inside the detail-fetch loop, `if (detail != null)` silently drops invoices where `GetInvoiceAsync` returns null. No test verifies that a null response for one code does not abort the batch and is excluded from the result set.

## Why it matters
The in-memory currency filter is the only place where multi-currency Shoptet stores are prevented from mixing EUR and CZK invoices in the same import batch. If the filter is wrong or removed, invoices of the wrong currency are imported and create duplicate or mis-attributed accounting records. The null guard prevents silent data loss of individual invoices — without a test, removing it would cause a NullReferenceException mid-batch, aborting the entire import.

## Suggested approach
Unit test with a mocked `IShoptetInvoiceClient` and a real `ShoptetInvoiceMapper`:
- Case: QueryByInvoice == true, client returns an invoice → batch contains that one mapped invoice
- Case: QueryByInvoice == true, client returns null → batch contains empty invoice list
- Case: list returns mixed-currency items → only those matching `query.Currency` (case-insensitive) are fetched individually
- Case: individual GetInvoiceAsync returns null → that code is excluded from the batch, others are included
~1.5 h effort.

---
_Filed by weekly coverage-gap routine on 2026-08-17. Based on CI run #31804633307 (6f781d410eb84616c8decb088d6d18cd1de01fb8)._
