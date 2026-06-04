## Module
Journal

## Finding
`JournalModule.cs` in the Application layer directly imports and registers concrete Persistence implementations:

```csharp
// backend/src/Anela.Heblo.Application/Features/Journal/JournalModule.cs
using Anela.Heblo.Persistence.Catalog.Journal;   // ← Application depends on Persistence

public static IServiceCollection AddJournalModule(this IServiceCollection services)
{
    services.AddScoped<IJournalRepository, JournalRepository>();      // concrete Persistence type
    services.AddScoped<IJournalTagRepository, JournalTagRepository>(); // concrete Persistence type
    ...
}
```

The Application layer must not know about Persistence (Infrastructure). Binding of concrete repository implementations to domain interfaces belongs in `PersistenceModule.cs`.

## Why it matters
Clean Architecture's dependency rule requires that inner layers (Application) do not reference outer layers (Persistence/Infrastructure). This coupling:
- Prevents the Application project from being tested in isolation from EF Core / the database
- Adds a project reference that contradicts the layer hierarchy (`Application` → `Persistence`)
- Is inconsistent with other modules, where concrete repositories are wired in `PersistenceModule.cs`

## Suggested fix
Move the two `AddScoped` bindings from `JournalModule.AddJournalModule` into `PersistenceModule.cs` (alongside the other repository registrations), and remove the `using Anela.Heblo.Persistence.Catalog.Journal;` import from `JournalModule.cs`.

---
_Filed by daily arch-review routine on 2026-06-04._