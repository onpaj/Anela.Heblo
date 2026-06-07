# Architecture Review: Remove Unused `BankStatementImportQueryDto` Dead Code

## Skip Design: true

No UI, no API surface, no visual components — pure backend dead-code deletion.

## Architectural Fit Assessment

The proposed change aligns cleanly with the project's Clean Architecture + Vertical Slice conventions documented in `docs/architecture/filesystem.md`. The `Contracts/` folder is reserved for DTOs shared across multiple use cases within a feature module; an orphan DTO with no consumers violates the spirit of that convention.

Integration points verified:

- **No code references.** Repository-wide grep for `BankStatementImportQueryDto` returns only the declaring file (`backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportQueryDto.cs`) — no handlers, controllers, mappers, validators, or tests touch it.
- **`BankMappingProfile`** (`backend/src/Anela.Heblo.Application/Features/Bank/BankMappingProfile.cs`) only maps `BankStatementImport → BankStatementImportDto`. No mapping involves the dead type.
- **`BankModule.cs`** wires only `GetBankStatementListRequest` and its validator. No DI registration depends on the dead type.
- **Active query contract** `GetBankStatementListRequest` (in `UseCases/GetBankStatementList/`) is the authoritative request and exposes the richer filter surface (`TransferId`, `Account`, `DateFrom`, `DateTo`, `ErrorsOnly`) the dead DTO lacks.

One discrepancy with the spec: it claims "no docs reference `BankStatementImportQueryDto`," but `docs/features/comgate.md:176` does name the type as the documented query-parameter shape for `GET /api/bank-statements`. The doc is already inaccurate (it lists only 3 of the 11 actual filter properties) — see Specification Amendments.

## Proposed Architecture

### Component Overview

```
Features/Bank/
├── Contracts/
│   ├── BankAccountDto.cs
│   ├── BankImportRequestDto.cs
│   ├── BankStatementImportDto.cs
│   ├── BankStatementImportResultDto.cs
│   └── BankStatementImportQueryDto.cs   ← DELETE
├── UseCases/
│   └── GetBankStatementList/
│       ├── GetBankStatementListRequest.cs   ← authoritative contract (unchanged)
│       ├── GetBankStatementListHandler.cs   ← unchanged
│       └── GetBankStatementListResponse.cs  ← unchanged
├── Validators/
│   └── GetBankStatementListRequestValidator.cs   ← unchanged
├── BankMappingProfile.cs   ← unchanged
└── BankModule.cs           ← unchanged
```

After deletion, the `Contracts/` folder retains only DTOs that are actually consumed across multiple use cases or returned from the API.

### Key Design Decisions

#### Decision 1: Delete wholesale rather than merge fields into `GetBankStatementListRequest`

**Options considered:**
- **A.** Delete the file outright.
- **B.** Merge any missing fields from `BankStatementImportQueryDto` into `GetBankStatementListRequest`.
- **C.** Keep the file and add `[Obsolete]`.

**Chosen approach:** A — delete the file outright.

**Rationale:** `GetBankStatementListRequest` is a strict superset of the dead DTO's fields (`Id`, `StatementDate`, `ImportDate`, `Skip`, `Take`, `OrderBy`, `Ascending` are all present, plus five more). There is nothing to merge. `[Obsolete]` would only prolong the confusion the spec calls out. Wholesale deletion is the minimal, intent-preserving change.

#### Decision 2: Use `git rm` (not just filesystem delete)

**Options considered:** filesystem delete + `git add -A` vs. explicit `git rm`.

**Chosen approach:** `git rm backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportQueryDto.cs`.

**Rationale:** Explicit `git rm` makes intent clear in the diff and avoids accidentally staging unrelated changes via `-A`. Matches the global git-workflow guidance to stage specific files.

#### Decision 3: Update stale doc reference in the same commit

**Options considered:** include doc fix here vs. defer to a separate ticket.

**Chosen approach:** include in this change.

**Rationale:** The spec's `Out of Scope` clause "Updating documentation — no docs reference `BankStatementImportQueryDto`" is factually incorrect — `docs/features/comgate.md:176` does reference it. Leaving the doc referencing a deleted type creates a worse inconsistency than the original problem the spec set out to solve. Fixing one line in the doc costs nothing and keeps the change atomic. See Specification Amendments.

## Implementation Guidance

### Directory / Module Structure

No new code. Only the following changes:

- **Delete:** `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportQueryDto.cs`
- **Edit:** `docs/features/comgate.md` — update line 176 (and the adjacent bullet list) to name `GetBankStatementListRequest` and reflect the actual filter set.

### Interfaces and Contracts

The authoritative query contract is and remains:

`backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListRequest.cs` — a `class : IRequest<GetBankStatementListResponse>` with the eleven filter/paging properties. Per project rules, this stays a `class` (not a `record`) because it is on the OpenAPI client generation surface via the MediatR controller binding.

### Data Flow

Unchanged. The bank statement list flow remains:
`Controller → MediatR.Send(GetBankStatementListRequest) → GetBankStatementListHandler → Response`. The deleted DTO never participated in this flow.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| OpenAPI/TypeScript client regenerates with a diff (unexpected schema removal). | Low | Verify the generated TS client is byte-identical post-build; the dead DTO was never wired into a controller, so it should not appear in `swagger.json`. If a diff appears, investigate before merging. |
| A hidden runtime reference (reflection, JSON config) escaped the static grep. | Low | Run `dotnet build` and the full backend test suite. Reflection-based usage is implausible for an Application-layer DTO with no registration, but the build is the cheap safety net. |
| Stale doc reference in `docs/features/comgate.md` left behind, creating new confusion. | Medium | Update the doc in the same commit (see Decision 3 and Specification Amendments). |
| Reviewer or future contributor reverts the deletion by reading the unupdated comgate.md doc and "restoring" the missing type. | Medium | Same mitigation — fixing the doc in the same commit removes the lure. |

## Specification Amendments

1. **Correct the "no docs reference this type" claim.** The spec's `Out of Scope` section asserts "Updating documentation — no docs reference `BankStatementImportQueryDto`." This is incorrect. `docs/features/comgate.md:176` (section *"Query API: GET /api/bank-statements"*) names the type and describes three of its properties as the query-parameter shape.

2. **Add a new functional requirement FR-4: Update stale doc reference.**
   - **Acceptance criteria:**
     - `docs/features/comgate.md` no longer mentions `BankStatementImportQueryDto`.
     - The "Query parametry" subsection at line 176 is updated to reference `GetBankStatementListRequest` and lists its actual filter properties (`Id`, `TransferId`, `Account`, `StatementDate`, `ImportDate`, `DateFrom`, `DateTo`, `ErrorsOnly`) along with paging/sort (`Skip`, `Take`, `OrderBy`, `Ascending`).
     - Repository-wide grep for `BankStatementImportQueryDto` returns zero hits.

3. **Update FR-3 verification step.** Add: a final `grep -r "BankStatementImportQueryDto" .` must return zero hits across both code and docs.

## Prerequisites

None. No migrations, no config, no infrastructure changes. The change is a single-file deletion plus a small doc edit within the existing branch.