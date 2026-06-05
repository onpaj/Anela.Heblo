# Specification: Relocate Journal Repositories to Correct Persistence Folder

## Summary
Move the Journal module's repository implementations out of the misleading `Persistence/Catalog/Journal/` sub-folder into their proper location at `Persistence/Journal/`, update namespaces accordingly, and fix all dependent `using` statements. This is a structural cleanup with no behavior change.

## Background
The Journal module is an independent feature with no structural relationship to Catalog, yet its persistence code currently lives inside `backend/src/Anela.Heblo.Persistence/Catalog/Journal/`. This violates the documented Persistence layer layout (`docs/architecture/filesystem.md`), which mandates `Persistence/{Feature}/` as the root for feature-specific persistence code.

The misplacement causes three concrete problems:
1. Developers searching `Persistence/Journal/` for Journal persistence code do not find it.
2. The namespace `Anela.Heblo.Persistence.Catalog.Journal` propagates into the Application layer via `JournalModule.cs` DI registration, falsely implying a Catalog dependency in every `using` statement.
3. It is a stale scaffolding artifact (Journal was likely created under Catalog and never cleaned up).

This issue was filed by the daily arch-review routine on 2026-06-04 and is related to issue #2513, which moves DI registrations from feature modules to `PersistenceModule.cs`.

## Functional Requirements

### FR-1: Relocate Repository Files
Move both files to the correct Persistence folder layout.

**Files to move:**
- `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` → `backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs`
- `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalTagRepository.cs` → `backend/src/Anela.Heblo.Persistence/Journal/JournalTagRepository.cs`

**Acceptance criteria:**
- Both files exist at the new path `backend/src/Anela.Heblo.Persistence/Journal/`.
- Both files are deleted from `backend/src/Anela.Heblo.Persistence/Catalog/Journal/`.
- If the now-empty `backend/src/Anela.Heblo.Persistence/Catalog/Journal/` folder remains, it is removed.
- If `backend/src/Anela.Heblo.Persistence/Catalog/` becomes empty as a result, it is also removed; otherwise it is left untouched.
- Git history for the moved files is preserved (use `git mv` rather than delete + create).

### FR-2: Update Namespaces in Moved Files
Update the `namespace` declaration in both moved files to match the new folder location.

**Acceptance criteria:**
- `JournalRepository.cs` declares `namespace Anela.Heblo.Persistence.Journal`.
- `JournalTagRepository.cs` declares `namespace Anela.Heblo.Persistence.Journal`.
- No file in the repository still declares `namespace Anela.Heblo.Persistence.Catalog.Journal`.

### FR-3: Update Other Persistence Configuration/Entity Files (if any)
Audit the `Persistence/Catalog/Journal/` folder for any other Journal-related files (e.g., `JournalConfiguration.cs`, `JournalTagConfiguration.cs`, entity-related code) and move them alongside the repositories using the same rules as FR-1 and FR-2.

**Acceptance criteria:**
- After the move, `backend/src/Anela.Heblo.Persistence/Catalog/Journal/` contains no Journal-related code.
- All Journal persistence files (repositories, EF Core configurations, etc.) live under `backend/src/Anela.Heblo.Persistence/Journal/` and declare `namespace Anela.Heblo.Persistence.Journal`.

### FR-4: Fix Dependent `using` Statements
Update every consumer of the old namespace `Anela.Heblo.Persistence.Catalog.Journal` to use `Anela.Heblo.Persistence.Journal`.

**Known consumer:** `JournalModule.cs` (DI registration in the Application layer).

**Acceptance criteria:**
- A repository-wide search confirms zero occurrences of the string `Anela.Heblo.Persistence.Catalog.Journal` in `.cs`, `.csproj`, `.json`, or any other text files.
- All previously dependent files compile and import the new namespace.
- If issue #2513 has already moved DI registrations to `PersistenceModule.cs`, the `using` is updated there instead; if not, it is updated in `JournalModule.cs` as a minimum, with a note in the PR linking to #2513.

### FR-5: Verify Build and Tests
The refactor must not change behavior. The full backend build and test suite must pass.

**Acceptance criteria:**
- `dotnet build` succeeds with zero new warnings or errors attributable to the change.
- `dotnet format` reports no formatting violations in the moved/changed files.
- All existing Journal-related unit and integration tests pass without modification (other than namespace updates in `using` statements if tests reference the old namespace).
- No EF Core migrations are added or modified.

## Non-Functional Requirements

### NFR-1: Performance
No runtime performance impact. This is a compile-time/source-organization change only.

### NFR-2: Security
No security impact. No changes to authentication, authorization, data access, or input handling.

### NFR-3: Backward Compatibility
- No public HTTP API or contract changes.
- No database schema changes.
- No changes to OpenAPI client generation output.
- Internal namespace change is acceptable because the Persistence layer is not a publicly consumed API surface.

### NFR-4: Code Quality
- Match existing code style in the moved files exactly; do not reformat unrelated lines.
- Do not modify the contents of repository methods, only the `namespace` declaration and the file location.
- Do not "improve" adjacent code (per CLAUDE.md surgical-changes rule).

## Data Model
No data model changes. Entity classes and EF Core configurations retain their existing schemas and mappings.

## API / Interface Design
No public API changes.

**Internal namespace change:**
| Before | After |
|---|---|
| `Anela.Heblo.Persistence.Catalog.Journal.JournalRepository` | `Anela.Heblo.Persistence.Journal.JournalRepository` |
| `Anela.Heblo.Persistence.Catalog.Journal.JournalTagRepository` | `Anela.Heblo.Persistence.Journal.JournalTagRepository` |

## Dependencies
- **Issue #2513** — moves DI registrations from per-feature modules to `PersistenceModule.cs`. If this issue is merged first, FR-4 should update `PersistenceModule.cs` instead of `JournalModule.cs`. The two changes are compatible in either order; coordinate the merge to avoid conflicts.
- **`docs/architecture/filesystem.md`** — defines the canonical Persistence layout this change conforms to.

## Out of Scope
- Refactoring DI registration location (covered by issue #2513).
- Any change to Journal domain logic, entities, DTOs, or API endpoints.
- Renaming or restructuring other modules that may have similar issues (audit only Journal as part of this work).
- Database schema or migration changes.
- Documentation updates beyond what is required to keep references accurate (no new docs needed; `filesystem.md` is already correct).

## Open Questions
None.

## Status: COMPLETE