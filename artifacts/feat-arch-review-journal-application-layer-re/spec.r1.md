# Specification: Move Journal Repository Registrations to Persistence Layer

## Summary
Move the concrete repository DI bindings (`IJournalRepository → JournalRepository`, `IJournalTagRepository → JournalTagRepository`) out of `Anela.Heblo.Application/Features/Journal/JournalModule.cs` and into `Anela.Heblo.Persistence/PersistenceModule.cs`. This restores Clean Architecture's dependency rule for the Journal slice (the Application layer should not reference concrete Persistence types) and aligns Journal with how every other module wires its repositories.

## Background
Daily architectural review on 2026-06-04 flagged that `JournalModule.AddJournalModule` binds two domain repository interfaces to their EF Core implementations directly inside the Application project:

```csharp
// backend/src/Anela.Heblo.Application/Features/Journal/JournalModule.cs
using Anela.Heblo.Persistence.Catalog.Journal;   // ← concrete persistence type
services.AddScoped<IJournalRepository, JournalRepository>();
services.AddScoped<IJournalTagRepository, JournalTagRepository>();
```

This is the only feature module in `Application` that registers concrete persistence types in its own DI extension. All other modules' repositories (Analytics, Bank, Stock, Background Jobs, KnowledgeBase, Meeting Tasks, Leaflet, Article, Grid Layouts, Data Quality, Feature Flags, Purchase, Packaging, Invoice Classification, Dashboard) are registered centrally in `Anela.Heblo.Persistence/PersistenceModule.cs`. Journal is the outlier.

Why it matters:
- **Layer violation** — the Application layer's source code knows the concrete `JournalRepository`/`JournalTagRepository` types in `Anela.Heblo.Persistence.Catalog.Journal`, which contradicts the inner-layer-doesn't-reference-outer-layer rule.
- **Inconsistency** — readers/maintainers expect all repository wiring in `PersistenceModule.cs`; the Journal exception is a cognitive trap.
- **Test isolation** — when removing the Persistence project reference from Application becomes feasible in the future, this binding is one of the blockers; addressing it is a prerequisite step.

Note: `Anela.Heblo.Application.csproj` still has a `ProjectReference` to `Anela.Heblo.Persistence.csproj` for reasons outside the scope of this change. This spec only removes the Journal-specific source-level coupling so that Journal matches the registration pattern used elsewhere.

## Functional Requirements

### FR-1: Move repository registrations to `PersistenceModule.cs`
Add the two Journal repository bindings to `PersistenceModule.AddPersistenceServices`, in a clearly labeled "Journal repositories" section consistent with the existing comment-grouped style:

```csharp
// Journal repositories
services.AddScoped<IJournalRepository, JournalRepository>();
services.AddScoped<IJournalTagRepository, JournalTagRepository>();
```

The bindings must use `Scoped` lifetime (matching the current Application-layer registration and consistent with the other repositories in `PersistenceModule.cs`).

The new `using` directives must be added in alphabetical order within their grouping to match the existing convention:
- `using Anela.Heblo.Domain.Features.Journal;` (for `IJournalRepository`, `IJournalTagRepository`)
- `using Anela.Heblo.Persistence.Catalog.Journal;` (for `JournalRepository`, `JournalTagRepository`)

**Acceptance criteria:**
- `PersistenceModule.cs` contains both `AddScoped` calls in the "Journal repositories" section.
- Both `using` directives are present.
- Service lifetime is `Scoped`.
- Section placement follows the existing comment-grouped ordering and includes a `// Journal repositories` header comment.

### FR-2: Remove persistence references from `JournalModule.cs`
Remove the two `AddScoped` lines and the `using Anela.Heblo.Persistence.Catalog.Journal;` directive from `Anela.Heblo.Application/Features/Journal/JournalModule.cs`.

After the change, the file must have **no references** to any type in the `Anela.Heblo.Persistence` namespace. The MediatR comment ("MediatR handlers are automatically registered by MediatR scan") must be preserved as it documents the rationale for the otherwise-empty body.

