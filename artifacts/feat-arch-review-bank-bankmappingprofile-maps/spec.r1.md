# Specification: Remove Dead AutoMapper Configuration for `BankStatementImportDto.ErrorType`

## Summary
Remove a no-op `ForMember` AutoMapper configuration in `BankMappingProfile` that targets the get-only computed property `BankStatementImportDto.ErrorType`. The DTO's computed getter already provides the correct derivation from `ImportResult`, making the mapper rule duplicated dead code that AutoMapper silently ignores.

## Background
During the daily architecture review on 2026-05-29, a dead-code finding was filed against the Bank module. `BankStatementImportDto.ErrorType` is defined as a get-only computed property:

```csharp
// backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs, line 13
public string? ErrorType => ImportResult != "OK" ? ImportResult : null;
```

`BankMappingProfile` then configures the same derivation via AutoMapper:

```csharp
// backend/src/Anela.Heblo.Application/Features/Bank/BankMappingProfile.cs, line 12
CreateMap<BankStatementImport, BankStatementImportDto>()
    .ForMember(dest => dest.ErrorType, opt => opt.MapFrom(src => src.ImportResult != "OK" ? src.ImportResult : null));
```

AutoMapper silently ignores `ForMember` calls targeting read-only destination properties. The configuration line is therefore:
- **Misleading** — readers assume it has effect; a future setter would silently activate it with potentially unexpected behaviour.
- **Duplicated business logic** — the `"not OK → return result"` rule lives in two places with no single source of truth.
- **A KISS violation** — one derivation point is enough.

The DTO's computed getter is the correct, sufficient implementation. The mapping line should be removed so the DTO is the single source of truth.

## Functional Requirements

### FR-1: Remove dead `ForMember` configuration
Remove the `.ForMember(dest => dest.ErrorType, ...)` call from `BankMappingProfile.cs` for the `CreateMap<BankStatementImport, BankStatementImportDto>()` mapping. The remaining mapping configuration (if any) must be preserved unchanged.

**Acceptance criteria:**
- `BankMappingProfile.cs` no longer contains a `ForMember` call targeting `ErrorType`.
- The `CreateMap<BankStatementImport, BankStatementImportDto>()` declaration remains in place.
- No other AutoMapper profile configurations are modified.

### FR-2: Preserve `ErrorType` runtime behaviour
The runtime value of `BankStatementImportDto.ErrorType` after mapping must remain identical to the current behaviour for all input cases: when `ImportResult == "OK"`, `ErrorType` returns `null`; otherwise `ErrorType` returns the value of `ImportResult`.

**Acceptance criteria:**
- For a `BankStatementImport` with `ImportResult = "OK"`, the mapped DTO's `ErrorType` is `null`.
- For a `BankStatementImport` with `ImportResult = "Failed"` (or any non-`"OK"` value), the mapped DTO's `ErrorType` equals that `ImportResult` value.
- For a `BankStatementImport` with `ImportResult = null`, the DTO's `ErrorType` behaves as the getter dictates (`null != "OK"` → returns `null`); behaviour matches pre-change.

### FR-3: No DTO contract change
`BankStatementImportDto.ErrorType` remains a get-only computed property derived from `ImportResult`. The DTO public contract (property names, types, nullability, getter logic) is unchanged. No setter is added.

**Acceptance criteria:**
- `BankStatementImportDto.cs` is not modified by this change.
- OpenAPI client generation output is unchanged (no diff in generated TypeScript or C# clients).

### FR-4: Verification via tests
Tests must demonstrate that mapping behaviour is preserved after removal of the `ForMember` line.

**Acceptance criteria:**
- At least one unit test exercises `IMapper.Map<BankStatementImportDto>(source)` with `ImportResult = "OK"` and asserts `ErrorType == null`.
- At least one unit test exercises mapping with a non-`"OK"` `ImportResult` value and asserts `ErrorType` equals that value.
- If equivalent tests already exist, they must continue to pass without modification.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance impact. The change removes a noop configuration; mapping throughput is expected to be unchanged or marginally improved (one fewer property rule evaluated during profile build).

### NFR-2: Security
No security impact. No authentication, authorization, input validation, or data exposure surface is affected. `ErrorType` continues to be derived from existing internal data.

### NFR-3: Maintainability
After the change, the business rule for deriving `ErrorType` lives in exactly one location (the DTO getter). Future readers will not be misled by configuration that has no runtime effect.

### NFR-4: Backwards compatibility
The DTO's serialized shape (JSON over HTTP, OpenAPI schema, generated clients) is identical before and after the change. No consumer — frontend, MCP, or external — needs to be updated.

## Data Model
No data model changes.

Affected types (read-only context, not modified except `BankMappingProfile`):

- `BankStatementImport` (domain entity) — source of mapping; exposes `ImportResult : string`.
- `BankStatementImportDto` (DTO class) — destination of mapping; exposes `ImportResult : string` and computed `ErrorType : string?`.
- `BankMappingProfile : AutoMapper.Profile` — the only file modified.

## API / Interface Design
No API surface changes. No endpoints, MediatR requests, MVC controllers, MCP tools, events, or UI flows are added, removed, or altered. The OpenAPI schema for `BankStatementImportDto` is unchanged.

## Dependencies
- AutoMapper (existing dependency; version unchanged).
- xUnit / existing BE test project (for FR-4 unit test coverage). No new packages.

## Out of Scope
- Renaming `ImportStatus.Success` (`"OK"`) for readability. The brief flags this as optional; deferred to a separate task to keep this change surgical.
- Introducing an `ImportStatus` enum or replacing the magic string `"OK"` with a constant.
- Refactoring other AutoMapper profiles in the codebase, even if similar dead `ForMember` patterns exist elsewhere.
- Changing `BankStatementImportDto` to a class with a setter, or otherwise modifying its public contract.
- Any frontend changes.

## Open Questions
None.

## Status: COMPLETE