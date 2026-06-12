# Specification: Remove Dead Query Methods from `IIssuedInvoiceRepository`

## Summary
Remove five unused query methods (`FindBySyncStatusAsync`, `FindByInvoiceDateRangeAsync`, `FindByCustomerNameAsync`, `FindWithCriticalErrorsAsync`, `FindStaleInvoicesAsync`) from `IIssuedInvoiceRepository`, their EF Core implementations in `IssuedInvoiceRepository`, and the unit tests that exercise them. The methods have no production callers — all live filtering happens through `GetPaginatedAsync`. This is a YAGNI/ISP cleanup that shrinks the contract to what current use-cases actually need.

## Background
A daily architecture review (`brief.md`, 2026-06-09) identified that `IIssuedInvoiceRepository` exposes five "Find*" methods whose only callers are the repository's own xUnit test class. Production code paths (`GetIssuedInvoicesListHandler`, `GetIssuedInvoiceSyncStatsHandler`, `GetIssuedInvoiceDetailHandler`, `InvoiceImportService`, `InvoiceConsumptionSourceAdapter`) rely exclusively on `GetPaginatedAsync`, `GetSyncStatsAsync`, `GetByIdAsync`, `GetByIdWithSyncHistoryAsync`, and `GetHeadersByDateAsync`.

Keeping the speculative API on the interface:
- **Violates YAGNI** — surface area without a real consumer.
- **Violates ISP** — any future test double or alternative implementation (e.g. a fake/mock for handler tests) must stub five methods nobody calls.
- **Hurts readability** — future maintainers cannot distinguish domain-driven query methods from leftovers when reading the interface.

The fix is non-functional: removing dead code on the interface and its implementation does not change observable system behavior. If a real use-case appears later, the method can be re-added on demand as a feature-driven addition.

## Functional Requirements

### FR-1: Remove unused query methods from the interface
Delete the five unused method declarations from `backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IIssuedInvoiceRepository.cs`:
- `FindBySyncStatusAsync` (line 16)
- `FindByInvoiceDateRangeAsync` (line 21)
- `FindByCustomerNameAsync` (line 26)
- `FindWithCriticalErrorsAsync` (line 31)
- `FindStaleInvoicesAsync` (line 41)

Their preceding XML doc-comment blocks must be removed together with the signatures. The remaining members on the interface — `GetByIdWithSyncHistoryAsync`, `GetSyncStatsAsync`, `GetPaginatedAsync`, `GetHeadersByDateAsync`, and inherited `IRepository<IssuedInvoice, string>` members — stay unchanged.

**Acceptance criteria:**
- `IIssuedInvoiceRepository.cs` no longer contains the five method declarations or their XML doc-comments.
- File compiles (`dotnet build` succeeds for the `Anela.Heblo.Application` project).
- A textual search across `backend/src/` and `backend/test/` for each removed method name returns zero matches.

### FR-2: Remove the matching implementations from the EF Core repository
Delete the five corresponding implementations from `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/IssuedInvoiceRepository.cs`:
- `FindBySyncStatusAsync` (lines 37–49)
- `FindByInvoiceDateRangeAsync` (lines 51–57)
- `FindByCustomerNameAsync` (lines 59–72)
- `FindWithCriticalErrorsAsync` (lines 74–80)
- `FindStaleInvoicesAsync` (lines 82–88)

All other repository members — `GetByIdAsync`, `GetByIdWithSyncHistoryAsync`, `GetSyncStatsAsync`, `AddAsync`, `UpdateAsync`, `GetPaginatedAsync`, `GetHeadersByDateAsync`, `ApplySorting` — must remain unchanged. The class must still implement `IIssuedInvoiceRepository`.

**Acceptance criteria:**
- `IssuedInvoiceRepository.cs` no longer contains the five method bodies.
- File compiles and the class still satisfies its interface (verified by `dotnet build`).
- Remaining public/override members compile and behave identically (no edits to retained methods).
- No new `using` directives become unused; remove `using` lines that are no longer referenced after deletion. Run `dotnet format` to confirm cleanup.

### FR-3: Remove the tests covering the deleted methods
Delete the test methods that exercise the removed repository APIs from `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs`:
- `FindBySyncStatusAsync_WithSyncedFilter_ReturnsOnlySynced` (line 120)
- `FindBySyncStatusAsync_WithNullFilter_ReturnsAll` (line 145)
- `FindByInvoiceDateRangeAsync_WithDateRange_ReturnsFilteredInvoices` (line 167)
- `FindWithCriticalErrorsAsync_WithErrorTypes_ReturnsOnlyCriticalErrors` (line 190)
- `FindStaleInvoicesAsync_WithStaleInvoices_ReturnsUnsyncedAndOldSynced` (line 217)
- `FindByCustomerNameAsync_WithPartialName_ReturnsMatchingInvoices` (line 286)
- `FindByCustomerNameAsync_WithEmptyName_ReturnsEmpty` (line 312)

Their `[Fact]` attributes and any arrange-only helper code that exists solely to support these tests (i.e. is not referenced by any retained test method) must be removed together.

**Acceptance criteria:**
- The seven test methods are gone from `IssuedInvoiceRepositoryTests.cs`.
- All remaining tests in that file still compile and pass (`dotnet test --filter "FullyQualifiedName~IssuedInvoiceRepositoryTests"`).
- The whole backend test suite passes (`dotnet test`).
- Test coverage for `IssuedInvoiceRepository` does not drop for the retained methods; only the deleted-method coverage disappears.

