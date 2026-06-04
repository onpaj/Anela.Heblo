---

# Implementation: Move Journal Repository Registrations to Persistence Layer

## What was implemented

Moved `IJournalRepository → JournalRepository` and `IJournalTagRepository → JournalTagRepository` DI bindings from `JournalModule.cs` (Application layer) to `PersistenceModule.cs` (Persistence layer). `JournalModule` is now a no-op shell preserving the `AddJournalModule()` extension for symmetry with other feature modules.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Journal/JournalModule.cs` — removed two `AddScoped` calls and all three now-unused `using` directives; only `Microsoft.Extensions.DependencyInjection` remains; MediatR comment preserved
- `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` — added `// Journal repositories` block with two `AddScoped` calls at the end of `AddPersistenceServices`; added `using Anela.Heblo.Domain.Features.Journal;` and `using Anela.Heblo.Persistence.Catalog.Journal;`

## Tests

No test files modified. The spec confirms all existing Journal tests (`GetJournalEntryHandlerTests`, `SearchJournalEntriesHandlerTests`, `CreateJournalEntryHandlerTests`, `DeleteJournalEntryHandlerTests`, `JournalRepositoryIntegrationTests`) pass without modification — the DI graph is byte-identical at runtime.

## How to verify

```bash
cd backend
dotnet build          # must succeed with 0 errors
dotnet format --verify-no-changes   # must report no changes needed
grep "Anela.Heblo.Persistence" src/Anela.Heblo.Application/Features/Journal/JournalModule.cs  # must return no output
```

## Notes

- Build succeeded with zero errors (234 pre-existing warnings, unchanged)
- `dotnet format --verify-no-changes` confirmed no formatting issues
- Appended Journal section at the end of the repository block per the arch review's instruction to follow chronological (not alphabetical) ordering

## PR Summary

Moves `IJournalRepository` and `IJournalTagRepository` DI bindings from `JournalModule` in the Application layer to `PersistenceModule` in the Persistence layer, restoring Clean Architecture's dependency rule for the Journal slice and eliminating the one feature module that was importing concrete Persistence types into Application.

`JournalModule` becomes a no-op shell — `AddJournalModule()` is preserved for symmetry with the dozen other thin-shell feature extensions and as a future home for application-layer-only registrations (validators, pipeline behaviors, etc.). The runtime DI graph is unchanged: same concrete types, same `Scoped` lifetime, same constructor resolution.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Journal/JournalModule.cs` — removed two `AddScoped` bindings and all now-unused `using` directives; left no-op shell with MediatR comment
- `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` — added `// Journal repositories` block with two `AddScoped` calls; added two `using` directives

## Status
DONE