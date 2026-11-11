## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `frontend/src/api/generated/api-client.ts` — generated `orderDate` is now optional (`orderDate?: Date`) because the C# property is a non-nullable value type without `[Required]`; a missing field is still rejected by the validator (`NotEqual(default)`), so behaviour is correct, but the TS type no longer signals the field is required.

Verified: no remaining callers of the removed string `ToUtcDateTime` helpers; backend test project builds with 0 errors; CreatePurchaseOrder and PurchaseOrdersController tests pass (90/90).
