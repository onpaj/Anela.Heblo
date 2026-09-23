# Architecture Review: Move BankStatementImportDto.ErrorType derivation out of the DTO and into BankMappingProfile

## Skip Design: true
Pure backend refactor: no new, changed, or removed UI components, screens, or layouts. The only frontend-visible effect is that the generated OpenAPI/TypeScript client's `errorType` field gains a setter — no frontend source change is required or in scope (see spec FR-4/NFR-4, Dependencies). The designer phase would have nothing to add.

## Architectural Fit Assessment
This aligns cleanly with an existing, explicit project rule: `docs/architecture/development_guidelines.md` § "Contracts and DTOs Rules" states DTOs in `contracts/` are data-only and that business/domain decisions belong in handlers or mapping profiles. Today's `BankStatementImportDto.ErrorType` computed property (`ImportResult != ImportStatus.Success ? ImportResult : null`) is the sole violator of this rule in the Bank module's Contracts — every sibling DTO in `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/` (`BankStatementImportResultDto`) is a plain data bag with no domain references or computed members.

I verified the integration surface directly:
- Both consumers of `BankStatementImportDto` — `GetBankStatementListHandler.Handle` (line 50: `_mapper.Map<List<BankStatementImportDto>>(items)`) and `GetBankStatementByIdHandler.Handle` (line 37: `_mapper.Map<BankStatementImportDto>(entity)`) — populate the DTO **exclusively** through `IMapper`. There is no code path anywhere in the codebase that `new BankStatementImportDto { ... }`-constructs this type directly and expects the getter to compute `ErrorType` for it outside the mapper. This means `BankMappingProfile` is a safe, complete choke point for the fix — no handler changes are required.
- `BankMappingProfile.cs` currently has a bare `CreateMap<BankStatementImport, BankStatementImportDto>();` with no `ForMember` calls (confirmed by reading the file and by `docs/superpowers/plans/2026-06-03-remove-dead-bankmappingprofile-errortype-formember.md`, which documents that a prior refactor removed a `ForMember(dest => dest.ErrorType, ...)` because AutoMapper **silently ignores `ForMember` calls that target a read-only/get-only destination member** — it does not throw or warn, it just does nothing). That historical fact is the crux of this review's one real risk (see Risks table) and is why `AssertConfigurationIsValid()` in `BankMappingProfileTests.cs` is the key regression guard: it fails loudly if any destination member ends up unmapped, and today it does **not** catch a getter-only `ForMember` typo (AutoMapper treats a `ForMember` rule against a getter-only property as valid config, not a validation error) — so the *only* thing that actually prevents silently regressing to the old no-op state is that `ErrorType` **must** become settable first (FR-1), *then* `ForMember` is added (FR-2). Order matters at the file-content level even though both edits land in one PR.
- `ImportStatus` (`backend/src/Anela.Heblo.Domain/Features/Bank/ImportStatus.cs`) is a small `static class` of three `const string` values. `BankMappingProfile.cs` already has `using Anela.Heblo.Domain.Features.Bank;` (needed for `BankStatementImport` itself), so referencing `ImportStatus.Success` there requires no new `using`.
- I confirmed via repo-wide search that `ErrorType`/`errorType` has no other production usage outside this module: `BankStatementImportDto.cs`, `BankMappingProfile.cs`, `BankMappingProfileTests.cs`, `GetBankStatementByIdHandlerTests.cs`, and the frontend's `ImportTab.tsx` (read-only display) plus the generated `frontend/src/api/generated/api-client.ts`. No other DTO, handler, or module is touched by this change.

## Proposed Architecture

### Component Overview
```
BankStatementImport (Domain entity)
        │  ImportResult: string
        ▼
BankMappingProfile (Application, AutoMapper Profile)
    CreateMap<BankStatementImport, BankStatementImportDto>()
        .ForMember(dest => dest.ErrorType, opt => opt.MapFrom(src =>
            src.ImportResult != ImportStatus.Success ? src.ImportResult : null))
        │
        ▼
BankStatementImportDto (Application, Contracts — pure data)
    ErrorType: string? { get; set; }   ◄── plain auto-property, no domain reference
        │
        ├── GetBankStatementListHandler ──► GetBankStatementListResponse.Items
        └── GetBankStatementByIdHandler ──► direct return
                │
                ▼
        OpenAPI schema / generated TS client
            errorType: string | undefined   (now a settable field, not derived-only)
```
No new components. The only structural move is which layer computes `ErrorType`: DTO getter → AutoMapper `ForMember` in the existing mapping profile.

### Key Design Decisions

