# Architecture Review: Refactor GetClassificationHistoryHandler to Use IMapper

## Skip Design: true
Backend-only, behavior-preserving internal refactor. No new or changed UI components, screens, or visual decisions.

## Architectural Fit Assessment
This refactor increases alignment with existing patterns rather than introducing anything new. The sibling `GetClassificationRulesHandler` in the same module already injects `IMapper` and calls `_mapper.Map<List<...Dto>>(...)`, and `InvoiceClassificationMappingProfile` already defines `CreateMap<ClassificationHistory, ClassificationHistoryDto>()` in full — including the two non-conventional members (`InvoiceId ← AbraInvoiceId`, `RuleName ← ClassificationRule.Name`) that the manual projection replicates. The handler is currently the sole consumer of that map that bypasses it. The change removes duplicated mapping logic and consolidates it in the one place the guidelines and existing code already treat as authoritative (development_guidelines.md lists AutoMapper as the mechanism for DTO↔Domain mapping). Integration points are minimal: DI (IMapper already registered and resolved elsewhere), the mapping profile (unchanged), and the repository call (unchanged).

## Proposed Architecture

### Component Overview
```
GetClassificationHistoryRequest
        │
        ▼
GetClassificationHistoryHandler
   ├── IClassificationHistoryRepository ──> (historyItems, totalCount)
   ├── IMapper ──> Map<List<ClassificationHistoryDto>>(historyItems)   [NEW dependency]
   └── ILogger
        │
        ▼
GetClassificationHistoryResponse { Items, TotalCount, Page, PageSize }

InvoiceClassificationMappingProfile
   └── CreateMap<ClassificationHistory, ClassificationHistoryDto>()  [reused, unchanged]
```

### Key Design Decisions

#### Decision 1: Reuse the existing profile map via IMapper, do not add a projection map
**Options considered:**
(a) Inject `IMapper` and call `_mapper.Map<List<ClassificationHistoryDto>>(historyItems)`.
(b) Use `_mapper.ProjectTo<ClassificationHistoryDto>` for IQueryable server-side projection.
**Chosen approach:** (a).
**Rationale:** The repository returns already-materialized items (`(historyItems, totalCount)` from `GetPagedHistoryAsync`), not an `IQueryable`, so `ProjectTo` does not apply. Option (a) matches the exact pattern of `GetClassificationRulesHandler` (`_mapper.Map<List<...Dto>>(...)`), keeping the module internally consistent. Scope stays surgical — the profile is untouched.

#### Decision 2: Keep constructor parameter ordering per spec, not per sibling
**Options considered:** Match sibling's `(repository, mapper)` order vs. spec's `(historyRepository, mapper, logger)`.
**Chosen approach:** Follow the spec: `(IClassificationHistoryRepository historyRepository, IMapper mapper, ILogger<GetClassificationHistoryHandler> logger)`.
**Rationale:** Constructor parameter order is irrelevant to DI resolution (resolved by type), and the spec's ordering preserves the existing `logger`-last convention in this handler. No behavioral difference.

## Implementation Guidance

### Directory / Module Structure
No new files. Single file edited:
`backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetClassificationHistory/GetClassificationHistoryHandler.cs`

### Interfaces and Contracts
- Constructor becomes `(IClassificationHistoryRepository historyRepository, IMapper mapper, ILogger<GetClassificationHistoryHandler> logger)`; add `private readonly IMapper _mapper;`.
- Add `using AutoMapper;`.
- `ClassificationHistoryDto` and the `CreateMap<ClassificationHistory, ClassificationHistoryDto>()` configuration are the contract of record — do not change either. The DTO stays a class per the project rule; unaffected here.
- HTTP contract (`GetClassificationHistoryResponse` envelope) is untouched.

### Data Flow
1. Handler calls `_historyRepository.GetPagedHistoryAsync(...)` → `(historyItems, totalCount)`. **Unchanged.**
2. Replace the 17-line `Select(... new ClassificationHistoryDto { ... }).ToList()` with:
   `var historyDtos = _mapper.Map<List<ClassificationHistoryDto>>(historyItems);`
3. AutoMapper populates the 14 properties: 12 by name convention, plus `InvoiceId ← AbraInvoiceId` and `RuleName ← ClassificationRule?.Name` (null-safe) from the profile's explicit `ForMember` rules.
4. Response envelope assembled identically.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Silent output drift — an AutoMapper convention maps a property differently than the manual code, or leaves a field default | Low | Confirm the profile map is validated (`configuration.AssertConfigurationIsValid()`); add/keep a handler or profile unit test asserting all 14 fields, especially `InvoiceId` and null `RuleName` when `ClassificationRule` is null |
| `ClassificationRule` navigation not loaded, causing `RuleName` to differ from manual `?.` behavior | Low | Behavior is identical — profile uses `src.ClassificationRule != null ? ... : null`, matching the manual `?.`; no repository/include change is in scope |
| Empty `historyItems` handling | Low | `_mapper.Map<List<...>>` on an empty enumerable returns an empty list — equivalent to `.Select().ToList()` |

## Specification Amendments
None required. The spec is accurate and complete. One clarification worth capturing during implementation: the two explicit `ForMember` rules in `InvoiceClassificationMappingProfile` (`InvoiceId`, `RuleName`) are what guarantee FR-2 equivalence — the reviewer should verify these against the manual assignments as the equivalence proof, since the other 12 properties rely on name-convention mapping.

## Prerequisites
None. `IMapper` is already registered in DI (consumed by `GetClassificationRulesHandler` and others) and the `ClassificationHistory → ClassificationHistoryDto` map already exists in `InvoiceClassificationMappingProfile`. No migration, config, or infrastructure change is needed.
