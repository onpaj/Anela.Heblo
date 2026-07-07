# Architecture Review: Remove dead `IncludeDetailedBreakdown` flag from GetMarginReport

## Skip Design: true

## Architectural Fit Assessment

This is a one-property deletion in an existing, well-formed vertical slice (`Analytics/UseCases/GetMarginReport`). It touches no module boundary, no persistence, no DI wiring, and no cross-module contract. Independent verification confirms the spec's core claim:

- `GetMarginReportHandler.Handle` (and its private helper `ProcessProductsForReport`) never reads `request.IncludeDetailedBreakdown` — `ProductSummaries` and `CategorySummaries` are unconditionally built and returned in `BuildSuccessResponse`.
- Repo-wide grep for `IncludeDetailedBreakdown`/`includeDetailedBreakdown` across `backend/` and `frontend/src` finds exactly three non-generated hits: the DTO property declaration itself, one test-object initializer (`GetMarginReportRequestValidatorTests.cs:220`), and the NSwag-generated `frontend/src/api/generated/api-client.ts`. No frontend hook, component, or `hooks.ts` wrapper references it — grep for `analytics_GetMarginReport(` outside the generated file returns nothing, confirming **zero live callers**, generated or hand-written.
- `GetMarginReportRequestValidator.cs` has no rule for this property (confirmed by reading the file) — removal requires no validator change, matching the spec.
- `AnalyticsController.GetMarginReport` binds the whole DTO via `[FromQuery] GetMarginReportRequest request` — no per-property mapping to edit.

This aligns with `development_guidelines.md`'s DTO rules (DTOs live in the module, classes not records — already the case here) and requires no ADR, no module-boundary change, and no new component. The spec's chosen approach (Option 1: delete) is architecturally correct — Option 2 (implement the flag) would add branching logic and tests for a documented-but-unused capability, which is scope the finding doesn't justify.

**One point the spec doesn't call out, worth flagging explicitly:** NSwag's Fetch template generates **positional** TypeScript parameters in DTO declaration order. Today `analytics_GetMarginReport(startDate, endDate, productFilter, categoryFilter, includeDetailedBreakdown, maxProducts)` — removing the property shifts `maxProducts` from the 6th positional argument to the 5th after regeneration. This is a **non-issue for this change** because grep confirms no caller invokes this method today (positionally or otherwise), so there is nothing to break. It's called out here only so the closing dev doesn't need to re-derive it: **any future consumer of this generated method must be added only after regenerating the client**, never against a stale copy, or it will silently bind arguments to the wrong parameters (no compile error, since both are `number | undefined`-shaped in adjacent slots... actually `boolean` vs `number`, so TypeScript would in fact catch a stale-signature mismatch at the call site — but only if the caller passes literal types; an untyped/`any` call site would not).

## Proposed Architecture

No new architecture — this is a subtractive change inside the existing `GetMarginReport` use case.

### Component Overview

Existing components, unchanged in shape:
- `GetMarginReportRequest.cs` (Application/Features/Analytics/UseCases/GetMarginReport) — loses one property.
- `GetMarginReportHandler.cs` — no change.
- `GetMarginReportRequestValidator.cs` — no change.
- `AnalyticsController.cs` — no change (model binding is DTO-driven).
- `frontend/src/api/generated/api-client.ts` — regenerated, not hand-edited.

### Key Design Decisions

#### Decision 1: Delete vs. implement the flag
**Options considered:**
1. Remove the property (spec's choice).
2. Implement conditional response-shaping based on the flag.

**Chosen approach:** Remove the property.

**Rationale:** Confirmed zero callers anywhere in the codebase (backend and frontend, generated and hand-written). Implementing Option 2 would add a branch, a "lightweight response" contract variant, and tests — all in service of a consumer that doesn't exist. That's speculative scope, which the finding and this review both reject. If a genuine need for a summary-only response later emerges, it should be scoped as its own feature with its own acceptance criteria (e.g. does "lightweight" mean omitting both lists, or just `ProductSummaries`?) — that ambiguity is exactly why building it now, un-requested, would be guesswork.

#### Decision 2: How to regenerate the OpenAPI/TypeScript client
**Options considered:**
1. Hand-edit `frontend/src/api/generated/api-client.ts` to drop the parameter.
2. Run the documented NSwag regeneration step and let it fall out automatically.

**Chosen approach:** Option 2 — run `dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual` (or `npm run generate-client` from `frontend/`) per `docs/development/api-client-generation.md`, and let the backend C# client (`Anela.Heblo.API.Client`) regenerate too if the build runs in Debug (PostBuild event).

**Rationale:** The generated file is derived output; hand-editing it drifts from the OpenAPI spec on the next build and risks missing template quirks (e.g. NSwag's parameter-ordering behavior noted above). This is the documented, enforced workflow — no reason to deviate for a one-property change.

## Implementation Guidance

### Directory / Module Structure

No structural changes. Files touched, all pre-existing:
```
backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportRequest.cs   (remove property, line 11)
backend/test/Anela.Heblo.Tests/Features/Analytics/Validators/GetMarginReportRequestValidatorTests.cs        (remove initializer, line 220)
frontend/src/api/generated/api-client.ts                                                                    (regenerate, don't hand-edit)
```

### Interfaces and Contracts

`GetMarginReportRequest` after change:
```csharp
public class GetMarginReportRequest : IRequest<GetMarginReportResponse>
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? ProductFilter { get; set; }
    public string? CategoryFilter { get; set; }
    public int MaxProducts { get; set; } = 50;
}
```
No interface signatures change (`IRequest<GetMarginReportResponse>` unaffected). `GetMarginReportResponse` is untouched.

### Data Flow

Unchanged: `AnalyticsController.GetMarginReport` → `IMediator.Send(request)` → `GetMarginReportHandler.Handle` → always builds `ProductSummaries` + `CategorySummaries` via `ProcessProductsForReport`/`ReportBuilderService`. Removing the dead property does not alter this path in any way — it only shrinks the public request contract to match what the handler actually consumes.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Stale generated TypeScript client left un-regenerated, causing frontend build to reference a client that still (or no longer) matches the backend contract | Low | Run the documented regeneration command (`npm run generate-client` or the msbuild target) as part of this change and confirm `includeDetailedBreakdown` is gone from `api-client.ts`; run `npm run build` to verify no TS errors. |
| Hidden caller missed by grep (e.g. dynamic property access, reflection, serialized config) | Very Low | Grep covered both `IncludeDetailedBreakdown` and `includeDetailedBreakdown` across all `.cs`/`.ts`/`.tsx` in the repo (not just Analytics) with zero hits outside the DTO, the one test, and the generated client — no dynamic/reflective usage pattern exists elsewhere in this codebase for query DTOs. Treat as closed. |
| Future dev re-adds a similar "looks configurable but isn't wired up" flag | Low | Not in scope for this change; general hygiene point only, no action here. |

## Specification Amendments

None. The spec (FR-1, FR-2, acceptance criteria) is accurate and sufficient as written. The only addition worth folding into the closing dev's checklist (not a spec defect, since it doesn't change any required action) is the regeneration verification step already listed under FR-1's acceptance criteria — confirm `includeDetailedBreakdown` is absent from both the generated TypeScript client and, if built in Debug, the backend `Anela.Heblo.API.Client` generated file.

## Prerequisites

None. This can proceed directly to implementation.
