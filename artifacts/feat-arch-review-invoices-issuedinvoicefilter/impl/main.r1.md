The `IssuedInvoiceRepository.cs` is confirmed in both commit `8b21c5ec` and the filesystem — the diff command simply didn't list it (possibly because it was also modified in a later commit and appeared as a single modification). Everything is properly committed. Let me write the final output summary.

---

# Implementation: Relocate `IssuedInvoiceFilters` and `PaginatedResult<T>` out of Domain

## What was implemented

A pure structural refactor — no behavior changes, no new API surface — that moves four types from incorrect architectural positions into Application:

| Type | From | To |
|---|---|---|
| `IssuedInvoiceFilters` | `Anela.Heblo.Domain.Features.Invoices` | `Anela.Heblo.Application.Features.Invoices.Contracts` |
| `PaginatedResult<T>` | `Anela.Heblo.Domain.Features.Invoices` | `Anela.Heblo.Application.Shared` |
| `IIssuedInvoiceRepository` | `Anela.Heblo.Domain.Features.Invoices` | `Anela.Heblo.Application.Features.Invoices.Contracts` |
| `IssuedInvoiceRepository` (impl) | `Anela.Heblo.Persistence.Features.Invoices` | `Anela.Heblo.Application.Features.Invoices.Infrastructure` |

`IssuedInvoiceSyncStats` was extracted from the multi-type Domain file into its own file (same namespace, no semantic change).

**Key deviation from plan:** The plan's additive phase (Tasks 1–5) assumed consumer handlers didn't already import `Application.Features.Invoices.Contracts`. They did — causing CS0104 ambiguity if Types 2+4 were added there while Domain copies existed. The fix was to atomically create the Application types AND remove the Domain `using` from the two affected handlers (`GetIssuedInvoicesListHandler`, `GetIssuedInvoiceDetailHandler`) in a single commit.

## Files created/modified

**New files:**
- `backend/src/Anela.Heblo.Application/Shared/PaginatedResult.cs` — generic pagination envelope
- `backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IssuedInvoiceFilters.cs` — filter/sort/page parameters
- `backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IIssuedInvoiceRepository.cs` — application-owned repository contract
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/IssuedInvoiceRepository.cs` — EF Core implementation (behavior-identical to Persistence original)
- `backend/src/Anela.Heblo.Domain/Features/Invoices/IssuedInvoiceSyncStats.cs` — extracted from multi-type file

**Deleted files:**
- `backend/src/Anela.Heblo.Domain/Features/Invoices/IIssuedInvoiceRepository.cs`
- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs`

**Modified (using directives only):** `InvoicesModule.cs`, four handler files, `InvoiceConsumptionSourceAdapter.cs`, `InvoiceImportService.cs`, four test files.

**Extended:** `ModuleBoundariesTests.cs` — new `Domain_must_not_reference_Application_and_relocated_invoice_types_must_be_gone` `[Fact]` (NFR-6 guard).

## Tests

- All 71 invoice-related tests pass (pagination, handler, repository, service, adapter)
- NFR-6 architectural guard test passes (1 test, reflection-based)
- 4269 total tests pass; 32 Docker/TestContainers integration tests fail as pre-existing infrastructure failures

## How to verify

```bash
# Build
dotnet build Anela.Heblo.sln

# Full test suite
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj

# NFR-3 Invoices Domain grep gate (expect: zero matches)
grep -rnwE "(PageNumber|PageSize|SortBy|TotalPages|HasNextPage|HasPreviousPage|ShowOnlyUnsynced|ShowOnlyWithErrors)" backend/src/Anela.Heblo.Domain/Features/Invoices/

# NFR-6 architectural test
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Domain_must_not_reference_Application_and_relocated_invoice_types_must_be_gone"
```

## Notes

- The Domain project's `IIssuedInvoiceRepository.cs` file is now deleted; `IssuedInvoiceSyncStats` lives in its own `IssuedInvoiceSyncStats.cs` file in Domain (correct — it's a domain-meaningful value type, not a pagination concern).
- Pre-existing CS8601/CS8602 nullable warnings in Manufacture handlers are unrelated to this change.
- Marketing (`MarketingActionQueryCriteria`) and Manufacture (`ManufacturedInventoryFilter`) Domain files still have `PageNumber`/`PageSize` — pre-existing similar boundary smells, explicitly out of scope per spec.
- All qualified names (`Application.Shared.PaginatedResult`, `Contracts.IIssuedInvoiceRepository`) used during the migration phase were cleaned up in Phase 4 after the Domain copies were deleted.

## PR Summary

Restores Clean Architecture boundaries by relocating three application-level types out of the Domain project and one implementation out of Persistence. `IssuedInvoiceFilters` (pagination/sort/UI flags), `PaginatedResult<T>` (generic pagination envelope), and `IIssuedInvoiceRepository` (application-shaped contract) move to `Application/Features/Invoices/Contracts/` and `Application/Shared/`. The EF Core implementation moves from `Persistence/Invoices/` to `Application/Features/Invoices/Infrastructure/` — matching the existing `Application → Persistence` project reference direction in this codebase (Persistence does not reference Application). No behavior changes, no API surface changes, no migration required.

### Changes
- `Application/Shared/PaginatedResult.cs` — generic pagination envelope, reusable across features
- `Application/Features/Invoices/Contracts/IssuedInvoiceFilters.cs` — filter/sort/page parameters (was in Domain)
- `Application/Features/Invoices/Contracts/IIssuedInvoiceRepository.cs` — repository contract (was in Domain)
- `Application/Features/Invoices/Infrastructure/IssuedInvoiceRepository.cs` — EF Core implementation (was in Persistence)
- `Domain/Features/Invoices/IssuedInvoiceSyncStats.cs` — extracted from the multi-type Domain file into its own file
- `Domain/Features/Invoices/IIssuedInvoiceRepository.cs` — deleted (all three types migrated out)
- `Persistence/Invoices/IssuedInvoiceRepository.cs` — deleted (implementation migrated to Application)
- Consumer handlers, services, adapters, DI module, tests — `using` directive updates only
- `Tests/Architecture/ModuleBoundariesTests.cs` — NFR-6 guard: enforces Domain has no Application references and the three relocated type names are absent from Domain assembly

## Status
DONE