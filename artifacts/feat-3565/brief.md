# [arch-review] Invoices: IsCriticalError business rule duplicated between domain entity and persistence query

## Module
Invoices

## Finding
The definition of "critical error" (any error type except `InvoicePaired`) appears in two places:

**Domain entity** — `backend/src/Anela.Heblo.Domain/Features/Invoices/IssuedInvoice.cs:53`:
```csharp
public bool IsCriticalError => ErrorType != null && ErrorType != IssuedInvoiceErrorType.InvoicePaired;
```

**Persistence query** — `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs:43`:
```csharp
var criticalErrors = await query.CountAsync(
    x => x.ErrorType.HasValue && x.ErrorType != IssuedInvoiceErrorType.InvoicePaired,
    cancellationToken);
```

The duplication exists because EF Core cannot translate computed properties into SQL, so the repository re-implements the same predicate as a raw expression.

## Why it matters
If the business rule changes (e.g. a new `IssuedInvoiceErrorType` is added that should also be non-critical), one of the two definitions will silently diverge. The domain entity and the stats query will then disagree on what "critical" means — the detail card shows a different error badge than the stats dashboard reports. The bug won't surface as a compilation error or test failure.

## Suggested fix
Extract the predicate as a static expression on the domain interface or on the repository, so it is defined once:

```csharp
// In IIssuedInvoiceRepository or a domain-layer static helper:
public static Expression<Func<IssuedInvoice, bool>> IsCriticalErrorExpression =>
    x => x.ErrorType.HasValue && x.ErrorType != IssuedInvoiceErrorType.InvoicePaired;
```

Use it in the repository via `.Where(IsCriticalErrorExpression)` and expose the compiled delegate as `IsCriticalError` on the entity (or keep the entity property and reference the same constant). Either way, the rule lives in one place.

---
_Filed by daily arch-review routine on 2026-07-08._
