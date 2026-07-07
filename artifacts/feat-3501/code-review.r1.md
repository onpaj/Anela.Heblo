## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs:154-189` — `Lines_Exactly100Items_PassesValidation` and `Lines_101Items_FailsValidation` duplicate the same `Enumerable.Range(...).Select(i => new UpdatePurchaseOrderLineRequest {...})` block with only the count changed. A small private helper (e.g. `CreateLines(int count)`) would remove the duplication; purely stylistic, not required.
