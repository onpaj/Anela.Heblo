# Architecture Review: Replace Magic String `"OK"` with `ImportStatus.Success` in `BankStatementImportDto`

## Skip Design: true

Backend-only, single-expression refactor. No UI components, screens, or visual behavior are touched. The serialized DTO shape and generated TypeScript client are unchanged by design.

## Architectural Fit Assessment

The change is a strict alignment to an **already-established convention** in the Bank module. `Anela.Heblo.Domain/Features/Bank/ImportStatus.cs` is the canonical source of import-status string constants, and it is consumed correctly at every other site in the module:

- `BankStatementImportRepository.cs:49` — filters with `ImportStatus.Success`
- `ImportBankStatementHandler.cs:89, 104, 113` — writes and counts using the constants
- `BankStatementImportRepositoryTests.cs:393, 395, 415, 417` — test setup uses the constants

The Application layer already has a `ProjectReference` to `Anela.Heblo.Domain` (`Anela.Heblo.Application.csproj:40`), so the proposed `using` introduces **no new project, package, or module dependency** and respects Clean Architecture layering (Application → Domain). `BankStatementImportDto` is the lone outlier; the refactor closes a known scattered-literal hazard and restores symmetry with the rest of the module.

Integration points: zero. The change is a compile-time constant substitution; the emitted IL is identical.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Domain
└── Features/Bank
    └── ImportStatus.cs                  (canonical constants — unchanged)
        ├── Success           = "OK"
        ├── ProcessingError   = "PROCESSING_ERROR"
        └── UnknownError      = "UNKNOWN_ERROR"
                ▲
                │ (existing project reference, no new dep)
                │
Anela.Heblo.Application
└── Features/Bank
    └── Contracts/
        └── BankStatementImportDto.cs    (← single edit site)
            └── ErrorType =>
                ImportResult != ImportStatus.Success ? ImportResult : null;
```

No new components, no new files in `src/`. One test file may be touched (see Specification Amendments).

### Key Design Decisions

#### Decision 1: Reference the Domain constant directly from the Application DTO

**Options considered:**
1. Reference `ImportStatus.Success` directly from the DTO (proposed in spec).
2. Mirror the constant inside the Application/Contracts layer to "decouple" the DTO from Domain.
3. Promote `ImportStatus` to a strong type (enum or smart enum) and migrate all consumers.

**Chosen approach:** Option 1 — direct reference.

**Rationale:**
- Option 2 reintroduces the exact problem we are fixing: two copies of the string `"OK"` that can drift. The Application layer already depends on Domain; no architectural rule justifies a second copy.
- Option 3 is explicitly out of scope per `spec.r1.md` and would force changes across the entire Bank module, the persistence projection (`BankStatementImportRepository`), and the generated TS client. That is a separate, larger refactor.
- Option 1 matches what every other Bank-module call site already does, making the DTO consistent with `BankStatementImportRepository.cs:49` and `ImportBankStatementHandler.cs:89`.

#### Decision 2: Keep the DTO a `class`, keep `ErrorType` as an expression-bodied computed property

**Options considered:**
1. Leave the DTO shape and member style exactly as-is.
2. Convert to `record` or promote `ErrorType` to a stored property mapped by AutoMapper.

**Chosen approach:** Option 1.

**Rationale:**
- The repo rule "DTOs are classes, never C# records" (CLAUDE.md, `docs/architecture/development_guidelines.md`) forbids `record` for DTOs because the OpenAPI generator mishandles record parameter order. The TS client must stay byte-identical.
- `ErrorType` is a derived view of `ImportResult`; making it stored would create a second field that can desynchronize from `ImportResult` and would change the AutoMapper profile (currently asserted valid by `BankMappingProfileTests.Profile_Configuration_IsValid`).
- The surgical-changes rule (CLAUDE.md) explicitly forbids adjacent cleanup.

## Implementation Guidance

### Directory / Module Structure

No new files in `src/`. Single edit:

- `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs`
  - Add `using Anela.Heblo.Domain.Features.Bank;` at the top.
  - Change line 13 to `public string? ErrorType => ImportResult != ImportStatus.Success ? ImportResult : null;`.

Test edit (see Specification Amendments below):

- `backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs`
  - Replace literal `"OK"` on lines 37 and 42 with `ImportStatus.Success` so the test does not silently break if the constant is renormalized in the future.

### Interfaces and Contracts

Public surface is unchanged:

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
    public string? ErrorType { get; }   // computed; behavior unchanged
}
```

