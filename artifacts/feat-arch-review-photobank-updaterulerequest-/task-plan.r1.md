Plan saved. Summary of what's in it:

**Path:** `docs/superpowers/plans/2026-06-16-fix-photobank-updaterule-body-contract.md`

**Structure:** 5 tasks decomposed into 2–5-minute steps:
1. Add `UpdateRuleBody.cs` (verbatim contents, block-namespace, `= null!;` style matching neighbours).
2. Swap `PhotobankController.UpdateRule` parameter type and body→command field source (lines 299–313 only). Explicit warning to keep `HandleResponse(response)` — not the spec sample's `Ok(...)`.
3. Backend build + `dotnet format` + Photobank tests gate.
4. Frontend regeneration + `git diff` schema verification + lint + build.
5. Final cross-cutting check: only the expected files touched, `UpdateRuleRequest.Id` still present, `AddRule` (sibling anti-pattern) untouched.

**Testing strategy:** No new tests — change is type-level only, FR-5 trivially satisfied (no existing UpdateRule tests), and a reflection-style "body has these fields" test would add no value the compiler doesn't already provide. The verifiable check is the regenerated `api-client.ts` schema in Task 4.

**Spec coverage verified:** FR-1 → Task 1, FR-2 → Task 2, FR-3 → Task 4, FR-4 → addressed via no-op verification, FR-5 → Testing Strategy + Task 5. NFRs (build gates, module conventions, no auth changes) covered. All four arch-review amendments respected (PascalCase `Contracts/`, keep `HandleResponse`, no existing tests, FR-4 no-op).