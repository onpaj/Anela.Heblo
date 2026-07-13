### task: dedupe-invoice-critical-error-predicate

## Goal

Consolidate the "critical error" business rule — currently duplicated as (1) the `IssuedInvoice.IsCriticalError` computed property and (2) a hand-written inline lambda inside `IssuedInvoiceRepository.GetSyncStatsAsync` — into a single shared definition on the `IssuedInvoice` entity, following the `TransportBox` `Expression<Func<T,bool>>` + compiled `Func<T,bool>` + delegating property pattern already established in this codebase. Add a regression test that fails if the two call sites ever diverge again. This is a pure internal refactor: no observable behavior, DTO, or API changes.

## Files to change

- `backend/src/Anela.Heblo.Domain/Features/Invoices/IssuedInvoice.cs`
- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs`
- `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceTests.cs` (new file)

## Approach

Mirror `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBox.cs:39-41` exactly — it is the precedent pattern for this exact problem (EF Core can't translate a computed C# property to SQL, so the predicate is defined once as an `Expression`, compiled once, and both the in-memory property and the SQL query share it):

```csharp
public static Expression<Func<TransportBox, bool>> IsInTransportPredicate = b => b.State == TransportBoxState.InTransit || b.State == TransportBoxState.Received || b.State == TransportBoxState.Opened;
public static Func<TransportBox, bool> IsInTransportFunc = IsInTransportPredicate.Compile();
public bool IsInTransit => IsInTransportFunc(this);
```

Note `TransportBox` declares these as plain `static` **fields** with `=` initializers (not `=>` expression-bodied properties, no `get`). Match that exact style — do not introduce a stylistic divergence within the same convention.

1. **`IssuedInvoice.cs`** (domain entity):
   - Add `using System.Linq.Expressions;` to the top of the file, alongside the existing `using System.Text.Json;` and `using Anela.Heblo.Xcc.Domain;`.
   - Replace the existing line:
     ```csharp
     public bool IsCriticalError => ErrorType != null && ErrorType != IssuedInvoiceErrorType.InvoicePaired;
     ```
     with three members, in the same location (immediately above where `IsCriticalError` currently sits, after the `SyncHistoryCount` property and before `SyncSucceeded`):
     ```csharp
     public static Expression<Func<IssuedInvoice, bool>> IsCriticalErrorPredicate =
         x => x.ErrorType != null && x.ErrorType != IssuedInvoiceErrorType.InvoicePaired;
     public static Func<IssuedInvoice, bool> IsCriticalErrorFunc = IsCriticalErrorPredicate.Compile();
     public bool IsCriticalError => IsCriticalErrorFunc(this);
     ```
   - Do not change anything else in the file (the `ErrorType` property's `private set` accessor, other members, etc. are untouched — the predicate lambda can read `x.ErrorType` since it's a member of the same class, same as `TransportBox`'s predicates read `b.State`).

2. **`IssuedInvoiceRepository.cs`** (persistence, `GetSyncStatsAsync`, currently at line 43):
   - Replace:
     ```csharp
     var criticalErrors = await query.CountAsync(x => x.ErrorType.HasValue && x.ErrorType != IssuedInvoiceErrorType.InvoicePaired, cancellationToken);
     ```
     with:
     ```csharp
     var criticalErrors = await query.CountAsync(IssuedInvoice.IsCriticalErrorPredicate, cancellationToken);
     ```
   - No other line in this method or file changes. No `using` changes needed — `IssuedInvoice` is already imported (`Anela.Heblo.Domain.Features.Invoices`), and `System.Linq.Expressions` is not referenced directly in this file (the expression type is inferred from `IssuedInvoice.IsCriticalErrorPredicate`'s declared type).

3. **New file `IssuedInvoiceTests.cs`** (entity-only unit tests, no DB fixture — sibling to `IssuedInvoiceRepositoryTests.cs` in `backend/test/Anela.Heblo.Tests/Features/Invoices/`):
   - This file doesn't exist yet; create it following the namespace/using conventions of `IssuedInvoiceRepositoryTests.cs` (`namespace Anela.Heblo.Tests.Features.Invoices;`, `using Xunit;`, `using Anela.Heblo.Domain.Features.Invoices;`).
   - Add a test that constructs an `IssuedInvoice` for every value of `IssuedInvoiceErrorType` (`General`, `InvoicePaired`, `ProductNotFound`) plus the `null` case, and asserts `invoice.IsCriticalError` (the entity property, which now goes through `IsCriticalErrorFunc`) agrees with `IssuedInvoice.IsCriticalErrorPredicate.Compile()(invoice)` evaluated independently — this proves the property and the predicate cannot silently diverge. Use `[Theory]`/`[InlineData]` (xUnit, matching the project's existing test style) with a nullable `IssuedInvoiceErrorType?` parameter, e.g.:
     ```csharp
     [Theory]
     [InlineData(null, false)]
     [InlineData(IssuedInvoiceErrorType.General, true)]
     [InlineData(IssuedInvoiceErrorType.InvoicePaired, false)]
     [InlineData(IssuedInvoiceErrorType.ProductNotFound, true)]
     public void IsCriticalError_AgreesWithSharedPredicate_ForAllErrorTypes(IssuedInvoiceErrorType? errorType, bool expectedCritical)
     ```
     Since `IssuedInvoice.ErrorType` has a `private set`, populate it via the existing public mutator (`SyncFailed(object, string, ...)` / `SyncFailed(object, IssuedInvoiceError, ...)` sets `ErrorType` from `IssuedInvoiceError.ErrorType`; a plain `SyncSucceeded(...)` call leaves `ErrorType` `null`). Do not add a test-only setter or reflection hack — use the entity's real public API, consistent with how `IssuedInvoiceRepositoryTests.cs` already builds invoices via `SyncSucceeded`/`SyncFailed`.
   - Assert both: (a) `invoice.IsCriticalError == expectedCritical`, and (b) `invoice.IsCriticalError == IssuedInvoice.IsCriticalErrorPredicate.Compile()(invoice)` — the second assertion is the actual regression guard against future divergence between the property and the predicate.

## Verification

- `dotnet build` from `backend/` (or the solution root) — must succeed with no new warnings.
- `dotnet format` — must produce no diff (run it before finalizing; if it reformats your edits, accept its output and re-verify build/tests).
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~Anela.Heblo.Tests.Features.Invoices` — runs both:
  - `IssuedInvoiceRepositoryTests` (must pass unmodified, including `GetSyncStatsAsync_WithVariousInvoices_ReturnsAccurateStats`'s `Assert.Equal(1, stats.CriticalErrors)` at `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs:156` — confirms the SQL-translated `CountAsync(IssuedInvoice.IsCriticalErrorPredicate, ...)` path still works against the EF Core InMemory provider).
  - The new `IssuedInvoiceTests` (must pass for all 4 cases: `null`→false, `General`→true, `InvoicePaired`→false, `ProductNotFound`→true).
- Manually confirm via `grep -rn "ErrorType.*InvoicePaired\|InvoicePaired.*ErrorType" backend/src` that no independently-written equivalent boolean condition remains outside `IssuedInvoice.IsCriticalErrorPredicate` (satisfies spec FR-1's acceptance criterion of exactly one such expression in the codebase).
