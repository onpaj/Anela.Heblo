# Specification: Resolve unimplemented `FamilyEntries` on Journal indicators

## Summary
`JournalIndicator` (domain) and `JournalIndicatorDto` (contract) expose `FamilyEntries` and a derived `TotalEntries`, but `JournalRepository.GetJournalIndicatorsAsync` never assigns `FamilyEntries`, so the value is always `0` and `TotalEntries` is identical to `DirectEntries`. A repository-wide search also shows that the entire indicator surface (`GetJournalIndicatorsAsync`, `JournalIndicator`, `JournalIndicatorDto`) has zero consumers in either the .NET backend or the React frontend. Applying YAGNI, this spec deletes the speculative `FamilyEntries`/`TotalEntries` members and additionally removes the unreferenced indicator surface; an Open Question captures the alternative of implementing the prefix-based count instead.

## Background

### How journal entries link to products
Journal entries associate to a product via a `ProductCodePrefix` stored on `JournalEntryProduct`. Other repository methods use prefix matching: a journal entry is "linked" to product code `X` when an associated prefix `pa.ProductCodePrefix` satisfies `X.StartsWith(pa.ProductCodePrefix)` (see `JournalRepository.GetEntriesByProductAsync` at `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs:161–173`, and the product filter inside `GetEntriesAsync`).

This creates a conceptual distinction the brief refers to:
- **Direct entry** — associated prefix equals the product code exactly.
- **Family entry** — associated prefix is a strictly shorter prefix of the product code (e.g. an entry logged against `TON` when the indicator is computed for `TON001A`).

### The current implementation gap
`GetJournalIndicatorsAsync` (`backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs:175–220`) only computes direct counts (`jep => productCodeList.Contains(jep.ProductCodePrefix)`) and never assigns `FamilyEntries`. The DTO mirrors the same speculative shape (`backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalIndicatorDto.cs:9–10`).

### The wider finding: the indicator surface is dead code
Repo-wide search (`grep -rn "GetJournalIndicators\|JournalIndicator"`) finds matches only in the four files that define the surface:

- `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs` — declares `GetJournalIndicatorsAsync`.
- `backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicator.cs` — domain type.
- `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` — implementation.
- `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalIndicatorDto.cs` — contract type.

There are **no** call sites: no MediatR handler, no controller, no test, no mapper, and no frontend reference to either type. Likewise, `frontend/` contains zero hits for `journalIndicator`, `familyEntries`, or `totalEntries`. The only test hits for "FamilyEntries" are method names in `JournalRepositoryIntegrationTests.cs` (`ShouldFindFamilyEntries`, `ShouldFindCorrectFamilyEntries`) which exercise `GetEntriesByProductAsync`'s prefix matching and do **not** assert on indicator fields.

### Direction
Per CLAUDE.md ("surgical changes", YAGNI) and the global coding-style rule ("Do not build features or abstractions before they are needed"), this spec removes the dead code rather than implementing a feature for which no consumer or product requirement exists. Two layers of cleanup are defined:

1. **Property-level (FR-1/FR-2)** — remove `FamilyEntries` and `TotalEntries` from the indicator type and its DTO. This is the literal scope of the brief.
2. **Surface-level (FR-3/FR-4)** — remove the unreferenced `GetJournalIndicatorsAsync` method, the `JournalIndicator` domain type, and the `JournalIndicatorDto` contract. This handles the broader dead-code observation surfaced during verification.

FR-5/FR-6 cover code-hygiene cleanup after the deletions. NFR-1 captures the breaking-change posture. The Open Questions confirm the chosen direction before implementation begins.

## Functional Requirements

### FR-1: Remove `FamilyEntries` from the domain indicator
Delete the `FamilyEntries` property and the derived `TotalEntries` property from `Anela.Heblo.Domain.Features.Journal.JournalIndicator`. (Skipped if FR-3 is executed, since the whole type is deleted.)

**Acceptance criteria:**
- `JournalIndicator.cs` no longer declares `FamilyEntries`.
- `JournalIndicator.cs` no longer declares `TotalEntries`.
- The remaining properties (`ProductCode`, `DirectEntries`, `LastEntryDate`, `HasRecentEntries`) are unchanged in name, type, and semantics.
- `dotnet build` for the full solution succeeds.

### FR-2: Remove `FamilyEntries` from the public DTO
Delete `FamilyEntries` and `TotalEntries` from `Anela.Heblo.Application.Features.Journal.Contracts.JournalIndicatorDto` to mirror FR-1. (Skipped if FR-3 is executed, since the whole type is deleted.)

**Acceptance criteria:**
- `JournalIndicatorDto.cs` no longer declares `FamilyEntries` or `TotalEntries`.
- After a clean build, the regenerated TypeScript OpenAPI client under `frontend/src/api/generated/` does not contain `familyEntries` or `totalEntries` on any Journal indicator type (and, after FR-3, does not contain a Journal indicator type at all).
- A repository-wide grep for `familyEntries` and `totalEntries` outside `frontend/src/api/generated/` returns zero matches.

