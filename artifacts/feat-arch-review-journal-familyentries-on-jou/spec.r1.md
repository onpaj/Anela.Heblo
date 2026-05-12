# Specification: Resolve unimplemented `FamilyEntries` on Journal indicators

## Summary
The `JournalIndicator` (domain) and `JournalIndicatorDto` (contract) expose `FamilyEntries` and a derived `TotalEntries`, but `FamilyEntries` is never populated by `JournalRepository.GetJournalIndicatorsAsync`. Consumers reading these fields receive misleading data: `FamilyEntries` is always `0` and `TotalEntries` is always equal to `DirectEntries`. This feature removes the speculative fields from both the domain type and the public API surface (YAGNI), keeping only the value that is actually computed.

## Background
The Journal module supports associating a journal entry to a product via a `ProductCodePrefix` stored on `JournalEntryProduct`. Lookups by product elsewhere in the repository (`GetEntriesByProductAsync`, `GetEntriesAsync` product filter) use prefix matching: a journal entry is "linked" to product code `X` when any associated prefix `pa.ProductCodePrefix` satisfies `X.StartsWith(pa.ProductCodePrefix)`. This creates a conceptual distinction:

- **Direct entry**: associated prefix equals the product code exactly.
- **Family entry**: associated prefix is a strict prefix of the product code (the entry is inherited from a broader "family" association such as `TON` or `TON001` while the indicator is for `TON001A`).

`GetJournalIndicatorsAsync` (`backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs:175–220`) only computes direct counts (`jep => productCodeList.Contains(jep.ProductCodePrefix)`) and never assigns `FamilyEntries`. The property has been in the public DTO since introduction but is unused: no frontend code references `familyEntries` or `totalEntries` (grep over `frontend/` returns zero matches).

Per CLAUDE.md ("surgical changes", YAGNI), and the global coding-style rule (KISS / YAGNI: "Do not build features or abstractions before they are needed"), we remove the dead fields rather than implement a feature for which there is no demonstrated consumer or product requirement. If a future need arises, the field can be reintroduced together with its query.

## Functional Requirements

### FR-1: Remove `FamilyEntries` from the domain indicator
Delete the `FamilyEntries` property from `Anela.Heblo.Domain.Features.Journal.JournalIndicator`. Replace the computed `TotalEntries` with a property that equals `DirectEntries`, OR remove `TotalEntries` entirely if no internal caller depends on it.

**Acceptance criteria:**
- `JournalIndicator.cs` no longer declares `FamilyEntries`.
- `JournalIndicator.cs` no longer exposes a derived `TotalEntries` that references `FamilyEntries`. Implementation choice: remove `TotalEntries` (preferred — same information as `DirectEntries`, so it adds noise) unless a non-test caller is found, in which case keep `TotalEntries` as a straight `=> DirectEntries` shim.
- `dotnet build` succeeds for the full solution.

### FR-2: Remove `FamilyEntries` from the public DTO
Delete the `FamilyEntries` property from `Anela.Heblo.Application.Features.Journal.Contracts.JournalIndicatorDto` and remove the `TotalEntries` shim (mirror of FR-1).

**Acceptance criteria:**
- `JournalIndicatorDto.cs` no longer declares `FamilyEntries`.
- `JournalIndicatorDto.cs` either drops `TotalEntries` or keeps it as `=> DirectEntries` consistent with the domain decision in FR-1.
- The regenerated TypeScript OpenAPI client (`frontend/src/api/generated/api-client.ts`) no longer contains `familyEntries` on the Journal indicator type after build. No frontend code references the removed members (verified by grep returning zero matches on `familyEntries`, `totalEntries` under `frontend/src/` outside `generated/`).

### FR-3: Simplify the repository implementation
Clean up `GetJournalIndicatorsAsync` so it no longer pretends to compute a "direct vs. family" split. The query that populates `DirectEntries` stays unchanged in behavior — it simply does not need the "direct" framing anymore. Rename the local variable `directAssociations` to a neutral name (e.g., `associationCounts`) and remove the misleading `// Get direct associations` comment if it remains stale.

**Acceptance criteria:**
- `GetJournalIndicatorsAsync` still returns the exact same per-product entry count and `LastEntryDate` as before (behavior preserved for `DirectEntries` and `LastEntryDate`/`HasRecentEntries`).
- No code path assigns `FamilyEntries` (the property is gone).
- The double blank line between the direct-associations loop and the recent-entries loop (`JournalRepository.cs:208–210`) is collapsed to a single blank line as part of the cleanup.

