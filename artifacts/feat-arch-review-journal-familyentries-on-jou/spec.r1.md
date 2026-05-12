# Specification: Resolve unimplemented `FamilyEntries` on Journal indicators

## Summary
The `JournalIndicator` (domain) and `JournalIndicatorDto` (contract) expose `FamilyEntries` and a derived `TotalEntries`, but `FamilyEntries` is never populated by `JournalRepository.GetJournalIndicatorsAsync`. Consumers reading these fields receive misleading data: `FamilyEntries` is always `0` and `TotalEntries` is always equal to `DirectEntries`. This feature removes the speculative fields from the domain type and the public API surface (YAGNI), keeping only what is actually computed.

## Background
The Journal module associates a journal entry to a product via a `ProductCodePrefix` stored on `JournalEntryProduct`. Lookups elsewhere in the repository (`GetEntriesByProductAsync`, the product filter in `GetEntriesAsync`) use prefix matching: a journal entry is "linked" to product code `X` when an associated prefix `pa.ProductCodePrefix` satisfies `X.StartsWith(pa.ProductCodePrefix)`. This creates a conceptual distinction:

- **Direct entry**: associated prefix equals the product code exactly.
- **Family entry**: associated prefix is a strict prefix of the product code (the entry is inherited from a broader "family" association such as `TON` while the indicator is computed for `TON001A`).

`GetJournalIndicatorsAsync` (`backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs:175–220`) only computes direct counts (`jep => productCodeList.Contains(jep.ProductCodePrefix)`) and never assigns `FamilyEntries`. The property has been part of the public DTO since introduction but is unused: no code under `frontend/`, no documentation under `docs/`, and no consumer outside the Journal module itself references `familyEntries` or `totalEntries` (grep returns zero matches). Within the test suite, the only occurrences of the word "Family" are method names for `GetEntriesByProductAsync` tests (`JournalRepositoryIntegrationTests.cs:61, 171`), which exercise prefix matching on a different code path and do not assert on indicator properties.

Per CLAUDE.md ("surgical changes", YAGNI) and the global coding-style rule ("Do not build features or abstractions before they are needed"), we remove the dead fields rather than implement a feature for which no consumer or product requirement exists. If a future need arises, the field can be reintroduced together with its query.

## Functional Requirements

### FR-1: Remove `FamilyEntries` from the domain indicator
Delete the `FamilyEntries` property from `Anela.Heblo.Domain.Features.Journal.JournalIndicator`. Remove the derived `TotalEntries` property (it equals `DirectEntries` after the change, so it adds no information).

**Acceptance criteria:**
- `JournalIndicator.cs` no longer declares `FamilyEntries`.
- `JournalIndicator.cs` no longer declares `TotalEntries`.
- The remaining properties (`ProductCode`, `DirectEntries`, `LastEntryDate`, `HasRecentEntries`) are unchanged in name, type, and semantics.
- `dotnet build` for the full solution succeeds.

### FR-2: Remove `FamilyEntries` from the public DTO
Delete `FamilyEntries` and `TotalEntries` from `Anela.Heblo.Application.Features.Journal.Contracts.JournalIndicatorDto` to mirror FR-1.

**Acceptance criteria:**
- `JournalIndicatorDto.cs` no longer declares `FamilyEntries` or `TotalEntries`.
- After a clean build, the regenerated TypeScript OpenAPI client (`frontend/src/api/generated/`) no longer contains `familyEntries` or `totalEntries` on the Journal indicator type.
- A repository-wide grep for `familyEntries` and `totalEntries` outside `frontend/src/api/generated/` and the removed C# files returns zero matches.

### FR-3: Simplify the repository implementation
Clean up `GetJournalIndicatorsAsync` so it no longer pretends to compute a "direct vs. family" split. The query that populates `DirectEntries` and `LastEntryDate` stays unchanged in behavior — only the framing is updated. Rename the local variable `directAssociations` to a neutral name (e.g., `associationCounts`) and remove or rewrite the stale `// Get direct associations` comment.

**Acceptance criteria:**
- `GetJournalIndicatorsAsync` still returns the exact same per-product entry count and `LastEntryDate` as before.
- No code path assigns `FamilyEntries` (the property is gone).
- The comment on the association query is either removed or reflects the new framing ("Count entries linked to each product code via exact prefix match").

### FR-4: Update or add tests
No existing test asserts on `FamilyEntries` or `TotalEntries` (confirmed via grep). Add at least one unit/integration test for `GetJournalIndicatorsAsync` covering:
- (a) product with multiple direct associations — verify `DirectEntries` count is correct,
- (b) product with no associations — verify `DirectEntries == 0`, `LastEntryDate == null`, `HasRecentEntries == false`,
- (c) product with at least one recent entry — verify `LastEntryDate` is the max entry date and `HasRecentEntries == true`.

