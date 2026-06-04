# Architecture Review: Move Journal Repository Registrations to Persistence Layer

## Skip Design: true

Backend-only DI registration move. No UI, no API contracts, no visual components.

## Architectural Fit Assessment

The proposed change **strengthens** an existing convention rather than introducing a new one. Verified against the codebase:

- `PersistenceModule.cs` (lines 133–177) already centralizes every other module's repository bindings using a "Comment header + group of `AddScoped` calls" style. Journal is the only outlier.
- `JournalModule.cs` (lines 4, 13–14) is the only Application-layer feature module that imports `Anela.Heblo.Persistence.*` and binds concrete persistence types.
- `Anela.Heblo.Domain/Features/Journal/` defines `IJournalRepository` and `IJournalTagRepository`; concrete `JournalRepository`/`JournalTagRepository` live in `Anela.Heblo.Persistence/Catalog/Journal/`. Both Domain interfaces are already reachable from the Persistence project (Persistence references Domain).
- `ApplicationModule.AddApplicationServices` (line 81) and `PersistenceModule.AddPersistenceServices` are both invoked at composition root, so moving the bindings has no runtime effect.

`docs/architecture/development_guidelines.md` reinforces "Generic repository abstraction in `Xcc`, implementation in Persistence layer" (ADR-002) and shows the canonical example registering repositories in a *module* extension. The codebase has diverged from that example toward central registration in `PersistenceModule.cs` — Journal is the leftover. The spec's direction matches today's actual convention, not the older documented example.

**Integration points:** zero new ones. Two source files change; the DI graph at runtime is byte-identical.

## Proposed Architecture

### Component Overview

```
┌───────────────────────────────────────────┐
│ Anela.Heblo.Domain                        │
│   Features/Journal/                       │
│     IJournalRepository      (interface)   │
│     IJournalTagRepository   (interface)   │
└──────────────▲────────────────────────────┘
               │ implements
               │
┌──────────────┴────────────────────────────┐
│ Anela.Heblo.Persistence                   │
│   Catalog/Journal/                        │
│     JournalRepository       (impl)        │
│     JournalTagRepository    (impl)        │
│   PersistenceModule.cs                    │
│     // Journal repositories               │
│     AddScoped<IJournal*, Journal*>()  ◄── │ (binding moves here)
└───────────────────────────────────────────┘
               ▲
               │ called from composition root
               │
┌──────────────┴────────────────────────────┐
│ Anela.Heblo.Application                   │
│   Features/Journal/                       │
│     JournalModule.cs                      │
│       (no Persistence imports)            │
│       (no concrete bindings)              │
└───────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Keep `JournalModule.AddJournalModule()` as a no-op shell
**Options considered:**
- (A) Delete `JournalModule.cs` and remove the `AddJournalModule()` call from `ApplicationModule.cs`.
- (B) Keep `AddJournalModule()` empty (only the MediatR comment + `return services;`).

**Chosen approach:** (B), per FR-3.

**Rationale:** Other feature modules (e.g., `MeetingTasksModule`, `LeafletModule`) follow the same "thin shell that may grow later" shape. Deleting only Journal's would create the same kind of inconsistency this refactor is meant to remove. The shell is the natural home for future validators, MediatR pipeline behaviors, or AutoMapper profiles specific to Journal — none of which belong in `PersistenceModule.cs`.

#### Decision 2: Do **not** remove `ProjectReference` from `Application.csproj` to `Persistence.csproj`
**Chosen approach:** Leave the project reference as-is (already declared out of scope in the spec).

**Rationale:** Removing the project reference is a much larger effort because the Application project still pulls Persistence types through other paths (verify with grep before scoping that work). Doing it now would expand the change beyond a low-risk move. This refactor removes the *source-level* coupling for Journal; the *assembly-level* coupling is a separate ticket.

#### Decision 3: Section placement inside `PersistenceModule.cs`
**Chosen approach:** Add a `// Journal repositories` block. Place it after `// Packaging repositories` (the current last entry, line 177) to minimize diff churn and preserve the natural append-style growth of the file. Do **not** alphabetize existing sections.

**Rationale:** FR-1 calls for "section placement follows the existing comment-grouped ordering." The existing ordering is *not* alphabetical (Analytics → Bank → Invoice Classification → Stock → Background Jobs → KnowledgeBase → … → Packaging) — it's chronological by feature introduction. Appending Journal at the end matches that convention and keeps the diff surgical.

## Implementation Guidance

### Directory / Module Structure

No new files, no moved files. Two existing files are edited:

```
backend/src/Anela.Heblo.Application/Features/Journal/JournalModule.cs   (edit: remove 2 lines + 1 using)
backend/src/Anela.Heblo.Persistence/PersistenceModule.cs                (edit: add 3 lines + verify usings)
```

Note: `Anela.Heblo.Application.csproj` (lines 51–52) lists `Features\Journal\Infrastructure\` and `Features\Journal\Model\` as `<Folder>` includes. These are empty placeholders unrelated to this refactor. **Do not touch them** — the spec's "surgical change" intent rules out unrelated cleanup.

### Interfaces and Contracts

No interface changes. `IJournalRepository` and `IJournalTagRepository` keep their signatures and locations in `Anela.Heblo.Domain.Features.Journal`.

### Data Flow

Composition root order (unchanged):

```
Program.cs
  └─ AddApplicationServices()
       ├─ AddJournalModule()           ← no longer registers repositories
       └─ … other modules
  └─ AddPersistenceServices()
       └─ Repositories section
             └─ Journal repositories   ← new home for bindings