The OpenAPI TS client must regenerate to a no-op diff. This is a verification gate, not a deliverable.

### Data Flow

Unchanged. For the read path (e.g. `GET /api/bank-statements/{id}` via `BankStatementsController.cs:142`):

```
Persistence (BankStatementImport entity, ImportResult: "OK" | "PROCESSING_ERROR: ..." | …)
   │
   ▼  AutoMapper (BankMappingProfile)
   │
BankStatementImportDto
   │   ImportResult: same string
   │   ErrorType:    derived at access time
   │                 == null              when ImportResult == ImportStatus.Success
   │                 == ImportResult      otherwise
   ▼
JSON serialization → TS client (unchanged shape)
```

The comparison previously written as `ImportResult != "OK"` is now written as `ImportResult != ImportStatus.Success`. Because `ImportStatus.Success` is a `const string` of value `"OK"`, the compiler inlines the comparison and the emitted IL is equivalent.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Generated TS client picks up a diff (e.g. import re-ordering, schema noise) | Low | Run the OpenAPI client regeneration locally and confirm zero diff under `frontend/src/api/` for `BankStatementImportDto`. NFR-3 in the spec already mandates this verification. |
| `dotnet format` reorders the new `using` directive in a way that introduces churn elsewhere | Low | Place the new `using` in alphabetical order relative to existing usings before running `dotnet format`. Limit format scope to the edited file. |
| Existing test `BankMappingProfileTests` keeps the literal `"OK"` and silently misclassifies if `ImportStatus.Success` is renormalized later | Medium | Update lines 37 and 42 to reference `ImportStatus.Success`. See Specification Amendments. |
| Reviewer perceives the change as too small to test | Low | The spec already requires both-branch coverage. The existing `BankMappingProfileTests` covers both branches via AutoMapper; reuse it rather than introducing a parallel direct-instantiation unit test. |
| Persistence projection in `BankStatementImportRepository.cs:49` is an EF `IQueryable` filter — it must not change | Low | The DTO edit is in `Application/Contracts`. The repository file is not touched. NFR/surgical rule already enforces this. |

## Specification Amendments

1. **Extend test scope to cover the existing mapping test file.**
   The spec's FR-3 and NFR-3 (lines: "all existing Bank-module tests pass without modification") understate the work. `backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs` already exercises both branches of `ErrorType` (`Map_BankStatementImport_To_Dto_When_ImportResult_Is_OK_…` and `…_Is_Not_OK_…`), but it uses the literal `"OK"` on lines 37 and 42. The spec's own test-strategy bullet states: *"Tests must reference `ImportStatus.Success` (not the literal `\"OK\"`) so they remain correct if the constant value is renormalized in the future."* These two test lines must be updated. The diff therefore covers two files, not one:
   - `BankStatementImportDto.cs` (production change)
   - `BankMappingProfileTests.cs` (lines 37 and 42: replace `"OK"` with `ImportStatus.Success`; the file already imports `Anela.Heblo.Domain.Features.Bank` on line 3)

   This does not violate the surgical-changes rule because the change is directly motivated by the same refactor goal. Amend FR-3 to read "*Diff is limited to `BankStatementImportDto.cs` and the test assertions in `BankMappingProfileTests.cs` that previously used the literal `\"OK\"`.*"

2. **No new test file is required.**
   The spec's Test Strategy reads as if a fresh unit test for `BankStatementImportDto` is to be added. The existing `BankMappingProfileTests` already covers both branches via the mapping pipeline (which is the actual production code path: Entity → DTO via AutoMapper, then property access). Adding a second direct-instantiation test would duplicate coverage. Recommend amending Test Strategy to: "*Reuse existing `BankMappingProfileTests` cases; update them to reference `ImportStatus.Success`. Do not add a new test file.*"

3. **NFR-3 clarification: `dotnet format` scope.**
   Run `dotnet format` scoped to the two edited files (e.g. `dotnet format --include backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs`) to avoid incidental reformatting elsewhere.

## Prerequisites

None. All required infrastructure exists:

- `Anela.Heblo.Domain.Features.Bank.ImportStatus` is present and stable.
- `Anela.Heblo.Application` already references `Anela.Heblo.Domain`.
- The `BankMappingProfileTests` test class and AutoMapper profile are wired up.
- No migration, configuration, feature-flag, secret, or infrastructure change is required.

Implementation can start immediately.