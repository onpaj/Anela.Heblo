# Design: DqtJobBase extraction

UX/UI: not applicable (Skip Design: true).

## Component boundaries
- `DqtJobBase` (protected ctor: `IDqtRunRepository`, `IRecurringJobStatusChecker`, `TimeProvider`, `ILogger`); members: `abstract Metadata`, `protected abstract DqtTestType TestType`, `protected abstract DateOnly GetTargetDate(DateOnly today)`, `protected abstract Task RunAsync(Guid runId, CancellationToken ct)`, `public async Task ExecuteAsync(CancellationToken)`.
- Concrete jobs: same public ctor signatures; call `base(repository, statusChecker, timeProvider, logger)`; store only their runner field.

## Per-job mapping
| Job | TestType | Date | Runner |
|---|---|---|---|
| InvoiceDqtJob | IssuedInvoiceComparison | yesterday | IInvoiceDqtJobRunner |
| ProductPairingDqtJob | ProductPairing | today | IDriftDqtJobRunner |
| StockWriteBackDqtJob | StockWriteBackReconciliation | yesterday | IDriftDqtJobRunner |
| LotStockReconciliationDqtJob | LotSumVsErpStock | today | IDriftDqtJobRunner |
| PriceComparisonDqtJob | PriceComparison | today | IDriftDqtJobRunner |

## Data schemas
None changed.
