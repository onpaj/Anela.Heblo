# PR Context

- **PR**: #3212 — fix(invoices): move IIssuedInvoiceRepository and IssuedInvoiceFilters to Domain layer (#3155)
- **URL**: https://github.com/onpaj/Anela.Heblo/pull/3212
- **Branch**: `feature/3155-fix-invoice-repo-interface-layer` → `main`
- **State**: OPEN
- **Author**: onpaj
- **Changes**: +9 / -31 across 12 files
- **Absorbed**: backmerged with `main`, all tests passing

## Description

### What the issue was

`IIssuedInvoiceRepository` and `IssuedInvoiceFilters` were defined in the **Persistence** project (`Anela.Heblo.Persistence.Invoices`), violating Clean Architecture's Dependency Rule. Every Application-layer handler that queried invoices had to import `using Anela.Heblo.Persistence.Invoices`, creating an upward dependency from Application to Infrastructure. This also made it impossible to test Application handlers without the Persistence project.

### How it was fixed

Pure file/namespace relocation — no logic changes:

1. Moved `IIssuedInvoiceRepository.cs` from `Persistence/Invoices/` to `Domain/Features/Invoices/` (namespace: `Anela.Heblo.Domain.Features.Invoices`)
2. Moved `IssuedInvoiceFilters.cs` from `Persistence/Invoices/` to `Domain/Features/Invoices/` (same namespace change)
3. Updated `using` directives in 6 Application files to reference the Domain namespace instead of Persistence
4. Updated `using` directives in 4 test files accordingly
5. `IssuedInvoiceRepository` (the implementation) stays in Persistence — only the interface and filter type move

All 76 invoice unit tests pass.

Closes #3155