### FR-4: Update tests
Any test that asserts on `FamilyEntries` or `TotalEntries` (none found at the time of writing — `JournalRepositoryIntegrationTests` uses these tokens only inside test method names referring to `GetEntriesByProductAsync` and `Family` as a domain concept, not on indicator properties) must be updated or deleted. Add at least one unit/integration test for `GetJournalIndicatorsAsync` covering: (a) product with multiple direct associations, (b) product with no associations, (c) `LastEntryDate` and `HasRecentEntries` correctly populated.

**Acceptance criteria:**
- No test references the removed `FamilyEntries` or `TotalEntries` members.
- `GetJournalIndicatorsAsync` has at least one happy-path test asserting `DirectEntries`, `LastEntryDate`, and `HasRecentEntries`.
- `dotnet test` for the affected projects passes.

## Non-Functional Requirements

### NFR-1: Backwards compatibility
This is a breaking change to the API contract (`JournalIndicatorDto`). The change is acceptable because:
- The repository is a solo-developer project (per CLAUDE.md project facts).
- No frontend consumer references the removed fields.
- The fields produce misleading values today, so removing them strictly improves API correctness.

**Acceptance criteria:**
- No documented or undocumented external consumer (e.g., the MCP server in `docs/integrations/mcp-server.md`) references the fields. See Open Questions.

### NFR-2: Behavior preservation
`DirectEntries`, `LastEntryDate`, and `HasRecentEntries` must continue to be produced with byte-identical semantics for every input product code.

**Acceptance criteria:**
- Existing integration tests in `JournalRepositoryIntegrationTests.cs` that exercise prefix matching via `GetEntriesByProductAsync` keep passing without modification.
- A new indicator test (FR-4) verifies count parity with the prior implementation for at least one fixture.

### NFR-3: Build/format/lint gates
The change must pass the project's standard completion gates from CLAUDE.md.

**Acceptance criteria:**
- `dotnet build` clean.
- `dotnet format` reports no diff after the change.
- `npm run build` and `npm run lint` clean (regenerated TypeScript client compiles).

### NFR-4: Performance
No regression in `GetJournalIndicatorsAsync` query time; the change is removal-only, so cost should be unchanged or lower (one fewer assigned property per result).

## Data Model
No persistence-layer changes. The `JournalEntryProduct` table and `ProductCodePrefix` column are untouched. The prefix-matching semantics used by `GetEntriesByProductAsync` and the search criteria filter remain in place.

Removed in-memory members on `JournalIndicator` and `JournalIndicatorDto`:
- `int FamilyEntries` — never populated, removed.
- `int TotalEntries` — derived from `FamilyEntries`, removed (or reduced to `=> DirectEntries` only if a consumer is identified).

## API / Interface Design
- `IJournalRepository.GetJournalIndicatorsAsync` signature is unchanged. Only the shape of the returned `JournalIndicator` changes.
- The DTO change propagates through the OpenAPI definition to the regenerated TypeScript client on next build. Anywhere the DTO is serialized to JSON, the wire payload loses two integer fields. No new endpoint, route, or handler is added.

## Dependencies
- EF Core (`Microsoft.EntityFrameworkCore`) — unchanged usage.
- The OpenAPI → TypeScript client generation pipeline (per `docs/development/api-client-generation.md`) regenerates the frontend client on build; verify the generated file no longer contains the removed members.

## Out of Scope
- Implementing a real prefix-based family-entry count. If this is later desired, it is a separate feature: define product requirements, decide on query strategy (e.g., `EF.Functions.Like(productCode, jep.ProductCodePrefix + "%")` excluding exact matches), and budget for the extra DB cost on the catalog listing endpoint that consumes indicators.
- Changing how prefix matching works in `GetEntriesByProductAsync`, `GetEntriesAsync`, or `SearchEntriesAsync`.
- Renaming `ProductCodePrefix` or revisiting the prefix-as-association data model.
- Touching `JournalEntryProduct` schema, configuration, or indexing.
- Frontend UI changes (none required since no consumer references the removed fields).

## Open Questions
- **Confirm no external consumer relies on the fields.** Beyond the in-repo frontend, do any external integrations (Heblo MCP server, scripts, dashboards, or third-party clients) read `familyEntries` or `totalEntries` from the Journal indicators API? If yes, prefer keeping `TotalEntries` as a straight `=> DirectEntries` shim during a deprecation window; otherwise remove both fields.
- **Confirm the chosen direction is removal, not implementation.** The brief presents removal vs. implementation as alternatives. This spec assumes removal (YAGNI, no consumer found). If there is a product reason to surface family-entry counts in any current or imminent UI/feature, flip the direction and write a follow-up spec for the implementation path (count entries linked via strictly-shorter prefixes, decide whether overlapping prefixes are double-counted, and re-baseline performance for the catalog listing query).

## Status: HAS_QUESTIONS