### FR-3: Remove the unused indicator surface (recommended, see Open Question)
The entire indicator surface has no consumers in this repository. Delete:

- `Task<Dictionary<string, JournalIndicator>> GetJournalIndicatorsAsync(...)` from `IJournalRepository` (`backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs:21–23`).
- The corresponding implementation in `JournalRepository.cs:175–220`.
- The `JournalIndicator` domain type file (`backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicator.cs`).
- The `JournalIndicatorDto` contract file (`backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalIndicatorDto.cs`).

This subsumes FR-1 and FR-2. The choice between FR-1/FR-2-only and full FR-3 is the first Open Question; the recommended direction is the full removal.

**Acceptance criteria:**
- The four bullet points above are executed. No file in `backend/` references `JournalIndicator` or `JournalIndicatorDto`.
- `IJournalRepository` no longer declares `GetJournalIndicatorsAsync`.
- `dotnet build` for the full solution succeeds.
- A regenerated TypeScript OpenAPI client builds cleanly and contains no Journal indicator type.

### FR-4: Confirm zero consumers before deletion
Before deleting members or files in FR-1 through FR-3, perform and record a final verification grep. This guards against missed consumers (e.g., reflection-based callers, source-generated mappers, or out-of-tree code).

**Acceptance criteria:**
- Running `grep -rn "JournalIndicator\|GetJournalIndicators\|familyEntries\|totalEntries" --include="*.cs" --include="*.ts" --include="*.tsx" --include="*.json"` from repo root returns matches only inside the files being deleted/edited (and inside `brief.md`/`spec.md` under `artifacts/`).
- The verification result is referenced in the implementation PR description.

### FR-5: Clean up adjacent code touched by the removal
- Remove the stale `// Get direct associations` comment block from `JournalRepository.cs` (only relevant if FR-1/FR-2 are taken without FR-3 — under FR-3 the whole method is deleted).
- Remove any now-unused `using` directives in edited files.
- Remove the double blank line that currently sits between the association loop and the recent-entries loop (`JournalRepository.cs:209–210`) if the method is retained.

**Acceptance criteria:**
- `dotnet format` reports no diff on edited files.
- No unused `using` directives remain in edited files.

### FR-6: Update tests
No existing test asserts on `FamilyEntries`, `TotalEntries`, `JournalIndicator`, `JournalIndicatorDto`, or `GetJournalIndicatorsAsync` (confirmed via grep).

- If FR-3 is taken: no new tests are required for the deletion. Existing tests in `backend/test/Anela.Heblo.Tests/Features/Journal/` must continue to pass without modification.
- If FR-3 is **not** taken (FR-1/FR-2 only path): add unit/integration tests for the retained `GetJournalIndicatorsAsync` covering:
  - (a) product with multiple direct associations — verify `DirectEntries` count is correct.
  - (b) product with no associations — verify `DirectEntries == 0`, `LastEntryDate == null`, `HasRecentEntries == false`.
  - (c) product with at least one recent entry — verify `LastEntryDate` is the max entry date and `HasRecentEntries == true`.
  - (d) product whose only association is via a strictly-shorter prefix (a "family" case) — verify `DirectEntries == 0` (proves the property removal does not silently fold family matches into direct counts).

**Acceptance criteria:**
- No test in the solution references `FamilyEntries`, `TotalEntries`, `JournalIndicator`, or `JournalIndicatorDto`.
- If FR-3 is **not** taken: a new test class (or new `[Fact]` methods on `JournalRepositoryIntegrationTests`) exercises `GetJournalIndicatorsAsync` for the four scenarios above using xUnit + FluentAssertions, consistent with existing test style.
- `dotnet test` for the affected test projects passes.

## Non-Functional Requirements

### NFR-1: Backwards compatibility
This is a breaking change to the API contract (`JournalIndicatorDto`) and, under FR-3, also to the repository contract (`IJournalRepository`). It is acceptable because:
- Anela.Heblo is a solo-developer project (per CLAUDE.md project facts).
- No frontend, MCP-server, handler, controller, documentation, or in-repo consumer references the removed members.
- The fields produce misleading values today, so removing them strictly improves API correctness.

**Acceptance criteria:**
- A final grep (per FR-4) confirms no consumer references the removed members outside the files being deleted.
- The PR description explicitly notes the breaking change for changelog/release-notes purposes.

### NFR-2: Behavior preservation
Existing journal flows that do not use the indicator surface must keep their semantics:
- `GetEntriesByProductAsync` continues to use prefix-based product matching.
- `GetEntriesAsync` / `SearchEntriesAsync` keep their current filter behavior.
- The `JournalEntryProduct` table and the `ProductCodePrefix` column are untouched.

