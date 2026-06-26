# Architecture Review: Relocate Journal Repositories to Correct Persistence Folder

## Skip Design: true

Backend-only namespace and file-move refactor. No UI components, screens, or visual changes are introduced or affected.

## Architectural Fit Assessment

This change brings the Journal module into compliance with the canonical Persistence layout documented in `docs/architecture/filesystem.md` (lines 40–47, 160–168): feature-specific persistence code lives under `Anela.Heblo.Persistence/{Feature}/`. Every other module that has been added since (`Persistence/KnowledgeBase/`, `Persistence/Features/Article/`, `Persistence/Features/Bank/`, `Persistence/Purchase/PurchaseOrders/`, `Persistence/GridLayouts/`, etc.) follows this convention. Only the `Catalog/` siblings (`Inventory/`, `Stock/`, `ManufactureDifficulty/`) legitimately belong under Catalog because they reference `Anela.Heblo.Domain.Features.Catalog.*` types. `Journal` does not — it consumes only `Anela.Heblo.Domain.Features.Journal.*`, confirming the misplacement.

Integration points:
- **DI registration**: `Application/Features/Journal/JournalModule.cs:4` is the runtime consumer.
- **EF Core configuration discovery**: `ApplicationDbContext.cs:170` uses `ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly)`. Configuration discovery is by assembly scan, **not** by namespace — moving the files is therefore safe at runtime. No `OnModelCreating` change is needed, and no migration is added or modified.
- **Tests**: `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs:3` directly imports the old namespace. This consumer is **not listed in spec FR-4** and must be included.
- **Coexisting issue #2513**: would move the `AddScoped<IJournalRepository, JournalRepository>()` calls from `JournalModule.cs` to `PersistenceModule.cs`. The two changes are orthogonal — whichever merges first dictates which file the `using` lives in at completion.

## Proposed Architecture

### Component Overview

```
Before:                                          After:
Anela.Heblo.Persistence/                         Anela.Heblo.Persistence/
├── Catalog/                                     ├── Catalog/
│   ├── Inventory/    (keep)                     │   ├── Inventory/    (unchanged)
│   ├── Stock/        (keep)                     │   ├── Stock/        (unchanged)
│   ├── ManufactureDifficulty/ (keep)            │   ├── ManufactureDifficulty/ (unchanged)
│   └── Journal/  ← MOVE OUT                     │   └── (Journal removed)
│       ├── JournalRepository.cs                 └── Journal/  ← NEW
│       ├── JournalTagRepository.cs                  ├── JournalRepository.cs
│       ├── JournalEntryConfiguration.cs             ├── JournalTagRepository.cs
│       ├── JournalEntryProductConfiguration.cs      ├── JournalEntryConfiguration.cs
│       ├── JournalEntryTagConfiguration.cs          ├── JournalEntryProductConfiguration.cs
│       └── JournalEntryTagAssignmentConfiguration  ├── JournalEntryTagConfiguration.cs
│                                                   └── JournalEntryTagAssignmentConfiguration.cs

namespace Anela.Heblo.Persistence.Catalog.Journal  →  namespace Anela.Heblo.Persistence.Journal
```

The full Journal folder contains **six** files (two repositories + four EF Core entity configurations), not just the two referenced in the spec summary. All six share the same misplaced namespace and must move together — this is already required by spec FR-3, but worth surfacing explicitly because the summary mentions only the two repositories.

`Persistence/Catalog/` **must remain** after the move because `Inventory/`, `Stock/`, and `ManufactureDifficulty/` continue to live there. Spec FR-1's conditional ("if Catalog becomes empty…") correctly leaves the folder in place, but the check itself is unnecessary here — it will not become empty.

### Key Design Decisions

#### Decision 1: Preserve git history via `git mv`
**Options considered:**
- (a) `git mv` each file then update the namespace in a follow-up commit.
- (b) Delete + re-create files (loses history).
- (c) Single commit combining `git mv` and namespace edit.

**Chosen approach:** (c) — perform `git mv` and namespace edits in a **single commit** per file (or as a single batch). Git's rename detection (`git log --follow`) will still recognize moves with small content edits because the move-plus-namespace-change diff stays well above git's similarity threshold (>95% unchanged).

