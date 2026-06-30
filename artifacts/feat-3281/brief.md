## Module / File
backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/ResolveManualAction/ResolveManualActionHandler.cs

## Coverage
Line coverage: 18% (filter threshold: 60%)

## What's not tested
The handler resolves a manual-action flag on a manufacture order with four independent conditional paths, none of which have assertions:
1. **Order not found** → returns `ResourceNotFound` error; the happy path is the only partially-covered path
2. **ErpOrderNumberSemiproduct provided** → overwrites the field; if omitted the field is untouched
3. **ErpOrderNumberProduct provided** → same pattern
4. **ErpDiscardResidueDocumentNumber provided** → overwrites plus sets `ErpDiscardResidueDocumentNumberDate = DateTime.UtcNow`; the timestamp is only set in this branch
5. **Note provided** → creates a `ManufactureOrderNote` with user identity from `ICurrentUserService`; if the current user is null, the name falls back to "Unknown User"
6. **ManualActionRequired is always reset to false** — no test asserts this happens

## Why it matters
This handler is the mechanism by which operators close out pending manual reviews in the manufacture workflow. If a field update branch is skipped or the timestamp is not set correctly, ERP reconciliation data is silently wrong and no alarm fires.

## Suggested approach
Unit tests with a mocked `IManufactureOrderRepository` and `ICurrentUserService`:
- Order not found → verify `ResourceNotFound` result, no save
- Happy path with all optional fields → verify all three ERP fields updated and `ManualActionRequired == false`
- Partial fields (only ErpOrderNumberSemiproduct) → verify others unchanged
- Note provided → verify note text, timestamp, and user name on the appended note
- Note omitted → verify Notes collection unchanged
Estimated effort: ~2 hours.

---
_Filed by weekly coverage-gap routine on 2026-06-22. Based on CI run #27941952679 (9463aa5983b2a6d201782725aeeaaba777d8c07d)._
