I've explored the codebase. Key facts validated:
- `PagedResult<T>` (in Xcc.Persistance) is already returned by many Domain repositories — acceptable per project convention
- `ILotRepository.GetPaginatedAsync` (Catalog/Inventory) and `IEanRepository.GetPaginatedAsync` already use the primitive-parameter-list pattern proposed here
- `IMarketingActionRepository.GetPagedAsync` uses a competing pattern (`MarketingActionQueryCriteria` in Domain) — same anti-pattern as Journal, but out of scope
- Domain layer contains no `Anela.Heblo.Application` references today
- `System.ComponentModel.DataAnnotations` is used across multiple Domain entities; the deletion of the criteria files won't affect those
- Tests use Moq with `It.IsAny<JournalSearchCriteria>()` and `It.Is<JournalSearchCriteria>(...)` matchers — must convert to per-parameter matchers

```markdown
# Architecture Review: Remove Journal Query/Search Criteria from Domain Layer

## Skip Design: true

Backend-only refactor. No UI, no DTO shape changes, no new screens. The OpenAPI surface is byte-for-byte unchanged; the frontend auto-generated client requires no regeneration.

## Architectural Fit Assessment

The proposed change brings the Journal repository contract into line with the dominant convention already used in `Anela.Heblo.Domain.Features.Catalog.Inventory.ILotRepository` and `IEanRepository`, both of which accept primitive query parameters (`string?`, `DateOnly?`, `int page`, `int pageSize`) and return `PagedResult<T>`. Today's `IJournalRepository` is the outlier — it carries a separate parameter-object whose fields exist only to be mechanically copied from an Application `Request` into a Domain `Criteria` and then read in `JournalRepository` (Persistence). The intermediate object adds zero semantic value and the `[MaxLength(...)]` data annotations on `JournalSearchCriteria` re-state a validation that already lives on `SearchJournalEntriesRequest`.

Integration points are narrow and well-scoped:

1. `Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs` — interface change (only two methods)
2. `Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` — implementation change (signature only; LINQ body translates 1:1)
3. `Anela.Heblo.Application/.../GetJournalEntriesHandler.cs` and `SearchJournalEntriesHandler.cs` — call site updates (delete the criteria allocation)
4. `Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs` — Moq matcher updates

There are no cross-module fan-outs. `JournalQueryCriteria` and `JournalSearchCriteria` are referenced only inside the Journal slice (verified by `grep`: 7 files, all listed in the spec). The Application contracts (`GetJournalEntriesRequest`, `SearchJournalEntriesRequest`) are unchanged, which preserves the HTTP/OpenAPI surface and FluentValidation/data-annotation behavior at the model-binding boundary.

The chosen approach (primitive parameter list) honors the project's Vertical Slice + Clean Architecture rules:
- Domain depends only on `Xcc.Persistance` (already its baseline)
- Domain no longer imports `System.ComponentModel.DataAnnotations` on behalf of these specific files
- Application owns pagination/sort/filter validation (via the `Request` types)
- Repository implementation owns query translation (LINQ shape, magic-string sort keys)

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│  HTTP / Controller                                                  │
│   GET /api/journal                 POST /api/journal/search         │
└───────────────────────┬───────────────────────┬─────────────────────┘
                        │                       │
                  binds to                binds to
                        │                       │
                        ▼                       ▼
┌─────────────────────────────────────────────────────────────────────┐
│ Application.Features.Journal.Contracts                              │
│   GetJournalEntriesRequest         SearchJournalEntriesRequest      │
│   (PageNumber, PageSize,           ([MaxLength(200)] SearchText,    │
│    SortBy, SortDirection)          DateFrom/To, ProductCodePrefix,  │
│                                    TagIds, [MaxLength(100)]         │
│                                    CreatedByUserId, paging, sort)   │
└───────────────────────┬───────────────────────┬─────────────────────┘
                        │ MediatR Send          │ MediatR Send
                        ▼                       ▼
┌─────────────────────────────────────────────────────────────────────┐
│ Application.Features.Journal.UseCases                               │
│   GetJournalEntriesHandler         SearchJournalEntriesHandler      │
│   ┌─────────────────────────────────────────────────────────────┐   │
│   │ (deleted)                                                   │   │
│   │   new JournalQueryCriteria { ... }                          │   │
│   │   new JournalSearchCriteria { ... }                         │   │
│   └─────────────────────────────────────────────────────────────┘   │
│   passes request fields as named parameters →                       │
└───────────────────────┬─────────────────────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────────────────────┐
│ Domain.Features.Journal                                             │
│   IJournalRepository : IRepository<JournalEntry, int>               │
│     GetEntriesAsync(int, int, string, string, CT)                   │
│     SearchEntriesAsync(string?, DateTime?, DateTime?, string?,      │
│                        IReadOnlyCollection<int>?, string?,          │
│                        int, int, string, string, CT)                │
│                                                                     │
│   (DELETED) JournalQueryCriteria.cs                                 │
│   (DELETED) JournalSearchCriteria.cs                                │
└───────────────────────┬─────────────────────────────────────────────┘
                        │ DI binds IJournalRepository → JournalRepository
                        ▼
┌─────────────────────────────────────────────────────────────────────┐
│ Persistence.Catalog.Journal.JournalRepository                       │
│   GetEntriesAsync / SearchEntriesAsync                              │
│   — bind parameters to local vars                                   │
│   — apply IsDeleted filter, includes, filters, sorting, paging      │
│   — return PagedResult<JournalEntry>                                │
└─────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Primitive-parameter list at the repository boundary (not a Domain-owned query record)

**Options considered:**
- **A. Primitive parameter list (chosen, per spec FR-2):** `GetEntriesAsync(int pageNumber, int pageSize, string sortBy, string sortDirection, CT)` and 11-parameter `SearchEntriesAsync`. No new types in Domain.
- **B. Replace with a Domain-internal `record JournalQuery(...)` / `JournalSearchQuery(...)` (no DataAnnotations, no `IRequest<>`).** Same shape as today's criteria but with the validation-attribute coupling stripped and a different, narrower intent (query parameters belong to Domain because the *repository* is in Domain).
- **C. Pass the Application `Request` types directly to the repository.** Smallest diff, but it inverts the dependency: Domain would have to import `Anela.Heblo.Application` — an outright Clean Architecture violation.

**Chosen approach:** A. Primitive parameter list, matching `ILotRepository` / `IEanRepository`.

**Rationale:**
- Eliminates the duplicated parameter carrier without re-introducing a parameter-object that future readers will want to "enhance" with attributes again (the same drift that produced the current bug).
- Consistent with existing Inventory repositories — a developer reading `IJournalRepository` after this change will recognize the pattern.
- YAGNI: option B would re-add a type that does not justify its existence for a single caller. If a second caller appears later (e.g., an internal job that lists entries), the refactor to a record can happen then.
- Option C is rejected outright on layering grounds.

**Trade-off accepted:** `SearchEntriesAsync` ends up with 11 parameters + `CancellationToken`. This is on the high end and would normally invite a parameter-object. The brief explicitly calls this out as "the smallest fix"; the spec accepts the smell. If a sibling repository follows the same trajectory in another module, that is the moment to introduce a shared Domain-side query record convention — not now.

#### Decision 2: Preserve all magic-string and case-sensitivity semantics in the sort branch

**Options considered:**
- **A. Preserve exactly (chosen, per FR-3):** `sortBy?.ToLower()` switch over `"title"`, `"createdat"`, default to `"entrydate"`; `sortDirection == "ASC"` → asc, otherwise desc.
- **B. Replace strings with an enum** (`SortBy`, `SortDirection`) at the repository boundary, with Application mapping from request strings.

**Chosen approach:** A.

**Rationale:** The brief and spec explicitly forbid behavioral changes. The external API takes strings (no enum on the wire). An enum at the repository boundary would force a translation step on the handler — i.e., reintroduce the exact "pointless translation" anti-pattern this work removes. Enum-isation is a legitimate follow-up but **must** include the Application contracts and the OpenAPI surface (and therefore a regenerated TS client), which the brief excludes.

**Trade-off accepted:** The magic-string switch remains in Persistence. The default branch silently swallows typos (any unrecognized `sortBy` value sorts by `EntryDate`). This is unchanged from today.

#### Decision 3: Retain `[MaxLength]` only on the Application `Request`

**Options considered:**
- **A. Validation lives on `SearchJournalEntriesRequest` (chosen, per NFR-4):** ASP.NET model-binding validates at the controller boundary.
- **B. Add a guard clause in the repository.**

**Chosen approach:** A.

**Rationale:** The model-binding pipeline already enforces `[MaxLength]` before the handler runs. Adding repository-side guards duplicates validation and pulls validation concerns into Persistence. Spec assumption #3 is correct as stated.

## Implementation Guidance

### Directory / Module Structure

No new directories. No new files. Only deletions and in-place modifications:

**Delete:**
- `backend/src/Anela.Heblo.Domain/Features/Journal/JournalQueryCriteria.cs`
- `backend/src/Anela.Heblo.Domain/Features/Journal/JournalSearchCriteria.cs`

**Modify:**
- `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs`
- `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs`
- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/GetJournalEntries/GetJournalEntriesHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs`

