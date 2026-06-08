# Specification: Extract Duplicated Period Totals Calculation in FinancialAnalysisService

## Summary
Three private methods in `FinancialAnalysisService` contain verbatim copy-pasted logic for computing monthly income and expenses from ledger items based on account-number prefixes. This spec defines a behavior-preserving refactor that extracts the shared logic into a single private static helper, eliminating the maintenance risk of keeping three sites in sync.

## Background
The `FinancialAnalysisService` (in `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs`) implements the accounting convention that ledger account numbers starting with `"5"` represent expense accounts and those starting with `"6"` represent income accounts. The signed totals per period are computed as:

- `expenses = sum(debit5) - sum(credit5)`
- `income  = sum(credit6) - sum(debit6)`

This exact block is repeated in three private methods:

1. `RefreshMonthlyDataAsync` (lines ~219–232)
2. `GetHybridWithCurrentMonthAsync` (lines ~302–310)
3. `GetFinancialOverviewRealTimeAsync` (lines ~505–521)

If the accounting convention changes (different prefixes, sign inversion, additional account ranges, or new filters), all three call sites must be updated in lockstep. The largest of the three methods is already ~147 lines, so the duplication adds noise to already long methods.

The daily architecture review routine flagged this on 2026-06-06 as a real maintenance risk worth fixing before the convention changes.

## Functional Requirements

### FR-1: Introduce a single shared calculation helper
Add a single private static method to `FinancialAnalysisService`:

```csharp
private static (decimal income, decimal expenses) CalculatePeriodTotals(
    IEnumerable<LedgerItem> debitItems,
    IEnumerable<LedgerItem> creditItems)
{
    var debit5 = debitItems.Where(i => i.DebitAccountNumber?.StartsWith("5") == true).Sum(i => i.Amount);
    var credit5 = creditItems.Where(i => i.CreditAccountNumber?.StartsWith("5") == true).Sum(i => i.Amount);
    var expenses = debit5 - credit5;

    var credit6 = creditItems.Where(i => i.CreditAccountNumber?.StartsWith("6") == true).Sum(i => i.Amount);
    var debit6 = debitItems.Where(i => i.DebitAccountNumber?.StartsWith("6") == true).Sum(i => i.Amount);
    var income = credit6 - debit6;

    return (income, expenses);
}
```

The method must:
- Be `private static` (no instance state required).
- Live inside `FinancialAnalysisService` in the same file.
- Accept `IEnumerable<LedgerItem>` for both debit and credit collections.
- Return a value tuple `(decimal income, decimal expenses)` so call sites can destructure naturally.
- Preserve the exact arithmetic, filter predicates, and null-handling (`?.StartsWith("...") == true`) of the existing duplicated blocks.

**Acceptance criteria:**
- A single `CalculatePeriodTotals` method exists in `FinancialAnalysisService`.
- The method is `private static` and not exposed outside the class.
- Filter predicates match the originals byte-for-byte (prefix strings `"5"` and `"6"`, null-conditional access, `== true` comparison).
- The returned tuple uses the exact names `income` and `expenses`.

### FR-2: Replace the three duplicated blocks with calls to the helper
The three identified blocks must be replaced with a single line:

```csharp
var (income, expenses) = CalculatePeriodTotals(debitItems, creditItems);
```

Locations:
1. `RefreshMonthlyDataAsync` — file `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs`, lines ~219–232
2. `GetHybridWithCurrentMonthAsync` — same file, lines ~302–310
3. `GetFinancialOverviewRealTimeAsync` — same file, lines ~505–521

**Acceptance criteria:**
- All three call sites use `CalculatePeriodTotals(debitItems, creditItems)` and destructure into `income` and `expenses`.
- The intermediate locals (`debit5`, `credit5`, `debit6`, `credit6`) no longer appear at the call sites.
- Variable names downstream of each block (anything that consumed `income` or `expenses`) remain unchanged so the rest of each method continues to compile and behave identically.
- The original filter literals (`"5"`, `"6"`) appear only inside `CalculatePeriodTotals` after the refactor.

### FR-3: Behavior preservation
This refactor must be purely structural. No observable behavior may change for any caller or test.

**Acceptance criteria:**
- For identical inputs, every public method of `FinancialAnalysisService` produces identical outputs before and after the refactor (same numeric results, same ordering, same DTO shapes).
- No new public, internal, or protected surface is added.
- No changes to method signatures, return types, dependency injection, or constructor parameters of `FinancialAnalysisService`.
- No changes to `LedgerItem` or any other type.
- No changes to logging, error handling, or exception types.

