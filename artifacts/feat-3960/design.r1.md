# Design: ShoptetApiInvoiceSource test coverage

## Component Design
Test class `ShoptetApiInvoiceSourceTests` (xUnit) in `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/`. Existing tests cover: single found, single null, currency exclusion, case-insensitive match (theory), null-detail skip. Candidate additions only if coverage shows gaps: list item with null `Price` is excluded; empty list returns one batch with no invoices and does not call `GetInvoiceAsync`; `BatchId` equals `RequestId` in list mode.

## Data Schemas
None.