No `.csproj` modifications. `System.ComponentModel.DataAnnotations` remains transitively available in Domain via the BCL and is used by other entities (e.g., `JournalEntry.cs`, `JournalEntryTag.cs`, `JournalEntryProduct.cs`) — verified via `grep`.

### Interfaces and Contracts

The single source of truth after the change is the interface in spec §"Repository interface (after)". Use it verbatim. Implementers MUST:

1. Keep `IJournalRepository : IRepository<JournalEntry, int>` (Xcc abstraction).
2. Use `IReadOnlyCollection<int>?` for `tagIds` — not `List<int>?`, not `int[]?`. This signals read-only intent and is consistent with the spec.
3. Keep `string sortBy` and `string sortDirection` as non-nullable on `GetEntriesAsync` (handler always passes the request's default-valued strings); the implementation already null-guards via `sortBy?.ToLower()`, so leave that defensive null-conditional in place — it costs nothing and matches today's behavior.
4. Do not add new methods. The four existing methods on `IJournalRepository` are the full surface (FR-2 acceptance criterion).

### Data Flow

```
GET /api/journal?pageNumber=2&pageSize=20&sortBy=Title&sortDirection=DESC
  → MVC model-binds → GetJournalEntriesRequest { PageNumber=2, PageSize=20, ... }
  → MediatR.Send → GetJournalEntriesHandler.Handle
  → _journalRepository.GetEntriesAsync(2, 20, "Title", "DESC", ct)
    → JournalRepository builds IQueryable<JournalEntry>
       (Where IsDeleted=false; Include associations & tag assignments)
    → ToLower() switch on "Title" → OrderByDescending(x => x.Title)
    → CountAsync → 47
    → Skip(20).Take(20).ToListAsync → page-2 items
    → return PagedResult<JournalEntry> { Items, TotalCount=47, PageNumber=2, PageSize=20 }
  → Handler maps each entry via JournalEntryMapper.ToDto
  → return GetJournalEntriesResponse { Entries, TotalCount=47, TotalPages=3, HasNextPage=true, HasPreviousPage=true }
```

For `SearchEntriesAsync`, the flow is identical with the additional filter clauses applied in the same order they appear today (text → date → product prefix → tag → user → sort → page).

**Critical preserved behaviors** (validate via SQL spot-check during implementation):
- `ProductCodePrefix` filter: `criteria.ProductCodePrefix.StartsWith(pa.ProductCodePrefix)` — the request value StartsWith the stored prefix (counter-intuitive direction; do not "fix").
- `searchText`: trimmed, ToLower, `Contains` on `Title` and `Content`.
- Sort default: any unrecognized `sortBy` (including `""` or null) → `EntryDate`.
- Sort direction default: anything other than `"ASC"` → descending.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Test refactor accidentally weakens coverage by switching from `It.Is<Criteria>(...)` to `It.IsAny<>()` everywhere | Medium | For every Moq setup that previously asserted a specific criteria field, the new test MUST assert the equivalent parameter via `It.Is<T>(...)` on that named parameter. Reviewer should diff the Verify(...) calls 1:1 with the old ones. |
| Sort parameter order in the new `SearchEntriesAsync` signature is mis-wired at the handler call site (positional, 11 args) | Medium | Always invoke with named arguments in the handler (`searchText: request.SearchText, ...`). The spec doesn't mandate this, but with 11 same-typed-looking parameters it is the cheapest defense against silent argument-position bugs. Add this as a hard rule in the implementation plan. |
| Removing the criteria object silently changes EF-translated SQL (e.g., null-handling of `IReadOnlyCollection<int>?` vs `List<int>?` in the `.Contains` filter) | Low | Spot-check generated SQL via EF logging for: (a) `tagIds = null`, (b) `tagIds = empty list`, (c) `tagIds = [1,2,3]`. Compare to today's output. EF Core translates `IReadOnlyCollection<int>.Contains` identically to `List<int>.Contains` when the runtime type is still `List<int>`. |
| `[MaxLength]` annotations no longer reach the repository — a misbehaving caller that bypasses the controller could now pass arbitrarily long `SearchText` | Low | Acceptable. Per spec assumption #3, validation belongs at the API boundary. The repository never validated independently; the `[MaxLength]` on the deleted Criteria was dead code (DataAnnotations are not enforced just by their presence on a class). |
| Future maintainer re-introduces a Domain-side criteria type "for tidiness" | Low | Add a one-line code comment at the top of `IJournalRepository.cs`? No — per CLAUDE.md, no commentary. Instead, encode the rule via a follow-up arch-review finding for the parallel `MarketingActionQueryCriteria` in `Domain.Features.Marketing` (out of scope for this PR but worth tracking). |
| Long parameter list invites copy-paste errors in callers added later | Low | Only one caller exists (`SearchJournalEntriesHandler`). If a second caller is added, that PR should refactor to a Domain-side query record at that point — not pre-emptively here. |

## Specification Amendments

The spec is fundamentally sound. Two clarifications/additions:

1. **FR-5 amendment — named arguments are required at the call site.** Update FR-5 acceptance criterion to read:
   > The handler invokes `_journalRepository.SearchEntriesAsync(...)` **passing each `request.*` field as a named argument** (e.g., `searchText: request.SearchText, dateFrom: request.DateFrom, ...`). Positional invocation is forbidden for this method because positional 11-argument calls are silently broken by argument reordering during future maintenance.

   This is a cheap, durable safety net and aligns with the spirit of the brief's "smallest fix" framing while removing the largest residual risk.

2. **FR-2 amendment — confirm `tagIds` parameter type.** The spec already specifies `IReadOnlyCollection<int>?`. Confirm in the implementation PR that the handler call uses `request.TagIds` (which is `List<int>?`) and that the implicit conversion compiles without ceremony. (It does: `List<T>` implements `IReadOnlyCollection<T>`. No conversion needed.)

3. **NFR-2 amendment — explicit Domain-csproj check.** Add a verification step:
   > Run `grep -R "Anela.Heblo.Application" backend/src/Anela.Heblo.Domain` after the change. Output MUST be empty. (Today it already is; the check ensures no regression.)

## Prerequisites

None. This is a pure refactor with no infrastructure, configuration, migration, or DI registration changes:

- `Anela.Heblo.Domain.csproj` — unchanged
- `Anela.Heblo.Persistence.csproj` — unchanged
- `Anela.Heblo.Application.csproj` — unchanged
- DI registration in `JournalModule.cs` — unchanged (interface identity preserved; only method signatures change)
- Database schema — unchanged
- Migrations — none
- Azure Key Vault secrets — N/A
- Feature flags — N/A
- OpenAPI / TypeScript client regeneration — N/A (public schema unchanged)

Implementation can start immediately. The validation sequence at completion is:
1. `dotnet build` clean (no new warnings)
2. `dotnet format` clean
3. `dotnet test --filter "FullyQualifiedName~Journal"` green
4. `grep -r "JournalQueryCriteria\|JournalSearchCriteria" backend/src` returns nothing
5. `grep -R "Anela.Heblo.Application" backend/src/Anela.Heblo.Domain` returns nothing
6. Optional: enable EF SQL logging once, hit a representative search endpoint, confirm SQL is byte-identical to the previous implementation (NFR-1).
```