# Specification: Replace Magic String "OK" with `ImportStatus.Success` Constant in `BankStatementImportDto`

## Summary
The `ErrorType` computed property on `BankStatementImportDto` compares `ImportResult` to the literal string `"OK"` instead of referencing the existing `ImportStatus.Success` constant. This refactor replaces the magic string with the named constant so that any future change to `ImportStatus.Success` propagates correctly.

## Background
The Bank module maintains a single source of truth for import status values in `Anela.Heblo.Domain/Features/Bank/ImportStatus.cs`:

```csharp
public static class ImportStatus
{
    public const string Success = "OK";
    public const string ProcessingError = "PROCESSING_ERROR";
    public const string UnknownError = "UNKNOWN_ERROR";
}
```

All other consumers in the module (e.g. `BankStatementImportRepository.cs:49`, `ImportBankStatementHandler.cs:89`) use this constant. The outlier is `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs:13`:

```csharp
public string? ErrorType => ImportResult != "OK" ? ImportResult : null;
```

If `ImportStatus.Success` is ever renormalized (e.g. lowercased to `"ok"` or expanded to a richer value), every other site updates in lockstep while this DTO would silently misclassify successful imports as errors. The constant exists precisely to prevent this scattered-literal hazard. The Application layer already depends on the Domain layer, so the reference introduces no new dependency.

## Functional Requirements

### FR-1: Replace literal `"OK"` with `ImportStatus.Success`
The `ErrorType` computed property in `BankStatementImportDto` must reference the `ImportStatus.Success` constant from the Domain layer instead of the literal string `"OK"`.

**Acceptance criteria:**
- `BankStatementImportDto.cs` no longer contains the literal string `"OK"`.
- `BankStatementImportDto.cs` imports `Anela.Heblo.Domain.Features.Bank` and uses `ImportStatus.Success` in the `ErrorType` expression.
- For any `ImportResult` equal to `ImportStatus.Success`, `ErrorType` returns `null` (unchanged behavior).
- For any `ImportResult` not equal to `ImportStatus.Success`, `ErrorType` returns the `ImportResult` value (unchanged behavior).

### FR-2: Preserve observable behavior of `ErrorType`
The refactor is behavior-preserving. No serialization shape, property name, nullability, or value semantics may change.

**Acceptance criteria:**
- The public surface of `BankStatementImportDto` (property names, types, nullability) is unchanged.
- The DTO remains a `class` (not converted to a `record`), per the project-specific rule that DTOs are classes for OpenAPI client compatibility.
- Generated TypeScript client output for `BankStatementImportDto` is unchanged.

### FR-3: No unrelated changes
Per the "Surgical changes" rule in `CLAUDE.md`, only the single expression and its required `using` directive are modified. No reformatting, renaming, or adjacent cleanup elsewhere in the file or module.

**Acceptance criteria:**
- Diff is limited to `BankStatementImportDto.cs`.
- The only changes are: (a) addition of the `using Anela.Heblo.Domain.Features.Bank;` directive, and (b) the body of the `ErrorType` expression.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance impact. The change is a compile-time constant reference substitution; the emitted IL is equivalent to the prior string literal comparison.

### NFR-2: Security
No security impact. No new data flows, no external dependencies, no authentication or authorization touched.

### NFR-3: Build & Quality Gates
- `dotnet build` succeeds for the full solution.
- `dotnet format` reports no remaining issues for the edited file.
- All existing Bank-module tests pass without modification.
- The OpenAPI TypeScript client regeneration produces no diff for `BankStatementImportDto`.

### NFR-4: Backwards Compatibility
The constant value `ImportStatus.Success = "OK"` is unchanged in this work item. Any serialized payloads or persisted `ImportResult` values containing `"OK"` continue to be classified as success and yield `ErrorType == null`.

## Data Model
No data model changes.

Affected type:
- `BankStatementImportDto` (Application layer DTO) — only the body of the `ErrorType` computed property changes.

Referenced type (unchanged):
- `ImportStatus` (Domain layer static class) — provides `Success`, `ProcessingError`, `UnknownError` constants.

## API / Interface Design
No API surface change. The DTO continues to expose:

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
    public string? ErrorType { get; } // computed; behavior unchanged
}
```

After the change, line 13 becomes:

```csharp
public string? ErrorType => ImportResult != ImportStatus.Success ? ImportResult : null;
```

with a new `using Anela.Heblo.Domain.Features.Bank;` directive at the top of the file.

## Dependencies
- **Existing dependency:** `Anela.Heblo.Application` → `Anela.Heblo.Domain` (already in place; no new project reference required).
- **No new NuGet packages.**
- **No configuration, migration, infrastructure, or feature-flag changes.**

## Test Strategy
- Add or extend a unit test for `BankStatementImportDto` covering both branches of `ErrorType`:
  - When `ImportResult == ImportStatus.Success` → `ErrorType` is `null`.
  - When `ImportResult` is any non-success value (e.g. `ImportStatus.ProcessingError`, `ImportStatus.UnknownError`, or an arbitrary string) → `ErrorType` equals `ImportResult`.
- Tests must reference `ImportStatus.Success` (not the literal `"OK"`) so they remain correct if the constant value is renormalized in the future.
- Run `dotnet test` for the affected test project(s) and confirm no regressions in the Bank module.

## Out of Scope
- Changing the value of `ImportStatus.Success` itself (e.g. lowercasing, enum migration).
- Converting `ImportStatus` from a static class of string constants to an enum or a strong type.
- Auditing other modules for similar magic-string occurrences.
- Reformatting, renaming, or otherwise modifying `BankStatementImportDto` beyond the targeted line and its `using` directive.
- Changes to `BankStatementImportRepository`, `ImportBankStatementHandler`, or any other Bank module consumer.
- Generated client regeneration as a deliverable (it should be a no-op; verify only).

## Open Questions
None.

## Status: COMPLETE