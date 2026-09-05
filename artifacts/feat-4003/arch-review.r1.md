# Architecture Review: Remove duplicate SearchJournalEntryDto in favor of JournalEntryDto

## Skip Design: true

## Architectural Fit Assessment
This change is a straightforward duplication-cleanup refactor and aligns cleanly with existing conventions. Per `docs/architecture/development_guidelines.md` (§ Contracts and DTOs Rules), DTOs for a module live in that module's `Contracts/` folder and must not be shared/global across modules — consolidating two DTOs that are *both already scoped to the Journal module* onto one type does not violate this rule; it removes accidental, unjustified duplication within a single module's contract surface, which is exactly the kind of cleanup the guideline's spirit supports. There is no cross-module boundary crossing here: `JournalEntryDto` and `SearchJournalEntryDto` both live in `Features/Journal/Contracts/`, and both are consumed only by Journal use cases and Journal-related frontend components. The project's DTO-as-class rule (`CLAUDE.md`: "DTOs are classes, never C# records") is already satisfied by both types and remains satisfied after consolidation — no change needed there.

No architectural risk, no new component, no new dependency. This is a pure type/reference consolidation confined to one module's Application-layer contracts, its mapper, its handler, and its frontend generated-client consumers.

## Proposed Architecture

### Component Overview
```
Before:
  JournalEntry (domain)
        │
        ├─▶ JournalEntryMapper.ToDto()        ─▶ JournalEntryDto        ─▶ GetJournalEntry* responses, JournalList (list view)
        └─▶ JournalEntryMapper.ToSearchDto()  ─▶ SearchJournalEntryDto  ─▶ SearchJournalEntriesResponse.Entries ─▶ JournalList (search view), CatalogDetail journal tab

After:
  JournalEntry (domain)
        │
        └─▶ JournalEntryMapper.ToDto()        ─▶ JournalEntryDto        ─▶ ALL journal read paths (both list and search)
```
`SearchJournalEntryDto` and `JournalEntryMapper.ToSearchDto()` are deleted; every former consumer of the search-shaped DTO is repointed at `JournalEntryDto`.

### Key Design Decisions

#### Decision 1: Keep `JournalEntryDto` as the single surviving type (not `SearchJournalEntryDto`)
**Options considered:**
- (a) Keep `JournalEntryDto`, delete `SearchJournalEntryDto` (the brief's proposal).
- (b) Keep `SearchJournalEntryDto`, delete `JournalEntryDto`.

**Chosen approach:** (a) — keep `JournalEntryDto`.

**Rationale:** `JournalEntryDto` is the more broadly consumed and more descriptively named type — it backs the non-search entry read paths (e.g. `GetJournalEntryResponse`, `MarginsChart`/`MarginsTab`/`ProductChart`/`ChartHelpers`/`JournalEntryForm`/`JournalEntryModal` in the frontend) and carries the module's natural name (no "Search" qualifier implying it's narrower than it is). Consolidating onto it minimizes the blast radius and matches the brief and issue exactly.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. One file is deleted:
- `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/SearchJournalEntryDto.cs` — **delete**.

Files modified (backend):
- `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/SearchJournalEntriesResponse.cs` — `Entries` becomes `List<JournalEntryDto>`.
- `backend/src/Anela.Heblo.Application/Features/Journal/Mapping/JournalEntryMapper.cs` — remove `ToSearchDto()`.
- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs` — call `JournalEntryMapper.ToDto` instead of `ToSearchDto` (line ~34).

### Interfaces and Contracts
`JournalEntryTagDto` is unaffected and continues to live in `JournalEntryDto.cs` (it is already declared there, not in the file being deleted) — no consumer of `JournalEntryTagDto` needs to change.

After the OpenAPI client regeneration (`docs/development/api-client-generation.md`), the generated TypeScript client will no longer export `SearchJournalEntryDto`, and `SearchJournalEntriesResponse.entries` will be typed `JournalEntryDto[]`.

**Important scope correction to the brief:** the brief's suggested fix names only `JournalList.tsx` (line 22) as needing a frontend import update. Direct exploration of the frontend found `SearchJournalEntryDto` imported/used in **five** frontend files, not one:
- `frontend/src/components/pages/Journal/JournalList.tsx` (import + `as SearchJournalEntryDto[]` cast, line ~422)
- `frontend/src/components/pages/CatalogDetail.tsx` (import + `useState<SearchJournalEntryDto | undefined>`)
- `frontend/src/components/catalog/detail/CatalogDetailModals.tsx` (import + `selectedJournalEntry?: SearchJournalEntryDto` prop type)
- `frontend/src/components/catalog/detail/tabs/JournalTab.tsx` (import + `onEditEntry: (entry: SearchJournalEntryDto) => void` prop type)
- `frontend/src/components/catalog/detail/CatalogDetailTabs.tsx` (import + `journalEntries: SearchJournalEntryDto[]` and `onEditJournalEntry: (entry: SearchJournalEntryDto) => void` prop types)

All five must have their `SearchJournalEntryDto` import and type annotations changed to `JournalEntryDto` — a plain type-name substitution in each case (the runtime shape is identical, so no logic changes are needed, only the type reference). The planner must expand the brief's single-file scope to all five files plus the regenerated `frontend/src/api/generated/api-client.ts`.

### Data Flow
1. `SearchJournalEntriesHandler.Handle()` calls `_journalRepository.SearchEntriesAsync(...)` → gets domain `JournalEntry` items.
2. Handler maps each item via `JournalEntryMapper.ToDto` (was `ToSearchDto`) → `List<JournalEntryDto>`.
3. `SearchJournalEntriesResponse.Entries` (now `List<JournalEntryDto>`) is serialized to JSON — identical wire shape to before, since the two DTOs were structurally identical.
4. Frontend `JournalList.tsx` and the CatalogDetail journal tab consume the response through the regenerated client, now typed as `JournalEntryDto[]` throughout — no runtime behavior change, only compile-time type identity.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Frontend has more `SearchJournalEntryDto` references than the brief listed (5 files, not 1) | Low | Enumerated all five in this review (see Interfaces and Contracts); planner must create a task covering all five plus client regeneration, not just `JournalList.tsx`. |
| OpenAPI client regeneration silently missed, leaving stale generated types that still reference `SearchJournalEntryDto` | Low | Task plan must include running the documented client-generation step (`docs/development/api-client-generation.md`) as an explicit step before touching frontend consumers, and `npm run build` must be run afterward to catch any remaining stale references. |
| Existing backend unit tests (`SearchJournalEntriesHandlerTests.cs`) assert on `SearchJournalEntryDto`-typed values and fail to compile after the type is removed | Low | Verify/update this test file's type references as part of implementation; test *assertions* (property values) do not need to change, only the DTO type name if referenced explicitly. |

## Specification Amendments
- FR-4 in `spec.r1.md` should be understood as covering all five frontend files listed above (`JournalList.tsx`, `CatalogDetail.tsx`, `CatalogDetailModals.tsx`, `JournalTab.tsx`, `CatalogDetailTabs.tsx`), not just `JournalList.tsx` — the brief undercounted frontend usages. No other amendment needed; FR-1 through FR-3 and the NFRs stand as written.

## Prerequisites
None — no migrations, no config, no infrastructure changes. Implementation can start immediately: delete the DTO, update the response/mapper/handler, regenerate the OpenAPI client, update the five frontend files, run backend and frontend builds/tests.
