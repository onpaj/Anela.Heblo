## Module
Journal

## Finding
`PagedResult<T>` is declared as a `public class` inside `IJournalRepository.cs` at line 26, placing it in the `Anela.Heblo.Domain.Features.Journal` namespace:

```
backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs:26–32
```

The Marketing module's repository interface imports this type directly:

```
backend/src/Anela.Heblo.Domain/Features/Marketing/IMarketingActionRepository.cs:5
  using Anela.Heblo.Domain.Features.Journal;

IMarketingActionRepository.cs:14
  Task<PagedResult<MarketingAction>> GetPagedAsync(...)
```

This creates a hard domain-level dependency from the Marketing module to the Journal module to consume a generic pagination utility.

## Why it matters
The guidelines explicitly forbid direct access to another module's domain types. `PagedResult<T>` is a cross-cutting infrastructure type with no business meaning specific to Journal. Any future change to the Journal namespace (rename, extraction to microservice) silently breaks Marketing. The dependency is non-obvious because the type name gives no hint it lives in a different feature's domain.

## Suggested fix
Move `PagedResult<T>` to the shared Xcc layer, e.g. `Anela.Heblo.Xcc.Persistance.PagedResult<T>` (alongside `IRepository<T,TKey>`). Both Journal and Marketing then import from Xcc — no cross-module coupling.

---
_Filed by daily arch-review routine on 2026-05-12._
