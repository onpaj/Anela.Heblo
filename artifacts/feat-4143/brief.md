## Module
MarketingInvoices

## Finding
In `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` lines 96–111, the `SaveChangesAsync` catch block updates `result.Failed` and then unconditionally rethrows:

```csharp
catch (Exception ex)
{
    _logger.LogError(...);
    result.Failed += stagedCount;   // ← line 103 — dead write
    // result.Imported intentionally stays 0 — nothing was committed.
    throw;   // ← always executes; result is never returned to caller
}
```

Because `throw;` always executes, `result` is never returned in this code path — the caller receives an exception, not a return value. The assignment `result.Failed += stagedCount` has no observable effect and is dead code (YAGNI).

## Why it matters
The dead assignment misleads readers: the adjacent comment implies `result` will eventually be inspected, but it cannot be. Any future reader reasoning about the error accounting will be confused. There is also a subtle risk: if `result.Failed += stagedCount` ever overflows (contrived, but valid with large batches), it would shadow the original persist exception with an `OverflowException`.

## Suggested fix
Remove the dead assignment. The log statement already captures all the information needed:

```csharp
catch (Exception ex)
{
    _logger.LogError(
        ex,
        "Failed to persist {Count} marketing transactions for {Platform}",
        stagedCount, source.Platform);
    throw;
}
```

---
_Filed by daily arch-review routine on 2026-09-11._