### FR-4: Preserve all retained behavior
The change must be a pure removal. No edits to:
- `GetIssuedInvoicesListHandler` or any other handler.
- `IssuedInvoiceFilters`, `PaginatedResult<T>`, or any DTO.
- `InvoicesModule.cs` DI registrations.
- `GetPaginatedAsync`, `GetSyncStatsAsync`, `GetByIdAsync`, `GetByIdWithSyncHistoryAsync`, `GetHeadersByDateAsync`, `AddAsync`, `UpdateAsync`, or `ApplySorting`.
- Any frontend code, OpenAPI contract, or generated client (the removed methods are repository-internal — they have no controller or DTO surface).

**Acceptance criteria:**
- `git diff` after the change shows only edits to the three files listed in FR-1, FR-2, FR-3.
- No regenerated OpenAPI/TypeScript-client artifacts appear in the diff.
- `dotnet build` and `dotnet format` are clean.
- All pre-existing tests (unit + integration) pass.

## Non-Functional Requirements

### NFR-1: Build & static-analysis cleanliness
After the change:
- `dotnet build` must succeed for the entire solution with no new warnings.
- `dotnet format --verify-no-changes` must report no formatting drift.
- Nullable-reference-type analysis must remain clean (no new `CS86xx` warnings introduced).

### NFR-2: Test stability
- `dotnet test` for the `Anela.Heblo.Tests` project must pass with the same green status as before the change, minus the seven deleted test cases.
- No remaining test must depend on the deleted methods, directly or transitively.

### NFR-3: Reversibility
The change is purely additive in reverse — re-adding any removed method later requires only restoring the declaration, body, and (optionally) test. Therefore no migration, feature flag, or deprecation window is needed.

### NFR-4: Surgical diff
Per project guideline "Surgical changes": touch only the three files identified. Do not reformat, rename, or refactor adjacent code, even if tempted.

## Data Model
No data-model changes. The `IssuedInvoice` entity, `IssuedInvoiceSyncStats`, `IssuedInvoiceFilters`, `PaginatedResult<T>`, and the EF Core `ApplicationDbContext` schema are untouched. No migration is required.

## API / Interface Design

### Before
```csharp
public interface IIssuedInvoiceRepository : IRepository<IssuedInvoice, string>
{
    Task<IEnumerable<IssuedInvoice>> FindBySyncStatusAsync(bool? isSynced, CancellationToken ct = default);
    Task<IEnumerable<IssuedInvoice>> FindByInvoiceDateRangeAsync(DateTime fromDate, DateTime toDate, CancellationToken ct = default);
    Task<IEnumerable<IssuedInvoice>> FindByCustomerNameAsync(string customerName, CancellationToken ct = default);
    Task<IEnumerable<IssuedInvoice>> FindWithCriticalErrorsAsync(CancellationToken ct = default);
    Task<IssuedInvoice?> GetByIdWithSyncHistoryAsync(string id, CancellationToken ct = default);
    Task<IEnumerable<IssuedInvoice>> FindStaleInvoicesAsync(DateTime beforeDate, CancellationToken ct = default);
    Task<IssuedInvoiceSyncStats> GetSyncStatsAsync(DateTime fromDate, DateTime toDate, CancellationToken ct = default);
    Task<PaginatedResult<IssuedInvoice>> GetPaginatedAsync(IssuedInvoiceFilters filters, CancellationToken ct = default);
    Task<IEnumerable<IssuedInvoice>> GetHeadersByDateAsync(DateOnly date, CancellationToken ct = default);
}
```

### After
```csharp
public interface IIssuedInvoiceRepository : IRepository<IssuedInvoice, string>
{
    Task<IssuedInvoice?> GetByIdWithSyncHistoryAsync(string id, CancellationToken ct = default);
    Task<IssuedInvoiceSyncStats> GetSyncStatsAsync(DateTime fromDate, DateTime toDate, CancellationToken ct = default);
    Task<PaginatedResult<IssuedInvoice>> GetPaginatedAsync(IssuedInvoiceFilters filters, CancellationToken ct = default);
    Task<IEnumerable<IssuedInvoice>> GetHeadersByDateAsync(DateOnly date, CancellationToken ct = default);
}
```

No public HTTP endpoint, MediatR contract, or MVC controller signature changes. No UI flow changes.

## Dependencies
None new. The change touches only:
- `Anela.Heblo.Application` project (interface + implementation, same assembly per the post-relocation Clean Architecture layout).
- `Anela.Heblo.Tests` project (one test file).

No external services, NuGet packages, configuration values, secrets, environment variables, or Azure Key Vault entries are affected.

## Out of Scope
- Adding new repository methods or expanding `GetPaginatedAsync` filtering.
- Refactoring `IssuedInvoiceFilters` or `PaginatedResult<T>`.
- Touching other repositories (`IssuedInvoiceRepository` only).
- Editing handlers, services, adapters, or the DI module.
- Regenerating or modifying the OpenAPI client.
- Database schema or EF Core migrations.
- Frontend changes.
- Replacing other "Find*" methods elsewhere in the codebase.
- Re-formatting `IssuedInvoiceRepositoryTests.cs` beyond removing the seven test methods.

## Open Questions
None.

## Status: COMPLETE