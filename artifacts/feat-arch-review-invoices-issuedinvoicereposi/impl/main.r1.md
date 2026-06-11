All tasks are complete. Here's a summary:

---

# Implementation: Relocate IssuedInvoiceRepository to Persistence Layer

## What was implemented

All four affected types moved from `Anela.Heblo.Application` into their architecturally correct locations:

| Type | From | To |
|---|---|---|
| `PaginatedResult<T>` | `Application/Shared` | `Xcc.Persistance` |
| `IIssuedInvoiceRepository` | `Application/Contracts` | `Persistence.Invoices` |
| `IssuedInvoiceFilters` | `Application/Contracts` | `Persistence.Invoices` |
| `IssuedInvoiceRepository` (impl) | `Application/Infrastructure` | `Persistence.Invoices` |

## Commits on this branch

```
489d3543 refactor(invoices): move IssuedInvoiceRepository implementation to Persistence.Invoices
67be7677 refactor(invoices): move IIssuedInvoiceRepository and IssuedInvoiceFilters to Persistence.Invoices
5cd72153 refactor(invoices): move PaginatedResult<T> consumers to Xcc and delete old copy
e27980e0 refactor(invoices): add PaginatedResult<T> to Xcc.Persistance and resolve ambiguity
```

## Verification results

- `dotnet build` — 0 errors ✓
- `dotnet format --verify-no-changes` — clean ✓
- Architecture test `Domain_must_not_reference_Application_and_relocated_invoice_types_must_be_gone` — passes ✓
- Invoice + Architecture tests — 78 + 24 tests pass ✓
- Full test suite — 4,877 pass (38 pre-existing Docker failures unrelated to this change) ✓

## Key deviations from spec

1. **Interface/Filters placed in Persistence, not Domain** — An existing architecture test (PR #2329) prohibits `IIssuedInvoiceRepository`, `IssuedInvoiceFilters`, and `PaginatedResult<T>` from living in Domain. Co-locating them with the implementation in `Persistence.Invoices` is the only placement that satisfies this constraint.
2. **Spec FR-3 dropped** — The `Application → Persistence` project reference is kept; it's load-bearing for `InvoicesModule.cs` DI wiring per ADR-004.