**Rationale:** Spec FR-1 mandates history preservation. A single commit keeps the move atomic; reviewers see the move and the namespace edit together. Splitting into two commits is unnecessary churn for a six-file refactor.

#### Decision 2: Keep block-scoped namespace style in moved files
**Options considered:**
- (a) Convert to file-scoped namespace (`namespace X;`), matching the neighboring `LotRepository.cs` style.
- (b) Preserve the existing block-scoped namespace (`namespace X { ... }`) used in the current Journal files.

**Chosen approach:** (b) — keep block-scoped.

**Rationale:** Spec NFR-4 and the project-level "surgical changes" rule (`CLAUDE.md`) explicitly forbid reformatting adjacent lines or changing style during a structural move. Style alignment with neighbors is a separate concern and out of scope for this change. Implementers must **not** let `dotnet format` rewrite the namespace block style. If `dotnet format` does want to change it, configure or skip the affected files for this PR — but flagging that this stylistic inconsistency exists is worthwhile (see Specification Amendments).

#### Decision 3: Update DI `using` in `JournalModule.cs` regardless of issue #2513 status
**Options considered:**
- (a) Wait for #2513 to merge first.
- (b) Update `JournalModule.cs` now; let #2513 reconcile via merge or rebase.
- (c) Update `PersistenceModule.cs` preemptively even if #2513 hasn't merged.

**Chosen approach:** (b) — update wherever the registration **currently** lives. If #2513 merges first, this PR rebases and updates `PersistenceModule.cs` instead. If this PR merges first, #2513 carries the updated `using` forward when it moves the registration.

**Rationale:** The two PRs are mechanically independent. Coupling them sequencing-wise is unnecessary friction. The `using` edit is one line and trivially reconciled in either order.

## Implementation Guidance

### Directory / Module Structure

Create new folder: `backend/src/Anela.Heblo.Persistence/Journal/`

Move (via `git mv`) and update the namespace in:
| File | Old path | New path |
|------|----------|----------|
| `JournalRepository.cs` | `Persistence/Catalog/Journal/` | `Persistence/Journal/` |
| `JournalTagRepository.cs` | `Persistence/Catalog/Journal/` | `Persistence/Journal/` |
| `JournalEntryConfiguration.cs` | `Persistence/Catalog/Journal/` | `Persistence/Journal/` |
| `JournalEntryProductConfiguration.cs` | `Persistence/Catalog/Journal/` | `Persistence/Journal/` |
| `JournalEntryTagConfiguration.cs` | `Persistence/Catalog/Journal/` | `Persistence/Journal/` |
| `JournalEntryTagAssignmentConfiguration.cs` | `Persistence/Catalog/Journal/` | `Persistence/Journal/` |

Delete the now-empty `Persistence/Catalog/Journal/` folder. Do **not** touch `Persistence/Catalog/` — its other subfolders remain valid.

### Interfaces and Contracts

No interface changes. The contracts (`IJournalRepository`, `IJournalTagRepository`) live in `Anela.Heblo.Domain.Features.Journal` and are unaffected.

The only contract-level change is the **internal** namespace of the implementations:

| Type | Before | After |
|------|--------|-------|
| `JournalRepository` | `Anela.Heblo.Persistence.Catalog.Journal` | `Anela.Heblo.Persistence.Journal` |
| `JournalTagRepository` | `Anela.Heblo.Persistence.Catalog.Journal` | `Anela.Heblo.Persistence.Journal` |
| `JournalEntryConfiguration` | `Anela.Heblo.Persistence.Catalog.Journal` | `Anela.Heblo.Persistence.Journal` |
| `JournalEntryProductConfiguration` | `Anela.Heblo.Persistence.Catalog.Journal` | `Anela.Heblo.Persistence.Journal` |
| `JournalEntryTagConfiguration` | `Anela.Heblo.Persistence.Catalog.Journal` | `Anela.Heblo.Persistence.Journal` |
| `JournalEntryTagAssignmentConfiguration` | `Anela.Heblo.Persistence.Catalog.Journal` | `Anela.Heblo.Persistence.Journal` |

### Data Flow

