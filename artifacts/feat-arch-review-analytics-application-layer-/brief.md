## Module
Analytics

## Finding

`backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`, line 41:
```xml
<ProjectReference Include="../Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj" />
```

`backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs`, lines 5–19:
```csharp
using Anela.Heblo.Persistence;
...
public class AnalyticsRepository : IAnalyticsRepository
{
    private readonly IAnalyticsProductSource _productSource;
    private readonly ApplicationDbContext _dbContext;

    public AnalyticsRepository(IAnalyticsProductSource productSource, ApplicationDbContext dbContext)
```

`AnalyticsRepository` lives in `Anela.Heblo.Application` and injects `ApplicationDbContext` directly from `Anela.Heblo.Persistence`. This requires `Anela.Heblo.Application.csproj` to carry a `ProjectReference` to `Anela.Heblo.Persistence`.

## Why it matters

Clean Architecture mandates that the Application layer must NOT depend on the Infrastructure/Persistence layer. The dependency must run inward (Persistence → Application → Domain), never outward. With this reference in place:

- The Application layer can bypass all repository abstractions and query `ApplicationDbContext` directly (the `GetInvoiceImportStatisticsAsync` and `GetBankStatementImportStatisticsAsync` methods already do so — lines 115–185 and 213–283).
- Future module splitting or microservice extraction from the Application layer will pull in EF Core and the full Persistence project.
- Every feature module in Application now has implicit access to the entire `ApplicationDbContext`, violating module isolation.

Per `docs/architecture/development_guidelines.md` ("Phase 1" persistence guidelines): the generic repository is in Xcc with **implementation in the Persistence layer**. The Analytics repository implementation belongs in `Anela.Heblo.Persistence`, not `Anela.Heblo.Application`.

## Suggested fix

Move `AnalyticsRepository` to `backend/src/Anela.Heblo.Persistence/Analytics/AnalyticsRepository.cs` (alongside other persistence-layer repositories). Keep `IAnalyticsRepository` in `Application/Features/Analytics/Infrastructure/` (the Application layer's abstraction). Remove the `ProjectReference` to `Anela.Heblo.Persistence` from `Anela.Heblo.Application.csproj`. Register the binding in `PersistenceModule.cs` (which already lives in the Persistence layer).

---
_Filed by daily arch-review routine on 2026-05-28._