**Acceptance criteria:**
- `JournalModule.cs` does not import any `Anela.Heblo.Persistence.*` namespace.
- `JournalModule.cs` does not register `JournalRepository` or `JournalTagRepository`.
- `grep "Anela.Heblo.Persistence" backend/src/Anela.Heblo.Application/Features/Journal/JournalModule.cs` returns no matches.

### FR-3: Preserve the `AddJournalModule()` extension method
Keep the `AddJournalModule()` extension method and its call site in `ApplicationModule.AddApplicationServices` (line 81). The method becomes a no-op for now (its body contains only the MediatR documentation comment plus `return services;`), but the method is preserved because:
- Other feature modules also expose empty-or-near-empty `Add{Feature}Module()` extensions; removing only Journal's would be inconsistent.
- Future application-layer-only registrations (validators, mappers, behaviors) can be added without re-introducing the method.

**Acceptance criteria:**
- `AddJournalModule()` method still exists with the same signature.
- The call `services.AddJournalModule();` in `ApplicationModule.cs` remains unchanged.
- Solution builds with no unused-symbol warnings on `JournalModule`.

### FR-4: No behavioral change at runtime
The DI container's resolved instance for `IJournalRepository` and `IJournalTagRepository` must be unchanged: same concrete type, same lifetime, same constructor parameters. Both registrations move from one `IServiceCollection` extension to another, but `ApplicationModule.AddApplicationServices` and `PersistenceModule.AddPersistenceServices` are both called during composition root setup, so the runtime DI graph is identical.

**Acceptance criteria:**
- All existing Journal unit tests (`GetJournalEntryHandlerTests`, `SearchJournalEntriesHandlerTests`, `CreateJournalEntryHandlerTests`, `DeleteJournalEntryHandlerTests`, `JournalRepositoryIntegrationTests`) pass without modification.
- A DI resolution sanity check (manual or test) confirms `IJournalRepository` resolves to `JournalRepository` and `IJournalTagRepository` resolves to `JournalTagRepository`.
- No change to any Journal use case handler, controller, or repository implementation file.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact. This is a DI registration move; the service graph at runtime is identical.

### NFR-2: Security
No security surface change. No new secrets, endpoints, or data paths.

### NFR-3: Maintainability
After the change, repository registration follows a single consistent rule across the codebase: "All concrete repository bindings live in `PersistenceModule.cs`, grouped by feature with a comment header." Future developers searching for a Journal repository binding will find it in the same place as every other module's bindings.

### NFR-4: Build & format
`dotnet build` of the `backend` solution must succeed with zero new warnings. `dotnet format` must report no formatting changes on the two edited files after the change is committed.

## Data Model
No data-model changes. No entity, DbContext, migration, or schema is touched.

## API / Interface Design
No public API or HTTP contract changes. The change is internal to the composition root.

Affected files (exactly two):
- `backend/src/Anela.Heblo.Application/Features/Journal/JournalModule.cs` — remove two `AddScoped` calls and one `using`.
- `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` — add two `AddScoped` calls and one new `using` (the Domain `using` may already be present transitively, but should be added explicitly if missing).

## Dependencies
- `Anela.Heblo.Persistence.csproj` already references `Anela.Heblo.Domain.csproj`, so `PersistenceModule.cs` can resolve `IJournalRepository` and `IJournalTagRepository`.
- `ApplicationModule.AddApplicationServices` and `PersistenceModule.AddPersistenceServices` must both be called during host startup for the binding to take effect. (Already the case in the current composition root — no change needed.)

## Out of Scope
- Removing the `ProjectReference` from `Anela.Heblo.Application.csproj` to `Anela.Heblo.Persistence.csproj`. The project reference exists for other reasons not addressed here; auditing and removing it would require a separate, broader effort.
- Refactoring `JournalRepository` or `JournalTagRepository` implementations.
- Refactoring Journal use case handlers, DTOs, or controllers.
- Adding or changing any tests beyond confirming existing ones still pass.
- Reorganizing other feature module registrations or `PersistenceModule.cs` section ordering.
- Renaming or restructuring `JournalModule.cs` (e.g., deleting it because it became empty); see FR-3 for rationale.

## Open Questions
None.

## Status: COMPLETE