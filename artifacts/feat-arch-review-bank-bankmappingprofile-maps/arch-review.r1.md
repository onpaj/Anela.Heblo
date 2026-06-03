# Architecture Review: Remove Dead AutoMapper Configuration for `BankStatementImportDto.ErrorType`

## Skip Design: true

## Architectural Fit Assessment

The change is a one-line removal inside the existing AutoMapper profile and aligns perfectly with the project's Vertical Slice / Clean Architecture conventions:

- **Module boundary respected.** `BankMappingProfile` lives in `Application/Features/Bank/`; the DTO lives in `Application/Features/Bank/Contracts/`; the domain entity lives in `Domain/Features/Bank/`. Nothing crosses these boundaries.
- **Single source of truth.** After removal, the `ImportResult → ErrorType` derivation lives only in `BankStatementImportDto.ErrorType` (the get-only computed property). This is consistent with the DTO-as-class rule in `docs/architecture/development_guidelines.md` (no `record`s for DTOs because OpenAPI client generators mishandle them) and with the project's KISS-first coding behavior.
- **No call-site impact.** The only mapping consumer is `GetBankStatementListHandler` at line 53 (`_mapper.Map<List<BankStatementImportDto>>(items)`). The handler is agnostic to `ForMember` declarations and continues to work because `ErrorType` is computed by the DTO getter at access time, not at map time.
- **AutoMapper's automatic enum→string conversion** for `Currency` (`CurrencyCode` enum → `string`) and matching-name property mapping for the other fields continue to work without any `ForMember` calls. The `CreateMap<BankStatementImport, BankStatementImportDto>()` declaration is still required and must remain.

The change has **zero architectural risk** beyond the mechanical edit.

## Proposed Architecture

### Component Overview

```
Domain.Features.Bank
   └── BankStatementImport           (entity, source of mapping)
            │
            │ mapped by AutoMapper
            ▼
Application.Features.Bank
   ├── BankMappingProfile             ← EDIT: drop ForMember(ErrorType)
   │      CreateMap<BankStatementImport, BankStatementImportDto>()
   │           (no .ForMember calls remain)
   │
   ├── Contracts/BankStatementImportDto
   │      ErrorType => ImportResult != "OK" ? ImportResult : null   (unchanged, sole source of truth)
   │
   └── UseCases/GetBankStatementList/GetBankStatementListHandler
          _mapper.Map<List<BankStatementImportDto>>(items)           (unchanged caller)

Tests
   └── backend/test/Anela.Heblo.Tests/Features/Bank/
          BankMappingProfileTests.cs   ← NEW: assert mapping + ErrorType derivation
```

### Key Design Decisions

#### Decision 1: Keep `CreateMap<BankStatementImport, BankStatementImportDto>()`, drop only `ForMember`
**Options considered:**
- A. Delete the whole `CreateMap` line.
- B. Delete only the `.ForMember(...)` chained call, leaving an empty `CreateMap`.

**Chosen approach:** Option B.

**Rationale:** `GetBankStatementListHandler` actively depends on the mapping (`_mapper.Map<List<BankStatementImportDto>>(items)`). Removing `CreateMap` would break runtime mapping. The `ForMember` is the only dead piece; the `CreateMap` itself is load-bearing.

