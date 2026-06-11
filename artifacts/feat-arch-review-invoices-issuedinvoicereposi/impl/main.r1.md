# Implementation: Relocate IssuedInvoiceRepository to Persistence Layer

## What was implemented

Relocated `IssuedInvoiceRepository` and its supporting types from `Anela.Heblo.Application` to `Anela.Heblo.Persistence.Invoices`, restoring the intended Clean Architecture dependency direction. The Application assembly no longer contains EF Core query code for Invoices. Four commits on the feature branch cover the change in safe, independently-verifiable increments.

## Files created/modified

### Created
- `backend/src/Anela.Heblo.Xcc/Persistance/PaginatedResult.cs` — generic paginated result container (namespace `Anela.Heblo.Xcc.Persistance`)
- `backend/src/Anela.Heblo.Persistence/Invoices/IIssuedInvoiceRepository.cs` — repository interface (moved from Application/Contracts)
- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceFilters.cs` — filter parameter object (moved from Application/Contracts)
- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs` — EF-backed implementation (moved from Application/Infrastructure)

### Deleted
- `backend/src/Anela.Heblo.Application/Shared/PaginatedResult.cs`
- `backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IIssuedInvoiceRepository.cs`
- `backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IssuedInvoiceFilters.cs`
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/IssuedInvoiceRepository.cs`

### Modified (using directives only)
- `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs`
- `backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs`
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceConsumptionSourceAdapter.cs`
- `backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetIssuedInvoicesList/GetIssuedInvoicesListHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetIssuedInvoiceDetail/GetIssuedInvoiceDetailHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetIssuedInvoiceSyncStats/GetIssuedInvoiceSyncStatsHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoicesListHandlerPaginationTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportServiceTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceConsumptionSourceAdapterTests.cs`

## Tests

All tests covering the changed code were exercised:
- `IssuedInvoiceRepositoryTests.cs` — 17 tests covering EF Core repository methods (all pass)
- `GetIssuedInvoicesListHandlerPaginationTests.cs` — pagination handler tests (pass)
- `GetIssuedInvoiceDetailHandlerTests.cs` — detail handler tests (pass)
- `InvoiceImportServiceTests.cs` — import service tests (pass)
- `InvoiceConsumptionSourceAdapterTests.cs` — adapter tests (pass)
- Architecture tests in `ModuleBoundariesTests.cs` — including `Domain_must_not_reference_Application_and_relocated_invoice_types_must_be_gone` (pass)
- Full suite: 4,877 tests pass; 38 pre-existing Docker/testcontainers failures unrelated to this change

## How to verify

```bash
# Build
dotnet build backend/Anela.Heblo.sln

# Format check
dotnet format backend/Anela.Heblo.sln --verify-no-changes

# Invoice + Architecture tests
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Invoices|FullyQualifiedName~Architecture" \
  --logger "console;verbosity=minimal"

# Full suite
dotnet test backend/Anela.Heblo.sln --logger "console;verbosity=minimal"

# Confirm relocated files exist
find backend/src/Anela.Heblo.Persistence/Invoices backend/src/Anela.Heblo.Xcc/Persistance \
  -type f -name "*.cs" | sort

# Confirm no Application.Shared.PaginatedResult references remain
grep -rn "Application\.Shared.*PaginatedResult" backend/ --include="*.cs"
```

## Notes

**Plan deviation: IIssuedInvoiceRepository and IssuedInvoiceFilters placed in Persistence, not Domain.** The architecture review proposed moving these to `Anela.Heblo.Domain`. The plan correctly overrides this because an existing architecture test (`Domain_must_not_reference_Application_and_relocated_invoice_types_must_be_gone`, introduced in PR #2329) explicitly asserts these types must NOT live in Domain. Co-locating the interface and filters with the implementation in `Anela.Heblo.Persistence.Invoices` is the only placement that satisfies both the architecture test and the goal of removing EF Core from the Application assembly.

**Spec FR-3 dropped.** The `Application → Persistence` project reference is NOT removed — it is load-bearing for `InvoicesModule.cs` DI wiring (per ADR-004), matching how every other module (`PackingMaterialsModule`, `ArticleModule`, etc.) binds repositories.

**Task 1 ambiguity handling.** When `PaginatedResult<T>` was added to Xcc.Persistance, `IIssuedInvoiceRepository.cs` (which already imported both `Application.Shared` and `Xcc.Persistance`) produced a CS0104 ambiguity. The implementer temporarily qualified the FQN; Task 2 resolved it cleanly by removing the `Application.Shared` import.

## PR Summary

Relocates `IssuedInvoiceRepository` from the Application layer to `Anela.Heblo.Persistence.Invoices`, co-located with the existing EF configurations. Moves `IIssuedInvoiceRepository`, `IssuedInvoiceFilters`, and `PaginatedResult<T>` to their architecturally correct homes. The Application assembly no longer contains EF Core query code; the compile-time graph is improved without changing any runtime behaviour.

The architecture review proposed placing the interface and filters in `Anela.Heblo.Domain`. That violates the existing architecture test `Domain_must_not_reference_Application_and_relocated_invoice_types_must_be_gone` (added in PR #2329, which deliberately moved these types out of Domain). Co-locating interface, filter, and implementation in `Anela.Heblo.Persistence.Invoices` keeps the architecture test green. Spec FR-3 (remove `Application → Persistence` reference) was dropped: the reference is required by ADR-004 for `InvoicesModule.cs` DI wiring, matching all other modules.

### Changes
- `backend/src/Anela.Heblo.Xcc/Persistance/PaginatedResult.cs` — added (was Application/Shared)
- `backend/src/Anela.Heblo.Persistence/Invoices/IIssuedInvoiceRepository.cs` — added (was Application/Contracts)
- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceFilters.cs` — added (was Application/Contracts)
- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs` — added (was Application/Infrastructure)
- Four old Application-layer files deleted
- ~11 consumer files updated (using directives only, no logic changes)

## Status
DONE
