# Design: Remove dead `result.Failed` write in MarketingInvoiceImportService catch block

## Component Design
No component boundaries change. `MarketingInvoiceImportService.ImportAsync` keeps its existing responsibility (stage transactions, flush once, return a `MarketingImportResult` on success or throw on flush failure) and its existing interface, `IMarketingInvoiceImportService`. The only change is inside the `catch` block wrapping the post-loop `_repository.SaveChangesAsync(ct)` call:

- Remove: `result.Failed += stagedCount;`
- Remove: `// result.Imported intentionally stays 0 — nothing was committed.`
- Keep unchanged: the `_logger.LogError(ex, "Failed to persist {Count} marketing transactions for {Platform}", stagedCount, source.Platform);` call and the unconditional `throw;`.

## Data Schemas
No schema, DTO, or contract changes. `MarketingImportResult` (`Imported`, `Skipped`, `Failed` counters) is unchanged in shape and semantics — only a dead write to `Failed` on an already-unreachable-by-caller path is removed.
