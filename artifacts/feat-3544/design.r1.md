# Design: Remove unused methods from IClassificationHistoryRepository

## Component Design

### `IClassificationHistoryRepository` (Anela.Heblo.Domain/Features/InvoiceClassification)
**Responsibility:** Domain-facing contract for persisting and querying `ClassificationHistory` records, exposing only the operations actually consumed by the InvoiceClassification slice.

**Interface after change:**
```csharp
namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public interface IClassificationHistoryRepository
{
    Task<ClassificationHistory> AddAsync(ClassificationHistory history);

    Task<(List<ClassificationHistory> Items, int TotalCount)> GetPagedHistoryAsync(
        int page = 1,
        int pageSize = 20,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        string? invoiceNumber = null,
        string? companyName = null);
}
```
`GetHistoryAsync(int skip = 0, int take = 50)` and `GetHistoryByInvoiceIdAsync(string abraInvoiceId)` are removed — no callers exist. `AddAsync` and `GetPagedHistoryAsync` are unchanged (signature, defaults, and behavior).

### `ClassificationHistoryRepository` (Anela.Heblo.Persistence/InvoiceClassification)
**Responsibility:** EF Core implementation of `IClassificationHistoryRepository` against `ApplicationDbContext.ClassificationHistory`.

**Change:** Delete the `GetHistoryAsync` and `GetHistoryByInvoiceIdAsync` method bodies. Retain `AddAsync` and `GetPagedHistoryAsync` byte-for-byte, including their existing use of `.Include(h => h.ClassificationRule)`, `.Where`, `.Skip`, `.Take`, `.CountAsync`, `.OrderByDescending`, `.ToListAsync`. No `using` directive changes — `Microsoft.EntityFrameworkCore` remains required by `GetPagedHistoryAsync`.

### Consumers (unchanged — no edits, listed for boundary clarity)
- `InvoiceClassificationService.cs` — calls `AddAsync` only; continues to compile and behave identically against the reduced interface.
- `GetClassificationHistoryHandler.cs` — calls `GetPagedHistoryAsync` only; continues to compile and behave identically.
- `InvoiceClassificationModule.cs` — DI registration `services.AddScoped<IClassificationHistoryRepository, ClassificationHistoryRepository>();` is unaffected by the reduced interface surface.
- `InvoiceClassificationServiceTests.cs` — mocks `IClassificationHistoryRepository` via Moq but only stubs `AddAsync`; the mock simply exposes less surface after the change, no test edit required.
- `ClassificationHistoryRepositoryTests.cs` — exercises only `GetPagedHistoryAsync` (and `AddAsync` for fixture setup); no test edit required.

### Boundary summary
This is a pure surface reduction on an internal (non-HTTP-facing) repository contract, scoped entirely to the `InvoiceClassification` vertical slice's Domain and Persistence layers. No component gains or loses a dependency; no new component is introduced.

## Data Schemas
No data schema changes. The `ClassificationHistory` EF Core entity, its mapping in `ApplicationDbContext`, and its relationship to `ClassificationRule` (`.Include(h => h.ClassificationRule)`) are unaffected — only two unused query methods against the existing model are removed. No database migration is required.

No API/HTTP request or response shapes change: neither removed method was ever reachable through `GetClassificationHistoryHandler` or any controller, so there is no MediatR contract, DTO, or frontend-facing payload to update.
