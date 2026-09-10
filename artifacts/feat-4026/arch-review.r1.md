# Architecture Review: Typed `GroupBy` for PackingMaterials Daily Consumption Breakdown

## Skip Design: true
Backend-only refactor: a request DTO's property type change, a handler's internal dispatch
logic, and a controller query-parameter type. No new or changed UI components, screens,
layouts, or visual design decisions. The frontend generated client regenerates
automatically from the OpenAPI spec on the next build; no call site was found in
`frontend/src` for this endpoint, so no manual frontend or UX work is implied.

## Architectural Fit Assessment
This aligns cleanly with existing conventions already present in the same module and
elsewhere in the codebase:

- `GetConsumptionHistoryRequest.ConsumptionType` (same module, `PackingMaterials`) already
  uses a typed enum (`Domain.Features.PackingMaterials.Enums.ConsumptionType`) for a closed
  set of values, bound via `[FromQuery]` on the whole request object. `GroupBy` is the
  outlier, not the norm.
- `CatalogController.GetList` already binds an enum array (`ProductType[]? productTypes`)
  via `[FromQuery]`, confirming ASP.NET Core's native enum query-parameter binding is an
  established, working pattern in this codebase — this is not a novel technique being
  introduced.
- The fix is a pure internal refactor: no new endpoint, no new module, no cross-module
  dependency, no persistence change. It touches exactly the three files the issue names.
- The new type is an **enum**, not a DTO class — this repo's "DTOs are classes, never
  records" rule (`docs/architecture/development_guidelines.md`, `CLAUDE.md`) does not apply
  to it and is not in tension with this change.
- Per `development_guidelines.md`'s Contracts rule, request/response DTOs "live in
  `contracts/` of the specific module" — the issue's suggested location,
  `Application/Features/PackingMaterials/Contracts/ConsumptionGroupBy.cs`, is consistent
  with this, even though an enum is a supporting type rather than a DTO itself.

No integration points beyond the three named files are affected. `ConsumptionGroupBy` is
request-scoped only — it is never persisted, never crosses a module boundary, and has no
relationship to the domain-layer `ConsumptionType` enum (a different, persisted concept:
*how* a material's consumption is measured, vs. `ConsumptionGroupBy`'s *how the breakdown
report is grouped*). These must stay separate types; do not consolidate them.

## Proposed Architecture

### Component Overview
```
PackingMaterialsController.GetDailyConsumptionBreakdown
        │  [FromQuery] ConsumptionGroupBy groupBy = ConsumptionGroupBy.Material
        │  (ASP.NET Core model binder validates/converts here — new)
        ▼
GetDailyConsumptionBreakdownRequest { GroupBy: ConsumptionGroupBy }
        │
        ▼
GetDailyConsumptionBreakdownHandler.Handle
        │  switch (request.GroupBy) { Material | Product | Order }
        │  (HashSet<string> validation — removed)
        ▼
BuildGroupByMaterial / BuildGroupByProduct / BuildGroupByOrder   (unchanged)
        │
        ▼
GetDailyConsumptionBreakdownResponse { GroupBy: string = request.GroupBy.ToString() }
```

No new components. `ConsumptionGroupBy` is a new leaf type with no dependencies.

### Key Design Decisions

#### Decision 1: Enum location and ownership
**Options considered:**
(a) `Application/Features/PackingMaterials/Contracts/ConsumptionGroupBy.cs` (issue's
suggestion, Application layer, alongside the DTOs that use it);
(b) `Domain/Features/PackingMaterials/Enums/ConsumptionGroupBy.cs` (next to the sibling
`ConsumptionType` enum, Domain layer).

**Chosen approach:** (a) — Application-layer `Contracts/`, per the issue.

**Rationale:** `ConsumptionGroupBy` is a **request-shaping** concept (how the API caller
wants the report grouped), not a **domain** concept — it has no meaning inside
`PackingMaterial`/`PackingMaterialConsumption` aggregates and is never persisted or read
back from the database, unlike `ConsumptionType` which is stored on
`PackingMaterialConsumption` rows and is a genuine domain enum. Placing it in
`Application/Features/PackingMaterials/Contracts/` alongside `GetDailyConsumptionBreakdownRequest`
itself (the only consumer) keeps it next to its one call site and correctly signals "this
is API request shape, not domain state." Confirmed: `ConsumptionType` living in `Domain/.../Enums/`
is not a counter-example — that enum documents domain state; this one documents a query
parameter's closed value set, matching how a `Contracts/`-folder enum would be scoped in
any REST-facing request DTO.