#### Decision 1: Where the success/failure rule lives
**Options considered:**
1. Leave the rule in the DTO getter (status quo) — rejected, this is exactly what issue #4279 flags as a violation.
2. Move the rule into each handler (`GetBankStatementListHandler`, `GetBankStatementByIdHandler`), setting `dto.ErrorType` manually after mapping — rejected: duplicates the rule in two places, and handlers would need to loop over `dtoList` in the list handler, adding boilerplate AutoMapper already exists to avoid.
3. Move the rule into `BankMappingProfile` via `ForMember`/`MapFrom` (issue's suggested fix) — **chosen**. Single location, already the project's established pattern for DTO field derivation (`AutoMapper | DTO ↔ Domain mapping` is explicitly called out as a supported tool in `development_guidelines.md`), and it is a straight reversal of the exact `ForMember` this codebase already had before the (now-superseded) 2026-06-03 no-op-removal refactor — so it is a known-good, previously-tested code shape, not a novel pattern.

**Chosen approach:** `BankMappingProfile`'s single `CreateMap<BankStatementImport, BankStatementImportDto>()` gains one `.ForMember(dest => dest.ErrorType, opt => opt.MapFrom(src => src.ImportResult != ImportStatus.Success ? src.ImportResult : null))` call, exactly as specified in the issue and in FR-2.

**Rationale:** Keeps the domain decision ("success" = `ImportStatus.Success`) in the Application layer's mapping/orchestration concern, per the project's own documented rule, without introducing any new abstraction, service, or file.

#### Decision 2: DTO property shape after the fix
**Options considered:**
1. Keep `ErrorType` as `{ get; }` only (no setter) and have AutoMapper... — not viable. AutoMapper's default `MapFrom` configuration requires a settable destination member; a get-only property cannot be a `ForMember` target that actually executes (this is precisely the silent-no-op bug documented in the 2026-06-03 plan). A getter-only property with `ForMember` is architecturally self-defeating for this fix.
2. `public string? ErrorType { get; set; }` — plain nullable auto-property — **chosen**, exactly as specified in the issue and FR-1.

**Rationale:** This is the only shape that (a) lets AutoMapper's `ForMember` actually apply, (b) removes the `ImportStatus` domain reference from the DTO file entirely, and (c) fixes the OpenAPI client's read-only-field problem described in the issue, since NSwag/the client generator emits a setter-bearing property as a normal writable field.

## Implementation Guidance

### Directory / Module Structure
No new files, no new directories. Two existing files are edited:
- `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs`
- `backend/src/Anela.Heblo.Application/Features/Bank/BankMappingProfile.cs`

One existing test file gains one new `[Fact]` (per spec FR-4):
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs`

### Interfaces and Contracts
`BankStatementImportDto` public shape (unchanged field set, changed mutability of one field):
```csharp
public class BankStatementImportDto
{
    public int Id { get; set; }
    public string TransferId { get; set; } = null!;
    public DateTime StatementDate { get; set; }
    public DateTime ImportDate { get; set; }
    public string Account { get; set; } = null!;
    public string Currency { get; set; } = null!;
    public int ItemCount { get; set; }
    public string ImportResult { get; set; } = null!;
    public string? ErrorType { get; set; }
}
```
No `using Anela.Heblo.Domain.Features.Bank;` remains in this file — nothing else in it references the Domain namespace.

`BankMappingProfile` gains the `ForMember` rule inside the existing `CreateMap` call (do not create a second `CreateMap<BankStatementImport, BankStatementImportDto>()` — AutoMapper only needs, and only allows sensibly, one map per type pair):
```csharp
CreateMap<BankStatementImport, BankStatementImportDto>()
    .ForMember(dest => dest.ErrorType,
        opt => opt.MapFrom(src =>
            src.ImportResult != ImportStatus.Success ? src.ImportResult : null));
```

### Data Flow
Identical to today for both existing call sites — only the point at which `ErrorType` is computed moves:
1. `BankStatementImportRepository` (or `GetByIdAsync`) loads a `BankStatementImport` entity with `ImportResult` set (`"OK"` / `"PROCESSING_ERROR"` / `"UNKNOWN_ERROR"` / any other stored value).
2. Handler calls `_mapper.Map<...>(...)`.
3. AutoMapper's new `ForMember` rule evaluates `src.ImportResult != ImportStatus.Success` and sets `dest.ErrorType` accordingly, alongside its existing implicit member-name-matched mappings for every other property.
4. Handler returns the DTO (or a `List<>`/wrapping response of it) unchanged from today.
5. Serialization to JSON / the OpenAPI schema is unaffected in shape (still `errorType?: string`); only the schema's `readOnly` flag on that property is expected to disappear, since the property is no longer computed-only in the CLR type (see NFR-4 — this is the intended fix, not a defect to prevent).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Reintroducing the exact "silent no-op `ForMember`" bug the project already hit once (2026-06-03 plan) if the DTO property is left get-only while `ForMember` is added | High | Sequence FR-1 before FR-2 within the same change: the DTO property **must** be a settable auto-property before the `ForMember` rule is added. This project has direct, documented precedent (`docs/superpowers/plans/2026-06-03-remove-dead-bankmappingprofile-errortype-formember.md`) that AutoMapper does **not** fail fast here — a `ForMember` rule targeting a get-only destination member is silently accepted at configuration time and silently never executes at map time, with `AssertConfigurationIsValid()` passing regardless. This means the ordering mistake would NOT be caught by `BankMappingProfileTests.Profile_Configuration_IsValid`; it would only surface if the two mapper-based `ErrorType`-derivation tests happened to still pass by coincidence (they would, since the untouched getter would still compute the right value) — masking the fact that the new `ForMember` rule is dead code. The planner must therefore land both edits in the same buildable step/commit (DTO becomes settable **and** `ForMember` is added together), and the new FR-4 settability test (`ErrorType` set directly on the DTO) is what actually proves the property is a real, working setter rather than relying on test coincidence. |
| OpenAPI/TypeScript client diff under `frontend/src/api/generated/` (or wherever it is emitted) is flagged as a regression by an automated check that (incorrectly, for this change) expects a byte-identical client, mirroring the stricter NFR-4 used in the two prior Bank DTO refactors | Medium | Spec NFR-4 explicitly documents that a client diff for `errorType` (read-only → settable) is **expected and intentional** for this change, unlike the two prior plans. The planner should not carry forward a "no client diff" verification gate from those prior plans' templates without amending it to allow exactly this one field's mutability change. |
| A future contributor mistakes the `ForMember` rule for dead code (as happened once already with the exact-opposite direction) and removes it, silently reverting `ErrorType` to always `null` (since the destination member has no implicit source with a matching name and type once it's just `string? ErrorType { get; set; }` and the mapper's default convention only auto-maps identically named source members — there is no `ErrorType` member on `BankStatementImport`) | Medium | Existing `BankMappingProfileTests` non-null assertions for the non-success case (`ErrorType.Should().Be("Failed")`) already fail loudly if the `ForMember` rule is removed, since AutoMapper's convention-based mapping has no `ErrorType`-named source member to fall back to — this is a stronger regression guard than the DTO-getter version ever had, since there `ErrorType` would still compute correctly with the rule absent. Note this in the mapping profile's rationale so a future arch-review or contributor does not misread it as dead code. |
| Any other code constructs `BankStatementImportDto` directly (bypassing the mapper) and relied on the getter computing `ErrorType` automatically from `ImportResult` | Low | Confirmed via repo-wide search: no production code does this. Both handlers exclusively use `_mapper.Map(...)`. |

## Specification Amendments
1. **NFR-4 correction/emphasis** (spec already states this, restating for planner visibility): a generated-client diff limited to `errorType`'s read-only-ness is expected. The planner must not add a "verify zero client diff" gate analogous to the one in `docs/superpowers/plans/2026-06-03-bank-importdto-importstatus-constant.md` Task 5 — that gate is inverted for this change and would fail the build on the exact intended outcome.
2. **Edit ordering within Task Structure**: the planner should land the DTO edit (FR-1, make `ErrorType` settable) and the `BankMappingProfile` edit (FR-2, add `ForMember`) together, verified by the build and full test run only after both are in place — per the Risks table, AutoMapper does *not* fail fast if `ForMember` is added while the destination member is still get-only (it silently no-ops, per documented project history), so there is no compiler or config-time signal to lean on for ordering; the FR-4 settability test is the actual proof that both edits took effect correctly.
3. **FR-4's new settability test**: confirm the new `[Fact]` in `BankMappingProfileTests.cs` constructs `BankStatementImportDto` directly (no mapper involved) — its purpose is specifically to prove FR-1 (settability), which is orthogonal to and not otherwise covered by the two existing mapper-based `ErrorType` tests.

## Prerequisites
None. No migrations, no config, no infrastructure changes. The change is buildable and testable entirely within `backend/`, and the OpenAPI/TypeScript client regeneration (which happens automatically on `dotnet build` per `docs/development/api-client-generation.md`) requires no separate manual step beyond the standard build.
