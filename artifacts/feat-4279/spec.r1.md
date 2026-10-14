# Specification: Move BankStatementImportDto.ErrorType derivation out of the DTO and into BankMappingProfile

## Summary
`BankStatementImportDto.ErrorType` is currently a computed, get-only property that references the domain constant `ImportStatus.Success` directly inside the Application-layer contract. This couples a pure data-transfer object to domain logic and to a specific mapping rule ("what does success look like?"), and causes the OpenAPI/TypeScript client to emit `errorType` as a read-only field. This spec converts `ErrorType` into a plain nullable auto-property and moves the success/failure derivation into `BankMappingProfile`, restoring the DTO to a pure data container per the project's DTO/contract rules.

## Background
`docs/architecture/development_guidelines.md` states that DTOs in `contracts/` are data-only, that business decisions belong in MediatR handlers or mapping profiles, and that DTOs must never leak domain coupling. `BankStatementImportDto` (in `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs`) currently violates this by computing `ErrorType` from `ImportResult != ImportStatus.Success`, which:
- Imports `Anela.Heblo.Domain.Features.Bank.ImportStatus` directly into a contract type.
- Emits `errorType` as a read-only computed property in the generated OpenAPI/TypeScript client (`errorType: string | undefined`), which cannot be round-tripped by any client that tries to set it.
- Silently changes its own serialization contract if `ImportStatus.Success` is ever renamed or split into multiple success codes.

This same file has already been through two related, narrower refactors recorded in `docs/superpowers/plans/`:
- `2026-06-03-remove-dead-bankmappingprofile-errortype-formember.md` removed a no-op `ForMember(dest => dest.ErrorType, ...)` AutoMapper rule (AutoMapper silently ignores `ForMember` targeting a read-only/get-only destination member), making the DTO getter the sole source of truth.
- `2026-06-03-bank-importdto-importstatus-constant.md` replaced the `"OK"` literal in the DTO getter with the `ImportStatus.Success` constant.

This spec reverses the direction taken by those two prior plans: it reintroduces a real, effective `ForMember` mapping rule in `BankMappingProfile`, and it removes the `ImportStatus` domain reference from the DTO entirely by turning `ErrorType` into a plain settable auto-property. This is intentional and is the fix requested by GitHub issue #4279.

## Functional Requirements

### FR-1: `BankStatementImportDto.ErrorType` becomes a plain nullable auto-property
`BankStatementImportDto.cs` must declare:
```csharp
public string? ErrorType { get; set; }
```
The `using Anela.Heblo.Domain.Features.Bank;` directive (needed only for the removed `ImportStatus` reference) must be removed from this file, since no other member of the DTO uses it.

**Acceptance criteria:**
- `BankStatementImportDto` no longer references `ImportStatus` anywhere.
- `BankStatementImportDto` no longer has a `using Anela.Heblo.Domain.Features.Bank;` directive.
- `ErrorType` has both a public getter and a public setter.

### FR-2: `BankMappingProfile` derives `ErrorType` during mapping
`BankMappingProfile.cs`'s `CreateMap<BankStatementImport, BankStatementImportDto>()` must add a `.ForMember(dest => dest.ErrorType, ...)` rule that reproduces the exact current runtime semantics:
```csharp
.ForMember(dest => dest.ErrorType,
    opt => opt.MapFrom(src =>
        src.ImportResult != ImportStatus.Success ? src.ImportResult : null))
```

**Acceptance criteria:**
- When `ImportResult == ImportStatus.Success` ("OK"), the mapped DTO's `ErrorType` is `null`.
- When `ImportResult != ImportStatus.Success`, the mapped DTO's `ErrorType` equals `ImportResult`.
- This holds identically through both existing consumers of the mapping: `GetBankStatementListHandler` (`_mapper.Map<List<BankStatementImportDto>>(items)`) and `GetBankStatementByIdHandler` (`_mapper.Map<BankStatementImportDto>(entity)`), which must keep producing byte-identical `ErrorType` values for the same entity (this equivalence is already covered by the existing `Handle_ProducesSameDtoAsListHandlerMapping_ForSameEntity` test).

### FR-3: No behavior change from the caller/consumer's point of view
This is a pure structural refactor. The externally observable value of `ErrorType` for any given `ImportResult` must be unchanged before and after this change.

**Acceptance criteria:**
- `ImportResult == "OK"` ⇒ `ErrorType == null` (unchanged).
- `ImportResult == "PROCESSING_ERROR"` ⇒ `ErrorType == "PROCESSING_ERROR"` (unchanged).
- Any other non-success `ImportResult` string ⇒ `ErrorType == ImportResult` (unchanged).

### FR-4: Test suite updated to reflect the new mapping-owned behavior
The existing `BankMappingProfileTests.cs` already asserts `ErrorType` derivation through the real `IMapper` built from `BankMappingProfile` (not a hand-constructed DTO), so its two `ErrorType`-asserting tests continue to be valid regression coverage for FR-2/FR-3 without needing behavioral changes. Because `ErrorType` becomes settable, add a `[Fact]` in the same file that directly constructs a `BankStatementImportDto` and sets `ErrorType` via its new setter, verifying the property round-trips a value — this is the concrete proof that the OpenAPI/TypeScript client's "silently loses the field" problem described in the issue is resolved at the DTO layer for future callers that round-trip the DTO.