#### Decision 2: Explicit enum values vs. default ordinals
**Options considered:** (a) no explicit values (`Material`, `Product`, `Order` get 0, 1, 2
by declaration order); (b) explicit values matching `ConsumptionType`'s style
(`Material = 1, Product = 2, Order = 3`).

**Chosen approach:** (a) — no explicit values.

**Rationale:** `ConsumptionType` assigns explicit values because it is **persisted** —
changing declaration order or inserting a new member in the middle would silently corrupt
already-stored data, so pinning values is required there. `ConsumptionGroupBy` is never
persisted or serialized to a durable store; it exists only for the lifetime of one HTTP
request. There is no stability requirement to protect, so explicit values add ceremony
without benefit. (If a future maintainer disagrees, explicit values are a zero-risk,
purely defensive addition — not a blocking requirement of this review.)

#### Decision 3: Invalid-`groupBy` error response shape (resolves spec's Open Question 1)
**Options considered:**
(a) Accept ASP.NET Core's default `[ApiController]` behavior: an unbindable
`[FromQuery] ConsumptionGroupBy` produces an automatic `400` with the framework's default
`ValidationProblemDetails` JSON body (`{ "type": "...", "title": "One or more validation
errors occurred.", "status": 400, "errors": { "groupBy": ["The value 'bogus' is not
valid."] } }` — exact wording depends on the ASP.NET Core version in use);
(b) Preserve the current `{ "error": "Invalid GroupBy value '...'. Must be one of:
material, product, order." }` shape by adding a custom `InvalidModelStateResponseFactory`
in `Program.cs`'s `AddControllers().ConfigureApiBehaviorOptions(...)`, or an explicit
`if (!ModelState.IsValid) return BadRequest(new { error = ... })` check in the action
(mirroring the pattern this same controller already uses for `date` parsing failures, and
for `ModelState.IsValid` checks on POST/PUT actions elsewhere in this controller).

**Chosen approach:** (a) — accept the framework default `ValidationProblemDetails` shape.

**Rationale:**
- No global `ConfigureApiBehaviorOptions`/`InvalidModelStateResponseFactory` override
  exists anywhere in the codebase today (verified: no matches for
  `InvalidModelStateResponseFactory` or `ConfigureApiBehaviorOptions` in `backend/`), so
  option (b) would introduce the *first* such override, and it would need to be scoped
  carefully to not change behavior for every other `[ApiController]` action in the API
  (a blast-radius risk for a change whose entire point is to *reduce* incidental
  complexity). A per-action `ModelState.IsValid` check is safer in blast radius but
  reintroduces manual validation logic in the controller — the exact kind of duplicated
  validation-state problem this issue is trying to eliminate, just moved one layer up
  instead of removed.
- No consumer of the current `{ error: "..." }" shape for this specific failure path was
  found: grep of `frontend/src` for this endpoint's generated client method
  (`packingMaterials_GetDailyConsumptionBreakdown`) found no call site at all — the
  endpoint currently has no wired-up frontend caller to break.
- This is a genuinely visible contract change on an existing, deployed endpoint (400 body
  shape changes), so it is called out here explicitly rather than silently accepted — but
  given no live consumer, matching the framework default is the correct trade of "less
  code, one fewer bespoke validation path" against "wire format changes on an edge case
  with zero known consumers."
- If a consumer is later discovered (e.g. an external Shoptet-adjacent integration, or a
  not-yet-written frontend feature), reintroducing shape parity is a small, isolated
  follow-up — not something that need block this refactor now.

#### Decision 4: Response `GroupBy` field stays `string` (resolves spec's Open Question 2)
**Chosen approach:** Keep `GetDailyConsumptionBreakdownResponse.GroupBy` as `string`,
assigned via `request.GroupBy.ToString()` in the three response-construction sites in the
handler (the early-return-on-empty-consumptions path, the success path, and — not present
today but worth confirming during implementation — the removed error path no longer needs
a `GroupBy` echo since that branch is now unreachable, see Decision 3).

**Rationale:** The issue's suggested fix is scoped to the *request* property only; the
response field is a separate, lower-value concern (it's an echo field, not consumed for
control flow by any known caller) and changing it is not necessary to close this finding.
Keeping it `string` also sidesteps a minor wire-format decision (enum member name vs. a
lowercase string to match today's exact casing) that isn't worth spending review cycles on
for an echo field. This is intentionally deferred, not forgotten — noted here so it isn't
silently reopened as a "new" finding by a future arch-review pass.

## Implementation Guidance

### Directory / Module Structure
One new file, two edited files — no structural changes:

```
backend/src/Anela.Heblo.Application/Features/PackingMaterials/
└── Contracts/
    └── ConsumptionGroupBy.cs                              # NEW
