Implementation complete. Here's a summary of what was done:

---

## Implementation: Refactor GetAvailableRuleTypes to MediatR Handler

**Two commits landed on `feat-arch-review-invoiceclassification-getava`:**

1. `afb3d6c` — `feat: add GetClassificationRuleTypes MediatR handler and tests`
   - Created 3 Application layer files (`Request`, `Response`, `Handler`) in the new `GetClassificationRuleTypes/` use case folder
   - Created `GetClassificationRuleTypesHandlerTests.cs` with 3 passing tests

2. `fec4445` — `refactor: remove IClassificationRule dependency from InvoiceClassificationController`
   - Removed `IEnumerable<IClassificationRule>` constructor param and field from controller
   - Removed `using Anela.Heblo.Domain.Features.InvoiceClassification;`
   - Updated `GetAvailableRuleTypes()` to async MediatR dispatch, returning `Ok(response.RuleTypes)` to preserve the bare-array JSON contract

**Results:** `dotnet build` ✅ zero errors — all 3985 tests pass — `dotnet format` no-diff.