No data flow changes. Runtime sequence:
1. `JournalController` (API) sends MediatR request.
2. `Get/Create/UpdateJournalEntryHandler` (Application) calls `IJournalRepository` / `IJournalTagRepository`.
3. DI resolves to `JournalRepository` / `JournalTagRepository` (Persistence) — only the namespace from which DI imports the concrete type changes.
4. EF Core applies the entity configurations via `ApplyConfigurationsFromAssembly` — assembly scan finds them at the new namespace without code change in `ApplicationDbContext`.

### Consumers to Update (complete list)

The spec lists only `JournalModule.cs`. The full set of `using Anela.Heblo.Persistence.Catalog.Journal;` references in the repository is:

1. `backend/src/Anela.Heblo.Application/Features/Journal/JournalModule.cs:4` — production DI registration.
2. `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs:3` — integration test directly instantiates `JournalRepository`.

Both must be updated. Post-change verification command: `grep -rn "Anela\.Heblo\.Persistence\.Catalog\.Journal" backend/` must return zero results (FR-4 acceptance criterion).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Spec FR-4 lists only `JournalModule.cs`; the integration test reference is missed and silently breaks the test build. | Medium | Treat the repo-wide grep (FR-4 acceptance) as authoritative, not the "Known consumer" list. Add an explicit step to update `JournalRepositoryIntegrationTests.cs`. |
| `dotnet format` rewrites block-scoped namespace to file-scoped, violating NFR-4 surgical-changes rule. | Low | After the move, run `dotnet format` first to confirm it doesn't touch the namespace style; if it does, exclude the moved files from this formatting pass or run `dotnet format --verify-no-changes` on the touched files before committing. |
| Merge conflict with issue #2513 if both PRs touch `JournalModule.cs` `using` and DI lines. | Low | Resolve by accepting #2513's structural move and keeping the new `using Anela.Heblo.Persistence.Journal;` form. Whichever merges second rebases. |
| Git rename detection fails because both the path **and** namespace line change, splitting the move into delete+add in some tooling views. | Low | Acceptable. Default git rename detection (50% similarity) easily clears this — content is >95% unchanged. Reviewers using GitHub web UI may need to enable "show rename" but `git log --follow` works. |
| Hidden `csproj` or `.editorconfig` reference to the old folder path. | Low | `Anela.Heblo.Persistence.csproj` is an SDK-style project that includes `**/*.cs` by glob — no per-file references exist. Verified by inspection of repo conventions. Repo-wide grep across `.csproj` and `.json` per FR-4 acceptance covers any unexpected reference. |

## Specification Amendments

1. **FR-4 must include the integration test consumer.** Add `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs` to the known-consumers list. The spec's "Known consumer: `JournalModule.cs`" wording is incomplete; the test file is the second production-affecting consumer and missing it will break `dotnet test`.

2. **FR-1 "Catalog becomes empty" check is dead code.** `Persistence/Catalog/` contains `Inventory/`, `Stock/`, and `ManufactureDifficulty/`, all of which legitimately belong there. The conditional cleanup of `Persistence/Catalog/` can be removed from the spec as it will never trigger. Keep the cleanup of `Persistence/Catalog/Journal/` itself.

3. **Clarify file count.** The spec summary mentions only `JournalRepository.cs` and `JournalTagRepository.cs`; FR-3 covers "any other Journal-related files" generically. The concrete inventory is six files (two repositories + four EF Core entity configurations: `JournalEntryConfiguration`, `JournalEntryProductConfiguration`, `JournalEntryTagConfiguration`, `JournalEntryTagAssignmentConfiguration`). Listing them explicitly in FR-3 removes any ambiguity for the implementer.

4. **Add explicit non-goal: do not change namespace style.** Existing Journal files use block-scoped namespaces while neighboring Persistence files (e.g. `LotRepository.cs`) use file-scoped. NFR-4 implicitly forbids changing this, but stating it explicitly prevents an implementer from "fixing" it during the move and inflating the diff.

## Prerequisites

None. This refactor requires no migration, no infrastructure change, no configuration, and no coordination with other in-flight work beyond the soft dependency on #2513 (handled by merge order, not blocking). It can begin immediately.