#### Decision 2: Add a dedicated `BankMappingProfileTests` rather than extending handler tests
**Options considered:**
- A. Extend `ImportBankStatementHandlerTests` or add to `GetBankStatementListHandlerTests` (which doesn't exist).
- B. New file `BankMappingProfileTests.cs` that exercises a real `IMapper` built from `BankMappingProfile`.

**Chosen approach:** Option B.

**Rationale:** Existing handler tests use `Mock<IMapper>` (see `ImportBankStatementHandlerTests.cs:19,30`), so they don't exercise the profile and cannot satisfy FR-4. A profile-level test in the same `backend/test/Anela.Heblo.Tests/Features/Bank/` folder is the smallest, most direct verification. It also gives us `BankMappingProfile.AssertConfigurationIsValid()` coverage — a one-line guard against future profile-build errors.

#### Decision 3: Do not introduce `ImportStatus.Success` substitution in the DTO getter
**Options considered:**
- A. Replace the magic string `"OK"` in `BankStatementImportDto.ErrorType` with `ImportStatus.Success` (which already exists at `Domain/Features/Bank/ImportStatus.cs:5`).
- B. Leave the getter unchanged.

**Chosen approach:** Option B.

**Rationale:** The spec explicitly puts the rename out of scope (Out of Scope, bullet 1) to keep this change surgical. Also, the DTO lives in `Application`, and pulling a domain constant into the Application contract layer would couple the DTO to a domain type — a deliberate decision worth a separate ticket, not a drive-by edit.

## Implementation Guidance

### Directory / Module Structure

**Modify (1 file):**
- `backend/src/Anela.Heblo.Application/Features/Bank/BankMappingProfile.cs`
  - Remove line 12 (`.ForMember(dest => dest.ErrorType, ...)`) — chain ends after `CreateMap<...>()`.
  - Result:
    ```csharp
    public BankMappingProfile()
    {
        CreateMap<BankStatementImport, BankStatementImportDto>();
    }
    ```

**Create (1 file):**
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs`
  - xUnit + FluentAssertions style, matching the existing test conventions in `Features/Bank/`.
  - Build a real `IMapper`: `new MapperConfiguration(cfg => cfg.AddProfile<BankMappingProfile>()).CreateMapper()`.
  - One test calling `AssertConfigurationIsValid()`.
  - Theory or two Facts exercising `ImportResult = "OK"` → `ErrorType == null` and `ImportResult = "Failed"` → `ErrorType == "Failed"`.

**Do not touch:**
- `BankStatementImportDto.cs` (FR-3).
- Any other profile, handler, repository, or controller.
- `ImportStatus.cs`.

### Interfaces and Contracts

No interface or contract change. Confirmed surfaces that **must remain bit-identical**:

- `BankStatementImportDto` public shape: `Id, TransferId, StatementDate, ImportDate, Account, Currency (string), ItemCount, ImportResult, ErrorType (get-only)`.
- OpenAPI schema for `BankStatementImportDto` — generated TS/C# clients must produce no diff (NFR-4).
- AutoMapper-resolved member map for `BankStatementImport → BankStatementImportDto`: all name-matched properties + automatic `CurrencyCode → string` enum-to-string conversion + computed `ErrorType` (from DTO getter, not from mapper).

### Data Flow

1. `GetBankStatementListHandler.Handle` calls `_repository.GetFilteredAsync(...)` → returns `IReadOnlyList<BankStatementImport>` plus count.
2. `_mapper.Map<List<BankStatementImportDto>>(items)` projects each entity onto a `BankStatementImportDto` instance.
3. During projection, AutoMapper sets `Id, TransferId, StatementDate, ImportDate, Account, Currency (enum→string), ItemCount, ImportResult` by convention. It does **not** set `ErrorType` (read-only).
4. Any caller reading `dto.ErrorType` (JSON serialization for the API response, OpenAPI consumers) triggers the getter, which returns `ImportResult != "OK" ? ImportResult : null`.
5. Serialized JSON shape is identical to today — `ErrorType` is included as a string-or-null field per System.Text.Json defaults for computed read-only properties.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Future contributor adds an `ErrorType` setter and expects the mapper to populate it. | LOW | The DTO getter remains the documented derivation. Mapping profile test (FR-4) will fail loudly if behavior changes. |
| `CreateMap<...>()` accidentally removed along with `ForMember`, breaking `GetBankStatementListHandler`. | MEDIUM | New `AssertConfigurationIsValid()` test plus `GetBankStatementListHandler` smoke coverage will catch a missing map at build/test time. Reviewer should diff the file before merging. |
| OpenAPI generated clients shift subtly (e.g. `ErrorType` becomes non-nullable). | LOW | NFR-4 explicitly requires no diff. Verify by running `dotnet build` (which regenerates the OpenAPI clients per project convention) and inspecting `git diff` on `frontend/src/api-client/` paths; any change is a regression. |
| Removing the `ForMember` is interpreted as a behavior change in PR review. | LOW | Commit message must explicitly cite that AutoMapper ignores `ForMember` on get-only members and link to the brief; spec is referenced. |

## Specification Amendments

None required. The spec is internally consistent with the codebase reality after verification:

- `BankMappingProfile.cs` line 12 contains the dead `ForMember` exactly as described.
- `BankStatementImportDto.cs` line 13 holds the computed getter as described.
- The only consumer (`GetBankStatementListHandler`) uses the configured mapper and is unaffected by the change.
- `ImportStatus.Success = "OK"` already exists in `Domain/Features/Bank/ImportStatus.cs`, confirming the out-of-scope rename is a clean future task (not blocked by missing infrastructure).

One **clarifying note** for the implementer (not a spec change):
- FR-4 says "at least one unit test exercises `IMapper.Map<BankStatementImportDto>(source)`". Implement these against a real `IMapper` built from `BankMappingProfile` — **not** a `Mock<IMapper>`. A mocked mapper would not exercise the profile and would give false confidence.

## Prerequisites

None. No migrations, configuration, infrastructure, secret-store entries, feature flags, or Key Vault secrets are required. The change is in-process source-only and ships in a normal build:

- BE validation: `dotnet build` + `dotnet format` + `dotnet test` for the `Anela.Heblo.Tests` project (Bank slice).
- No FE work, no E2E run required (NFR-4 guarantees no client surface change).
- No DB migration.
- No Azure App Settings or Key Vault changes.