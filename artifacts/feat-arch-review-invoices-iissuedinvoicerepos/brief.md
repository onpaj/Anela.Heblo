## Module
Invoices

## Finding
`IIssuedInvoiceRepository` (`Application/Features/Invoices/Contracts/IIssuedInvoiceRepository.cs`) declares five methods that have **no callers in production code**:

| Method | Lines |
|--------|-------|
| `FindBySyncStatusAsync` | line 16 |
| `FindByInvoiceDateRangeAsync` | line 21 |
| `FindByCustomerNameAsync` | line 26 |
| `FindWithCriticalErrorsAsync` | line 31 |
| `FindStaleInvoicesAsync` | line 41 |

The only files that call these methods are in `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs`. No handler, service, or adapter in the production tree uses them.

All live filtering is handled through `GetPaginatedAsync` (used by `GetIssuedInvoicesListHandler`) and purpose-specific methods (`GetSyncStatsAsync`, `GetByIdWithSyncHistoryAsync`, `GetHeadersByDateAsync`) that each have a production caller.

## Why it matters
- **YAGNI**: the interface contract exposes capabilities no current feature needs.
- **ISP**: any future test double (mock or fake) for `IIssuedInvoiceRepository` must stub five dead methods, adding noise and coupling to the test setup.
- **Readability**: future maintainers can't tell which query methods are actually part of the domain use-cases and which are speculative leftovers.

## Suggested fix
Remove the five methods from `IIssuedInvoiceRepository` and their corresponding implementations in `IssuedInvoiceRepository.cs` (lines 37–88). Remove the test cases that exercise them in `IssuedInvoiceRepositoryTests.cs`. If a future use-case genuinely needs one of these queries it can be re-added at that point, as a concrete driven-by-feature addition.

---
_Filed by daily arch-review routine on 2026-06-09._