**Acceptance criteria:**
- All existing tests in `backend/test/Anela.Heblo.Tests/Features/Journal/` pass without modification.
- No EF Core migration is generated by the change (this is a code-only deletion).

### NFR-3: Build / format / lint gates
The change must pass the project's standard completion gates from CLAUDE.md.

**Acceptance criteria:**
- `dotnet build` clean.
- `dotnet format` reports no diff after the change.
- `npm run build` and `npm run lint` clean (regenerated TypeScript client compiles).

### NFR-4: Performance
No regression. Removal of dead code is neutral-or-better. `GetJournalIndicatorsAsync` was never executed in production paths (no callers), so its deletion has zero runtime impact.

### NFR-5: Security
No security surface is touched. No SQL is hand-built, no auth boundary is altered, no input validation is bypassed. EF Core continues to parameterize all retained queries.

## Data Model
No persistence-layer changes. The `JournalEntryProduct` table, the `ProductCodePrefix` column, EF configurations, and indexes are untouched. No migration is generated.

Removed in-memory members:
- `int JournalIndicator.FamilyEntries` — never populated.
- `int JournalIndicator.TotalEntries` — derived from `FamilyEntries`.
- `int JournalIndicatorDto.FamilyEntries` — mirrors the above.
- `int JournalIndicatorDto.TotalEntries` — mirrors the above.

Removed types (under FR-3, recommended):
- `Anela.Heblo.Domain.Features.Journal.JournalIndicator` (class).
- `Anela.Heblo.Application.Features.Journal.Contracts.JournalIndicatorDto` (class).

Removed interface member (under FR-3):
- `IJournalRepository.GetJournalIndicatorsAsync(IEnumerable<string>, CancellationToken)`.

## API / Interface Design
- No new endpoints, routes, or handlers are added.
- `IJournalRepository` shrinks by one method (under FR-3) or retains it with a narrower return shape (under FR-1/FR-2 only).
- The OpenAPI definition shrinks by the deleted DTO (or by two fields, depending on chosen path). The regenerated TypeScript client (`frontend/src/api/generated/`, per `docs/development/api-client-generation.md`) reflects the change automatically on next build.
- No wire-format consumer exists today, so no client migration is required.

## Dependencies
- EF Core (`Microsoft.EntityFrameworkCore`) — unchanged usage; if FR-3 is taken, one query is deleted.
- OpenAPI → TypeScript client generation pipeline (per `docs/development/api-client-generation.md`) regenerates the frontend client on build; verify the generated output no longer contains the removed members or type.

## Out of Scope
- Implementing a real prefix-based family-entry count. If later desired, it becomes a separate feature: define product requirements, decide on the query strategy (e.g., `EF.Functions.Like(productCode, jep.ProductCodePrefix + "%")` excluding exact matches), decide whether overlapping prefixes are double-counted, and budget for the extra DB cost on whichever catalog listing endpoint consumes indicators.
- Changing prefix-matching semantics in `GetEntriesByProductAsync`, `GetEntriesAsync`, or `SearchEntriesAsync`.
- Renaming `ProductCodePrefix` or revisiting the prefix-as-association data model.
- Touching the `JournalEntryProduct` schema, EF configuration, or indexing.
- Frontend UI changes (none required — no consumer references the removed surface).
- Generalised dead-code sweep across other modules (this spec covers only the Journal indicator surface).

## Open Questions

- **Confirm the scope of removal: property-only (FR-1/FR-2) vs. full-surface (FR-3).** Verification shows that `GetJournalIndicatorsAsync`, `JournalIndicator`, and `JournalIndicatorDto` are referenced **only** by their own declarations — no handler, controller, test, frontend, or doc consumes them. The recommended path is the full-surface removal under FR-3, on YAGNI grounds. The alternative is the conservative property-only path (FR-1/FR-2 plus FR-6 tests) which keeps the method alive in case a planned consumer (e.g., a catalog-listing endpoint that will surface "products with recent journal activity") is imminent. Which path?

- **Confirm the chosen direction is removal, not implementation.** The brief presents removal vs. implementation as alternatives. This spec assumes removal. If a product requirement exists or is imminent to surface family-entry counts in any UI/feature, flip the direction and write a follow-up spec for the implementation path: count entries linked via strictly-shorter prefixes, decide whether overlapping prefixes are double-counted, decide the exact-match-exclusion semantics (the brief proposes `productCode.StartsWith(jep.ProductCodePrefix) && jep.ProductCodePrefix != productCode`), and re-baseline performance for the catalog listing query.

- **Confirm no out-of-repo consumer relies on the fields.** In-repo grep is clean. Are there any out-of-repo consumers (deployment scripts, Power BI dashboards, the MCP server's downstream clients, Postman collections, manual API users) that read `journalIndicator` / `familyEntries` / `totalEntries`? If yes, prefer keeping `JournalIndicatorDto` (without the speculative fields) as a deprecation shim for one release; otherwise remove the whole surface as specified.

## Status: HAS_QUESTIONS