└── UseCases/GetDailyConsumptionBreakdown/
    ├── GetDailyConsumptionBreakdownRequest.cs              # EDIT: string → ConsumptionGroupBy
    └── GetDailyConsumptionBreakdownHandler.cs              # EDIT: remove HashSet, retype switch

backend/src/Anela.Heblo.API/Controllers/
└── PackingMaterialsController.cs                           # EDIT: string → ConsumptionGroupBy param

backend/test/Anela.Heblo.Tests/Features/PackingMaterials/
└── GetDailyConsumptionBreakdownHandlerTests.cs              # EDIT: string literals → enum values;
                                                               #        retire/rewrite GroupBy_InvalidValue_ReturnsError
```

### Interfaces and Contracts

```csharp
// Application/Features/PackingMaterials/Contracts/ConsumptionGroupBy.cs
namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

public enum ConsumptionGroupBy
{
    Material,
    Product,
    Order
}
```

```csharp
// GetDailyConsumptionBreakdownRequest.cs
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetDailyConsumptionBreakdown;

public class GetDailyConsumptionBreakdownRequest : IRequest<GetDailyConsumptionBreakdownResponse>
{
    public DateOnly Date { get; set; }
    public ConsumptionGroupBy GroupBy { get; set; } = ConsumptionGroupBy.Material;
}
```

```csharp
// PackingMaterialsController.cs — signature only
[HttpGet("consumption")]
[ProducesResponseType(typeof(GetDailyConsumptionBreakdownResponse), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
public async Task<ActionResult<GetDailyConsumptionBreakdownResponse>> GetDailyConsumptionBreakdown(
    [FromQuery] string? date,
    [FromQuery] ConsumptionGroupBy groupBy = ConsumptionGroupBy.Material,
    CancellationToken cancellationToken = default)
{
    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
    {
        return BadRequest(new { error = "Invalid date format. Expected yyyy-MM-dd." });
    }

    var request = new GetDailyConsumptionBreakdownRequest { Date = parsedDate, GroupBy = groupBy };
    var response = await _mediator.Send(request, cancellationToken);
    return response.Success ? Ok(response) : BadRequest(new { error = response.Error });
}
```
(Requires `using Anela.Heblo.Application.Features.PackingMaterials.Contracts;` in the
controller — already imported at the top of the file as a wildcard-adjacent namespace via
the existing `using Anela.Heblo.Application.Features.PackingMaterials.Contracts;` line 1,
so no new `using` is needed.)

```csharp
// GetDailyConsumptionBreakdownHandler.cs — key excerpt
public class GetDailyConsumptionBreakdownHandler
    : IRequestHandler<GetDailyConsumptionBreakdownRequest, GetDailyConsumptionBreakdownResponse>
{
    // ValidGroupByValues field DELETED

    private readonly IPackingMaterialRepository _repository;
    private readonly ILogger<GetDailyConsumptionBreakdownHandler> _logger;

    // constructor unchanged

    public async Task<GetDailyConsumptionBreakdownResponse> Handle(
        GetDailyConsumptionBreakdownRequest request,
        CancellationToken cancellationToken)
    {
        // if (!ValidGroupByValues.Contains(...)) block DELETED — binding now guarantees a valid enum value

        try
        {
            _logger.LogInformation("Loading daily consumption breakdown for {Date} grouped by {GroupBy}", request.Date, request.GroupBy);

            var consumptions = (await _repository.GetConsumptionsByDateAsync(request.Date, cancellationToken)).ToList();

            if (consumptions.Count == 0)
                return new GetDailyConsumptionBreakdownResponse { Success = true, Date = request.Date, GroupBy = request.GroupBy.ToString() };

            var materials = (await _repository.GetAllWithAllocationsAsync(cancellationToken)).ToList();

            var groups = request.GroupBy switch
            {
                ConsumptionGroupBy.Material => BuildGroupByMaterial(consumptions, materials),
                ConsumptionGroupBy.Product => BuildGroupByProduct(consumptions, materials),
                ConsumptionGroupBy.Order => BuildGroupByOrder(consumptions, materials),
                _ => throw new ArgumentOutOfRangeException(nameof(request.GroupBy), request.GroupBy, "Unhandled GroupBy value.")
            };

            return new GetDailyConsumptionBreakdownResponse
            {
                Success = true,
                Date = request.Date,
                GroupBy = request.GroupBy.ToString(),
                Groups = groups
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading daily consumption breakdown for {Date}", request.Date);

            return new GetDailyConsumptionBreakdownResponse
            {
                Success = false,
                Error = "An unexpected error occurred while loading the breakdown.",
                Date = request.Date,
                GroupBy = request.GroupBy.ToString()
            };
        }
        // BuildGroupByMaterial / BuildGroupByProduct / BuildGroupByOrder: UNCHANGED, not reproduced here
    }
}
```

### Data Flow
1. Client issues `GET /api/packing-materials/consumption?date=2026-01-15&groupBy=Product`
   (or omits `groupBy` for the `Material` default; or passes the legacy lowercase
   `product`, which still binds case-insensitively).
2. ASP.NET Core model binding converts the `groupBy` query string to `ConsumptionGroupBy`.
   If it cannot (e.g. `groupBy=bogus`), `[ApiController]`'s automatic model-state check
   short-circuits with `400` + `ValidationProblemDetails` **before** the action method body
   runs — the handler is never invoked. (See Decision 3.)
3. If binding succeeds, the controller's existing `date` parsing runs as today; on success
   it constructs `GetDailyConsumptionBreakdownRequest` with the now-strongly-typed
   `GroupBy` and sends it via MediatR.
4. The handler no longer validates `GroupBy` (impossible for it to be invalid at this
   point) and dispatches directly via the enum switch to the appropriate
   `BuildGroupBy*` method — logic and output unchanged from today for all three modes.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| `[ApiController]`'s automatic `400` for unbindable `groupBy` uses a different JSON shape than today's `{ error: "..." }`, silently breaking an undiscovered consumer. | Low | Grep confirmed no frontend call site exists for this endpoint today. Flagged explicitly in this review (Decision 3) rather than silently accepted, so it surfaces in PR review if a hidden consumer exists. |
| `GroupBy_InvalidValue_ReturnsError` handler unit test becomes untestable as originally written (invalid value can no longer reach the handler through the normal API path). | Low | Planner should replace it with either (a) an out-of-range enum cast test (`(ConsumptionGroupBy)99`) asserting the discard-arm `ArgumentOutOfRangeException`, or (b) drop the handler-level test and add/confirm an integration-level test that an invalid `groupBy` query string produces `400` at the controller/API level. Either is acceptable; (a) is cheaper and keeps parity with existing unit-test style in this file. |
| Numeric `groupBy` values (e.g. `?groupBy=0`) now bind successfully where they previously would have been rejected by the `HashSet` string check — a minor behavior widening. | Low | Inherent to ASP.NET Core's default enum binder; not worth a custom binder to close. Already called out in the spec's API/Interface Design section. No known consumer relies on numeric values being rejected. |
| Log message text for `{GroupBy}` changes casing (`"material"` → `"Material"`). | Negligible | Cosmetic only; no log-parsing consumer identified. |

## Specification Amendments
- Spec's Open Question 1 (error shape) is resolved: **accept the framework default
  `ValidationProblemDetails`** — see Decision 3. No custom `InvalidModelStateResponseFactory`
  or manual `ModelState.IsValid` check is required.
- Spec's Open Question 2 (response field type) is resolved: **keep
  `GetDailyConsumptionBreakdownResponse.GroupBy` as `string`**, assigned via
  `request.GroupBy.ToString()` — see Decision 4.
- Spec's NFR-3 test-migration guidance is refined into a concrete choice in the Risks table
  above: prefer an out-of-range-cast unit test over dropping invalid-value coverage
  entirely, so the discard arm in the handler's switch retains test coverage.

## Prerequisites
None. No migrations, no config, no infrastructure changes. This can be implemented,
tested, and merged as a single self-contained PR.