```

At runtime, an MediatR handler that depends on `IJournalRepository` resolves to `JournalRepository` (Scoped) exactly as it does today. The order in which `AddApplicationServices` vs. `AddPersistenceServices` is invoked is **irrelevant** for these two bindings because neither replaces a prior registration and `IServiceCollection` is order-agnostic for first-wins-on-resolve scoped service lookup of distinct interfaces.

### Concrete edit recipe

`Anela.Heblo.Application/Features/Journal/JournalModule.cs` — after change:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Domain.Features.Journal;

namespace Anela.Heblo.Application.Features.Journal
{
    public static class JournalModule
    {
        public static IServiceCollection AddJournalModule(this IServiceCollection services)
        {
            // MediatR handlers are automatically registered by MediatR scan

            return services;
        }
    }
}
```

Verify the `Anela.Heblo.Application.Features.Journal.Contracts` and `Anela.Heblo.Domain.Features.Journal` usings are still needed after removal. If neither is referenced in the now-empty body, both must be removed too — otherwise `dotnet format` and analyzers will flag unused imports (NFR-4 requires zero warnings). Based on the current file content, **all three persistence-related elements come out (`using Anela.Heblo.Persistence.Catalog.Journal;` + two `AddScoped` lines), and the remaining two usings should also be removed since the body no longer references those namespaces**.

`Anela.Heblo.Persistence/PersistenceModule.cs` — append after line 177 (after Packaging repositories):

```csharp
        // Journal repositories
        services.AddScoped<IJournalRepository, JournalRepository>();
        services.AddScoped<IJournalTagRepository, JournalTagRepository>();
```

Add usings (alphabetize within the existing groupings):
- `using Anela.Heblo.Domain.Features.Journal;`
- `using Anela.Heblo.Persistence.Catalog.Journal;`

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Removing unused `using` directives in `JournalModule.cs` triggers `dotnet format` to do nothing visible, but leaving them causes IDE0005 warnings under strict analyzer settings. | Low | Verify the final `JournalModule.cs` body and prune to only the usings still in use. Run `dotnet build -warnaserror` locally before committing if available. |
| A type with the same name (`JournalRepository`) exists elsewhere and the new `using` resolves ambiguously. | Very Low | Grep confirmed `JournalRepository` only exists at `Anela.Heblo.Persistence.Catalog.Journal.JournalRepository`. No ambiguity. |
| Future contributor re-adds the binding to `JournalModule.cs` (regression). | Medium | Optional follow-up: extend `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` with a reflection-based test that asserts `Anela.Heblo.Application.Features.Journal.*` types contain no references to `Anela.Heblo.Persistence.*` namespaces. Out of scope per spec, but worth filing as a follow-up issue — the existing `ModuleBoundariesTests` file already does this for the Leaflet ↔ KnowledgeBase boundary, so the pattern is established. |
| The empty `JournalModule` shell rots — never gets new bindings and becomes pure noise. | Low | Accepted trade-off. FR-3 explicitly preserves the shell for symmetry with other modules. Re-evaluate if a future cleanup removes the empty shells project-wide. |
| `ApplicationDbContext` registration is in `AddPersistenceServices` — if the repository bindings were ever called before `AddPersistenceServices`, `DbContext` would be unresolvable. | Very Low | Composition root already calls both. The bindings register a *factory*; resolution happens only when MediatR handlers run, well after startup. No change in behavior. |

## Specification Amendments

**None required.** The spec is complete, accurate, and matches the codebase. Two clarifications worth folding into the implementation notes (not the spec itself):

1. **Prune all newly-unused `using` directives in `JournalModule.cs`**, not just the Persistence one. After removing the two `AddScoped` calls, `Anela.Heblo.Application.Features.Journal.Contracts` and `Anela.Heblo.Domain.Features.Journal` are also unused. FR-3's "no unused-symbol warnings on `JournalModule`" requires this. Treat this as part of FR-2 even though the spec names only the Persistence using explicitly.
2. **Append the Journal section at the end of the repository block** in `PersistenceModule.cs` rather than alphabetizing. The existing file order is chronological, not alphabetical; appending matches convention and minimizes diff size.

## Prerequisites

None. All of the following are already in place:

- `Anela.Heblo.Persistence.csproj` references `Anela.Heblo.Domain.csproj` (so Domain interfaces resolve in `PersistenceModule.cs`).
- `ApplicationDbContext` is registered in `AddPersistenceServices`, and `JournalRepository`/`JournalTagRepository` depend on it via constructor injection — no change needed.
- Both `AddApplicationServices` and `AddPersistenceServices` are wired into the API project's composition root.
- The test suite (`GetJournalEntryHandlerTests`, `SearchJournalEntriesHandlerTests`, `CreateJournalEntryHandlerTests`, `DeleteJournalEntryHandlerTests`, `JournalRepositoryIntegrationTests`) provides regression coverage; no new tests are required to ship the change.

Implementation can begin immediately. Expected effort: **one focused commit, ~5 minutes of editing plus build/format/test verification**.