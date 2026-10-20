# Review: add-residual-tests r1

Both required tests present in `ShoptetApiInvoiceSourceTests.cs`, follow existing helpers (BuildDto/BuildSource), assertions match task spec (Price=null filtered with no detail call; empty list -> one batch, BatchId == RequestId, empty invoices, zero GetInvoiceAsync calls). Suite passes 8/8; test-only change, no production edits.

**Status:** PASS