### FR-4: Test coverage for the extracted helper
Because the helper now centralizes business-critical accounting logic, it must be covered by unit tests. Since the helper is `private`, coverage is established by exercising it through the public methods of `FinancialAnalysisService` that call into it (preferred), or — if existing public-surface coverage is insufficient — by promoting the helper's visibility to `internal` and marking the test assembly with `InternalsVisibleTo`. Prefer public-surface tests; only fall back to `internal` if needed to assert edge cases that cannot be reached via public methods.

**Acceptance criteria:**
- At least one existing or new unit test exercises each of the three refactored methods (`RefreshMonthlyDataAsync`, `GetHybridWithCurrentMonthAsync`, `GetFinancialOverviewRealTimeAsync`) with a representative mix of ledger items including:
  - Debit-side `"5"` entries
  - Credit-side `"5"` entries (negative contribution to expenses)
  - Credit-side `"6"` entries
  - Debit-side `"6"` entries (negative contribution to income)
  - Entries with `null` account numbers (must be ignored, not throw)
  - Entries with account-number prefixes other than `"5"`/`"6"` (must be ignored)
- All tests pass before and after the refactor with identical assertions.

### FR-5: Validation gates
The change must pass the standard repository validation gates.

**Acceptance criteria:**
- `dotnet build` succeeds with no new warnings introduced by the change.
- `dotnet format` reports no formatting differences.
- The full backend test suite passes.
- No changes to the frontend, OpenAPI client, or generated TypeScript artifacts are produced.

## Non-Functional Requirements

### NFR-1: Performance
The refactor must not regress performance. The helper performs the same four LINQ aggregations as the original blocks. No additional materialization (e.g., calling `.ToList()`) is permitted. Callers continue to pass the same `IEnumerable<LedgerItem>` collections they already use; the helper enumerates each collection twice (once for the `"5"` filter, once for the `"6"` filter), matching today's behavior.

### NFR-2: Security
Not applicable. This is an internal refactor of an in-process calculation; no authentication, authorization, data exposure, or input boundaries are affected.

### NFR-3: Maintainability
The single change-point established here is the entire point of the refactor: any future change to the accounting convention (prefix changes, additional account ranges, sign inversion, currency handling) must be possible by editing only `CalculatePeriodTotals`.

### NFR-4: Readability
Each of the three refactored methods should be shorter and more focused after the change. Magic-number-like literals `"5"` and `"6"` are acceptable to leave inline inside `CalculatePeriodTotals` because they are the accounting convention; introducing named constants is **out of scope** for this task (see Out of Scope).

## Data Model
No data model changes. The refactor operates entirely on existing types:

- `LedgerItem` (consumed read-only) with the relevant properties:
  - `DebitAccountNumber : string?`
  - `CreditAccountNumber : string?`
  - `Amount : decimal`

No new entities, DTOs, database columns, or migrations.

## API / Interface Design

### Internal interface
A single new private static helper inside `FinancialAnalysisService`:

```csharp
private static (decimal income, decimal expenses) CalculatePeriodTotals(
    IEnumerable<LedgerItem> debitItems,
    IEnumerable<LedgerItem> creditItems);
```

### Public API
No changes. All public methods retain their existing signatures, return types, and behavior. No new MediatR requests, controllers, endpoints, or OpenAPI changes.

### Frontend
No changes. No generated TypeScript client regeneration is expected.

## Dependencies
None new. The change uses only types already imported by `FinancialAnalysisService.cs` (`System.Linq`, `LedgerItem`, value tuples).

## Out of Scope
- Renaming or restructuring `FinancialAnalysisService` beyond the three blocks identified.
- Splitting `GetFinancialOverviewRealTimeAsync` (147 lines) into smaller methods, even though the brief notes its size. That is a separate refactor.
- Introducing named constants for the `"5"` and `"6"` account-prefix literals.
- Moving `CalculatePeriodTotals` to a separate utility class or to the domain layer.
- Changing the return type of any public method, the shape of any DTO, or any persistence concern.
- Modifying `LedgerItem` or any other domain type.
- Adding new logging, telemetry, metrics, or feature flags.
- Frontend changes of any kind.
- Adding new accounting prefixes or rule variations.

## Open Questions
None.

## Status: COMPLETE