**Acceptance criteria:**
- `BankMappingProfileTests.Profile_Configuration_IsValid` still passes (guards against reintroducing an unmapped-destination-member failure).
- The two existing `ErrorType`-derivation tests still pass unmodified in their assertions (they read through `_mapper.Map(...)`, not the DTO directly, so they remain valid regardless of which layer performs the derivation).
- A new test proves `ErrorType` is settable on `BankStatementImportDto` directly (e.g. `new BankStatementImportDto { ErrorType = "X" }.ErrorType.Should().Be("X")`).
- `GetBankStatementByIdHandlerTests` continues to pass unmodified — all four existing test methods, including the direct `Assert.Null(result.ErrorType)` and `Assert.Equal(fromListMapping.ErrorType, fromHandler.ErrorType)` assertions, since it exercises the handler → mapper path, not the DTO getter.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance impact. Moving a single ternary expression from a property getter to an AutoMapper `ForMember`/`MapFrom` delegate is computationally equivalent — both execute the same comparison once per mapped instance.

### NFR-2: Security
No security surface is affected. No new data flows, no new trust boundaries, no client-controllable input introduced (the DTO is only ever populated server-side by the mapper; nothing in this change makes any handler bind `ErrorType` from client-supplied input).

### NFR-3: Maintainability
Restores Single Responsibility: `BankStatementImportDto` is a pure data container; `BankMappingProfile` (an Application-layer mapping/orchestration concern) owns the domain-to-presentation translation rule, matching the project's own documented Contracts/DTO rules.

### NFR-4: Backwards compatibility — OpenAPI/TypeScript client contract change is expected and intentional
Unlike the two prior narrower Bank-module refactors (which explicitly required a byte-identical generated TypeScript client), this change **intentionally** changes the generated client surface: `errorType` moves from a read-only computed property to a full, settable property in the generated OpenAPI schema and TypeScript client. This is the entire point of the fix (see issue: "any client that tries to round-trip the DTO will silently lose the field"). A diff under `frontend/src/api-client/` (or wherever the generated client lives) that reflects `errorType` becoming settable is expected and must **not** be treated as a regression to revert. No other property of `BankStatementImportDto`, and no other DTO, should be affected.

## Data Model
No changes to any persisted/domain data model. `BankStatementImport` (domain entity), `ImportStatus` (domain constants class), and the `BankStatementImports` database table/schema are all unchanged. Only the Application-layer contract (`BankStatementImportDto`) and its mapping profile (`BankMappingProfile`) change.

## API / Interface Design
No endpoint, route, or HTTP contract shape changes (same fields on `BankStatementImportDto`: `Id`, `TransferId`, `StatementDate`, `ImportDate`, `Account`, `Currency`, `ItemCount`, `ImportResult`, `ErrorType`). The only interface-level change is that `ErrorType` gains a setter in the C# type and, consequently, in the generated OpenAPI schema and TypeScript client type (per NFR-4). No controller, MediatR request/response, or route changes are required — `GetBankStatementListHandler` and `GetBankStatementByIdHandler` both already populate `BankStatementImportDto` exclusively through `_mapper.Map(...)`, so no handler code changes are needed beyond what the mapping profile itself provides.

## Dependencies
- AutoMapper (already a dependency of `Anela.Heblo.Application`, already used by `BankMappingProfile`).
- `Anela.Heblo.Domain.Features.Bank.ImportStatus` — this dependency moves from the DTO file to the mapping profile file; `BankMappingProfile.cs` already has `using Anela.Heblo.Domain.Features.Bank;`, so no new `using` is required there.
- No other feature or module is affected. Confirmed via search: `ErrorType`/`errorType` only appears in `BankStatementImportDto.cs`, `BankMappingProfile.cs`, `BankMappingProfileTests.cs`, `GetBankStatementByIdHandlerTests.cs`, and the frontend `ImportTab.tsx` (which only reads `statement.errorType`, never sets it) plus the generated `api-client.ts`.

## Out of Scope
- Renaming, splitting, or otherwise changing `ImportStatus.Success` / `ImportStatus.ProcessingError` / `ImportStatus.UnknownError`.
- Any change to `BankStatementImport` (domain entity) or its persistence/repository/EF configuration.
- Any change to `BankStatementImportResultDto`, `GetBankStatementListResponse`, or any other Bank contract not named above.
- Any frontend behavior change beyond what the regenerated OpenAPI/TypeScript client produces automatically — `ImportTab.tsx`'s read-only usage of `statement.errorType` needs no source edit.
- Reintroducing or modifying the previously-removed `ForMember` history described in `docs/superpowers/plans/2026-06-03-remove-dead-bankmappingprofile-errortype-formember.md` beyond what FR-2 specifies.

## Open Questions

None.

## Status: COMPLETE
