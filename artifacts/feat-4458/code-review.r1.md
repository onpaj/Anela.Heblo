## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculateBatchPlan/CalculateBatchPlanHandler.cs:60` — `CreateEffectiveRequest` copies each property by hand; a future property added to `CalculateBatchPlanRequest` must be added here too. The reflection-based test `Handle_ProductRequest_ForwardedRequestCopiesAllOtherProperties` guards this, so no change needed.

Verified: the handler no longer assigns to the incoming request; all 11 request properties are forwarded; non-Product path forwards the original request unchanged (FR-2); `ResolveSalesRanges` still uses the original request (FR-3); the template lookup still uses the original product code.