**Acceptance criteria:**
- No test in the solution references `FamilyEntries` or `TotalEntries`.
- A new test class (or new `[Fact]` methods on the existing `JournalRepositoryIntegrationTests`) exercises `GetJournalIndicatorsAsync` for the three scenarios above using xUnit + FluentAssertions, consistent with the existing test style.
- `dotnet test` for the affected test projects passes.

## Non-Functional Requirements

### NFR-1: Backwards compatibility
This is a breaking change to the API contract (`JournalIndicatorDto`). It is acceptable because:
- Anela.Heblo is a solo-developer project (per CLAUDE.md project facts).
- No frontend, MCP-server, documentation, or in-repo external consumer references the removed fields.
- The fields produce misleading values today, so removing them strictly improves API correctness.

**Acceptance criteria:**
- A final grep confirms no consumer references `familyEntries` or `totalEntries` (outside the generated TypeScript client, which is regenerated on build).

### NFR-2: Behavior preservation
`DirectEntries`, `LastEntryDate`, and `HasRecentEntries` must be produced with byte-identical semantics for every input product code.

**Acceptance criteria:**
- Existing tests in `JournalRepositoryIntegrationTests.cs` that exercise prefix matching via `GetEntriesByProductAsync` keep passing without modification.
- The new indicator tests (FR-4) verify count parity with the pre-change behavior for at least one fixture.

### NFR-3: Build / format / lint gates
The change must pass the project's standard completion gates from CLAUDE.md.

**Acceptance criteria:**
- `dotnet build` clean.
- `dotnet format` reports no diff after the change.
- `npm run build` and `npm run lint` clean (regenerated TypeScript client compiles).

### NFR-4: Performance
No regression in `GetJournalIndicatorsAsync` query time; the change is removal-only and should be neutral-or-better.

## Data Model
No persistence-layer changes. The `JournalEntryProduct` table and the `ProductCodePrefix` column are untouched. The prefix-matching semantics used by `GetEntriesByProductAsync` and the search-criteria product filter remain in place.

Removed in-memory members on `JournalIndicator` and `JournalIndicatorDto`:
- `int FamilyEntries` — never populated, removed.
- `int TotalEntries` — derived from `FamilyEntries`, removed.

## API / Interface Design
- `IJournalRepository.GetJournalIndicatorsAsync` signature is unchanged. Only the shape of the returned `JournalIndicator` changes.
- The DTO change propagates through the OpenAPI definition to the regenerated TypeScript client on next build. Wire payloads emitted by any handler that serializes `JournalIndicatorDto` lose two integer fields. No new endpoint, route, or handler is added.

## Dependencies
- EF Core (`Microsoft.EntityFrameworkCore`) — unchanged usage.
- OpenAPI → TypeScript client generation pipeline (per `docs/development/api-client-generation.md`) regenerates the frontend client on build; verify the generated file no longer contains the removed members.

## Out of Scope
- Implementing a real prefix-based family-entry count. If this is later desired, it is a separate feature: define product requirements, decide on the query strategy (e.g., `EF.Functions.Like(productCode, jep.ProductCodePrefix + "%")` excluding exact matches), and budget for the extra DB cost on the catalog listing endpoint that consumes indicators.
- Changing prefix-matching semantics in `GetEntriesByProductAsync`, `GetEntriesAsync`, or `SearchEntriesAsync`.
- Renaming `ProductCodePrefix` or revisiting the prefix-as-association data model.
- Touching the `JournalEntryProduct` schema, EF configuration, or indexing.
- Frontend UI changes (none required since no consumer references the removed fields).

## Open Questions
- **Confirm the chosen direction is removal, not implementation.** The brief presents removal vs. implementation as alternatives. This spec assumes removal (YAGNI, no consumer found in this repo). If there is a product reason to surface family-entry counts in any current or imminent UI/feature, flip the direction and write a follow-up spec for the implementation path (count entries linked via strictly-shorter prefixes, decide whether overlapping prefixes are double-counted, and re-baseline performance for the catalog listing query).
- **Confirm no out-of-repo consumer relies on the fields.** Grep verifies that nothing inside this repository references `familyEntries` or `totalEntries`. Are there any out-of-repo consumers (scripts, dashboards, the MCP server's downstream clients, Postman collections) that read these fields? If yes, prefer keeping `TotalEntries` as a straight `=> DirectEntries` shim during a deprecation window; otherwise remove both fields as specified.

## Status: HAS_QUESTIONS