# Architecture Review: PackingMaterials daily-consumption date validation

## Skip Design: true
Backend-only refactor, no UI.

## Architectural Fit Assessment
Fully aligned with existing conventions: `GetConsumptionHistory` already uses string dates, a `*RequestValidator`, explicit DI registration of validator + `ValidationBehavior` in `PackingMaterialsModule`, and handler-side `DateOnly.TryParseExact`. `ValidationExceptionHandler` (API layer) already maps `ValidationException` to 400 ProblemDetails. Verified in code.

## Proposed Architecture

### Component Overview
Controller (binds query to request) -> MediatR -> `ValidationBehavior<Req,Resp>` -> `GetDailyConsumptionBreakdownRequestValidator` -> `GetDailyConsumptionBreakdownHandler` (parses date) -> repository.

### Key Design Decisions

#### Decision 1: Bind the request object via [FromQuery]
**Options considered:** keep `string? date` + `groupBy` params and build the request; bind the request type directly.
**Chosen approach:** bind `[FromQuery] GetDailyConsumptionBreakdownRequest request`, like `GetConsumptionHistory`.
**Rationale:** property names `Date`/`GroupBy` bind case-insensitively to `date`/`groupBy`; OpenAPI query parameters remain identical so the generated client is unchanged. Default `GroupBy = Material` retained by the class initializer.

#### Decision 2: Date required in validator
**Rationale:** current behavior returns 400 on null/empty (TryParseExact fails). Unlike `DateFrom/DateTo` (optional), this date is mandatory, so use `NotEmpty()` plus format rule.

#### Decision 3: Handler defensive parse
Handler uses `TryParseExact` and returns `Success=false, Error=...` on failure, since existing contract already supports `Success/Error` and the controller maps it to 400. Keeps handler safe for callers bypassing the pipeline.

## Implementation Guidance

### Directory / Module Structure
- Modify `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetDailyConsumptionBreakdown/GetDailyConsumptionBreakdownRequest.cs`
- Add `.../GetDailyConsumptionBreakdownRequestValidator.cs` (same folder)
- Modify `.../GetDailyConsumptionBreakdownHandler.cs`
- Modify `backend/src/Anela.Heblo.Application/Features/PackingMaterials/PackingMaterialsModule.cs`
- Modify `backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs`
- Tests under `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/`: update `GetDailyConsumptionBreakdownHandlerTests.cs`; add `GetDailyConsumptionBreakdownRequestValidatorTests.cs`.

### Interfaces and Contracts
`public string? Date { get; set; }` on the request (class). Response unchanged (`DateOnly Date`). Format constant "yyyy-MM-dd", `CultureInfo.InvariantCulture`.

### Data Flow
Invalid date: behavior throws ValidationException -> 400 ProblemDetails. Valid: handler parses -> repository -> response.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| 400 body shape changes from `{error}` to ProblemDetails | Low | Frontend generated client throws on non-2xx either way; grep showed no hand-written consumer of this endpoint; document in PR |
| Handler tests use `DateOnly` literals | Low | Update to `TestDate.ToString("yyyy-MM-dd")` |
| Other callers of the request type (MCP tools) | Low | Grep for `GetDailyConsumptionBreakdownRequest` usage across `backend/` and fix at compile |
| Process docs CI gate | Low | Check `docs/processes/` for a PackingMaterials doc owning this code; bump `verified_at` / run `python3 scripts/process-docs/check.py index` if applicable |

## Specification Amendments
None.

## Prerequisites
None.
