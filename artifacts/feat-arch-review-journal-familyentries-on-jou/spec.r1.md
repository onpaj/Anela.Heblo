# Specification: Remove unimplemented `FamilyEntries` from `JournalIndicator`

## Summary
The `JournalIndicator` domain model and its mirrored `JournalIndicatorDto` expose a `FamilyEntries` property that is never populated by the repository, making `TotalEntries` a misleading alias for `DirectEntries`. This spec removes the unimplemented property to align the public surface with actual behavior (YAGNI).

## Background
`JournalIndicator` (domain) and `JournalIndicatorDto` (application contract) both declare `FamilyEntries` and a computed `TotalEntries => DirectEntries + FamilyEntries`. The `JournalRepository.GetJournalIndicatorsAsync` method only assigns `DirectEntries` via a grouped query on `JournalEntryProduct.ProductCodePrefix`; `FamilyEntries` defaults to `0` for every result. Any client therefore receives misleading values.

A codebase-wide search shows that `GetJournalIndicatorsAsync`, `JournalIndicator`, and `JournalIndicatorDto` have **no current consumer** outside their declaration files — no MediatR handler, no controller, no frontend hook, no test. The DTO is never mapped from the domain entity. The "family vs direct" distinction is real conceptually (the `JournalEntry → JournalEntryProduct → ProductCodePrefix` prefix-matching pattern already exists in `GetEntriesByProductAsync` at `JournalRepository.cs:169`), but it has never been wired into the indicator API.

Two paths were considered: (1) implement the prefix-based family count, or (2) remove the unimplemented property. Because no consumer exists today and YAGNI applies, path **(2) Remove** is selected. Adding a populated family count later — with a real consumer driving the requirement — is a small, mechanical change at that point.

## Functional Requirements

### FR-1: Remove `FamilyEntries` from the domain entity
Delete the `FamilyEntries` property from `Anela.Heblo.Domain.Features.Journal.JournalIndicator` and rewrite `TotalEntries` so it no longer depends on a non-existent field.

**Acceptance criteria:**
- `JournalIndicator` no longer declares a `FamilyEntries` property.
- `JournalIndicator.TotalEntries` is defined as a property returning `DirectEntries` (per brief option 2: `public int TotalEntries => DirectEntries;`).
- No reference to `FamilyEntries` remains in `backend/src/Anela.Heblo.Domain/`.
- `dotnet build` succeeds at the solution level.

### FR-2: Remove `FamilyEntries` from the DTO
Delete the mirrored `FamilyEntries` property from `Anela.Heblo.Application.Features.Journal.Contracts.JournalIndicatorDto` and update `TotalEntries` to match the domain.

**Acceptance criteria:**
- `JournalIndicatorDto` no longer declares a `FamilyEntries` property.
- `JournalIndicatorDto.TotalEntries` is defined as `public int TotalEntries => DirectEntries;`.
- No reference to `FamilyEntries` remains in `backend/src/Anela.Heblo.Application/`.
- The generated OpenAPI schema and TypeScript client no longer expose a `familyEntries` field on indicator responses (this is automatic via the OpenAPI client generation build step; the spec only requires that the generation runs and the produced client is consistent).

### FR-3: Clean up the repository implementation
`JournalRepository.GetJournalIndicatorsAsync` currently has a single blank line gap (lines 209–210) where a family-entries query was presumably intended. Remove any orphan whitespace or dead comments left by the deletion. Do **not** change the semantics of `DirectEntries`, `LastEntryDate`, or `HasRecentEntries` calculations.

**Acceptance criteria:**
- `GetJournalIndicatorsAsync` still populates `DirectEntries`, `LastEntryDate`, and `HasRecentEntries` as before.
- No commented-out or stubbed code is added; the method is left in a coherent state.
- No new query is introduced.

### FR-4: Test coverage
The repository currently has no unit/integration test for `GetJournalIndicatorsAsync` (the test class `JournalRepositoryIntegrationTests` covers `GetEntriesByProductAsync` only). Add minimal coverage to protect against regressions.

**Acceptance criteria:**
- A new integration test in `JournalRepositoryIntegrationTests` calls `GetJournalIndicatorsAsync` for a set of product codes that includes:
  - a product code with multiple direct entries (asserts `DirectEntries` is the correct count, `TotalEntries == DirectEntries`),
  - a product code with no entries (asserts `DirectEntries == 0`, `TotalEntries == 0`, `LastEntryDate == null`, `HasRecentEntries == false`),
  - a product code whose latest direct entry is within 30 days (asserts `HasRecentEntries == true`).
- Tests follow the AAA pattern and the project's xUnit + FluentAssertions conventions.
- All new and existing tests pass via `dotnet test`.

## Non-Functional Requirements

### NFR-1: Backward compatibility
No production consumer reads `FamilyEntries` today (confirmed by codebase grep). The OpenAPI/TypeScript client regeneration will surface any indirect consumers at build time. No data-migration or versioning concerns apply.

### NFR-2: Performance
The change is purely structural and removes work that did not exist. No performance impact.

### NFR-3: Maintainability
Removing the speculative property eliminates a class of confusing-by-default behavior (always-zero field). Future reintroduction must be paired with a real, used consumer and a populating query.

### NFR-4: Build and format
`dotnet build` and `dotnet format` must succeed without warnings introduced by this change. `npm run build` must succeed (regenerates TypeScript client) and `npm run lint` must pass.

## Data Model
No schema changes. `JournalEntry`, `JournalEntryProduct`, and persisted tables are untouched.

In-memory shape after change:
- `JournalIndicator { ProductCode, DirectEntries, TotalEntries (= DirectEntries), LastEntryDate, HasRecentEntries }`
- `JournalIndicatorDto` has the same shape (still a class, not a record, per project DTO rule).

## API / Interface Design
- `IJournalRepository.GetJournalIndicatorsAsync` signature is unchanged.
- The contract type `JournalIndicatorDto` loses one property (`FamilyEntries`). The generated TypeScript client will reflect the removal.
- No new endpoints, no MediatR handlers, no controllers added.

## Dependencies
- .NET 8 backend solution (`Anela.Heblo.Domain`, `Anela.Heblo.Application`, `Anela.Heblo.Persistence`, `Anela.Heblo.Tests`).
- OpenAPI / TypeScript client generation pipeline (auto-runs on build per `docs/development/api-client-generation.md`).
- xUnit + FluentAssertions + EF Core `UseInMemoryDatabase` for tests (already in place).

## Out of Scope
- **Removing the unused `GetJournalIndicatorsAsync` method or `JournalIndicatorDto` entirely.** Although neither has a current consumer, the brief targets the `FamilyEntries`/`TotalEntries` semantics only. Wider dead-code cleanup is a separate decision and should be filed as its own arch-review item if desired.
- **Implementing a real family-entries count** (brief option 1). Deferred until a concrete consumer requires it.
- **Schema or migration changes.** None required.
- **Frontend changes.** No frontend code references the affected fields; the TypeScript client regeneration is the only frontend-side effect and is mechanical.

## Open Questions
None.

## Status: COMPLETE