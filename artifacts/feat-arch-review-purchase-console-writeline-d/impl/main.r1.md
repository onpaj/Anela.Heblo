All tasks complete. Writing the output artifact.

---

# Implementation: Remove debug Console.WriteLine from PurchaseOrder.AddLine()

## What was implemented

Deleted the `// Debug logging` comment and `Console.WriteLine(...)` call from `PurchaseOrder.AddLine()` — a pure 3-line removal that restores the Domain layer's Clean Architecture compliance (zero infrastructure I/O). No replacement logging introduced, no other lines touched.

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrder.cs` — removed `// Debug logging`, `Console.WriteLine(...)`, and the blank line following it from `AddLine()`

## Tests

No new tests written (deleting a side-effect-only statement has no assertable behavioral change). Existing fixture verified:

- `backend/test/Anela.Heblo.Tests/Domain/Purchase/PurchaseOrderTests.cs` — 33 tests passed before and after the change

## How to verify

```bash
# Confirm no Console.* in Domain layer
grep -rn "Console\." backend/src/Anela.Heblo.Domain/ --include="*.cs"
# → no output

# Run PurchaseOrder unit tests
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Domain.Purchase.PurchaseOrderTests" --nologo
# → 33 passed

# Verify build
dotnet build backend/Anela.Heblo.sln --nologo
# → 0 errors
```

## Notes

FR-2 scan confirmed: no other `Console.*` calls exist anywhere in `backend/src/Anela.Heblo.Domain/`. The Domain layer is fully clean.

Commit: `3393d038` — `chore(domain): remove debug Console.WriteLine from PurchaseOrder.AddLine`

## PR Summary

Removed a leftover `// Debug logging` + `Console.WriteLine` from `PurchaseOrder.AddLine()` that violated the project's Clean Architecture rule: domain entities must have zero infrastructure I/O. The statement was emitting unstructured noise to container stdout on every line addition during `CreatePurchaseOrder` / `UpdatePurchaseOrder` requests. No replacement logging is introduced — handlers already have `ILogger<T>` available if telemetry is ever genuinely required, but that is a separate concern.

FR-2 scan after the change: `grep -rn 'Console\.' backend/src/Anela.Heblo.Domain/` returns no matches — the Domain layer now has zero `Console.*` calls.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrder.cs` — deleted 3 lines: `// Debug logging` comment, `Console.WriteLine(...)` call, and the trailing blank line

## Status
DONE