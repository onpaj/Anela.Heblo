# Specification: Typed `GroupBy` for PackingMaterials Daily Consumption Breakdown

## Summary
`GetDailyConsumptionBreakdownRequest.GroupBy` is currently a raw `string` validated at
runtime against a `HashSet<string>` of three literal values (`"material"`, `"product"`,
`"order"`), duplicated across the request default, the handler's validation/dispatch
logic, and the controller's query-parameter default. This spec replaces the string with
a `ConsumptionGroupBy` enum, letting ASP.NET Core's native enum model binding do the
validation and eliminating the three-way duplication. This is a backend-only,
non-behavior-changing refactor of the PackingMaterials module.

## Background
The same module already uses a typed enum (`ConsumptionType`, in
`GetConsumptionHistoryRequest`) for an analogous closed set of values, so the string-based
`GroupBy` is an inconsistency with the module's own established convention, not just a
generic style nit. The set of valid grouping modes (material / product / order) is closed
and has not changed since the endpoint was introduced; a fourth mode is not anticipated,
but if one is ever added, today's implementation requires touching three separate places
(`HashSet`, `switch`, controller default) to add it consistently, which is itself a KISS/DRY
violation the enum removes for free.

## Functional Requirements

### FR-1: Introduce `ConsumptionGroupBy` enum
Add a new enum type to the PackingMaterials module's Application-layer contracts:

```csharp
// backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/ConsumptionGroupBy.cs
namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

public enum ConsumptionGroupBy
{
    Material,
    Product,
    Order
}
```

**Acceptance criteria:**
- Enum lives in `Application/Features/PackingMaterials/Contracts/ConsumptionGroupBy.cs`.
- Three members only: `Material`, `Product`, `Order` — matching the three existing string
  values `"material"`, `"product"`, `"order"` (case-insensitive) in meaning.
- No explicit numeric values are required (unlike `ConsumptionType`, which is persisted and
  therefore pins its values); `ConsumptionGroupBy` is request-only and never persisted, so
  default ordinal assignment is fine. If the implementer prefers explicit values for
  documentation/stability, that is acceptable but not required.

### FR-2: `GetDailyConsumptionBreakdownRequest.GroupBy` becomes enum-typed
Change the request DTO property from `string` to `ConsumptionGroupBy`, defaulting to
`ConsumptionGroupBy.Material` (preserving today's default of `"material"`).

**Acceptance criteria:**
- `GetDailyConsumptionBreakdownRequest.GroupBy` is `public ConsumptionGroupBy GroupBy { get; set; } = ConsumptionGroupBy.Material;`.
- `GetDailyConsumptionBreakdownRequest` remains a plain class (per this repo's
  "DTOs are classes, never records" rule) — this FR does not touch that.

### FR-3: Controller binds the enum natively, no manual default/parse
`PackingMaterialsController.GetDailyConsumptionBreakdown` currently declares
`[FromQuery] string groupBy = "material"` and manually assigns it into the request. Change
the parameter type to `ConsumptionGroupBy groupBy = ConsumptionGroupBy.Material` so ASP.NET
Core's model binder performs the string→enum conversion and rejects unparseable values
before the handler is ever invoked.

**Acceptance criteria:**
- Controller signature: `[FromQuery] ConsumptionGroupBy groupBy = ConsumptionGroupBy.Material`.
- An invalid query value (e.g. `?groupBy=bogus`) fails ASP.NET Core model binding
  (`ModelState` invalid) rather than reaching the handler as a string — see NFR-1 for the
  exact HTTP response shape this must preserve.
- The existing `[FromQuery] string? date` parameter and its manual `DateOnly.TryParseExact`
  validation are untouched — out of scope for this change.

### FR-4: Handler drops the `HashSet` validation and string switch
Remove `GetDailyConsumptionBreakdownHandler.ValidGroupByValues` and the runtime
`if (!ValidGroupByValues.Contains(...))` guard entirely (invalid values can no longer reach
the handler once FR-3 is in place — see NFR-1 for what replaces this guard at the boundary).
Replace `request.GroupBy.ToLowerInvariant() switch { "material" => ..., "product" => ...,
"order" => ..., _ => throw ... }` with an exhaustive switch over the enum:

```csharp
var groups = request.GroupBy switch
{
    ConsumptionGroupBy.Material => BuildGroupByMaterial(consumptions, materials),
    ConsumptionGroupBy.Product => BuildGroupByProduct(consumptions, materials),
    ConsumptionGroupBy.Order => BuildGroupByOrder(consumptions, materials),
    _ => throw new ArgumentOutOfRangeException(nameof(request.GroupBy), request.GroupBy, "Unhandled GroupBy value.")
};
```

**Acceptance criteria:**
- `ValidGroupByValues` field is deleted.
- The three `BuildGroupBy*` private helper methods (`BuildGroupByMaterial`,
  `BuildGroupByProduct`, `BuildGroupByOrder`) are unchanged in signature and behavior —
  only the dispatch switch changes.
- The switch's discard arm (`_ =>`) is retained defensively (e.g. for an out-of-range enum
  cast) even though it is unreachable through normal API binding.
- `_logger.LogInformation` call that logs `request.GroupBy` continues to compile — enum
  interpolates to its member name (`Material`/`Product`/`Order`) in the log message, which
  is an acceptable, arguably clearer, change in log text (was lowercase `"material"`, becomes
  `"Material"`).

### FR-5: Response `GroupBy` field
`GetDailyConsumptionBreakdownResponse.GroupBy` (currently `string`, echoed back from the
request) is **out of scope for a type change** per the issue's suggested fix, which only
calls out the request property. Assign it as `request.GroupBy.ToString()` (or equivalent)
everywhere the handler currently does `GroupBy = request.GroupBy`, so the response DTO's
wire shape (a string) is unchanged for any existing consumer. See Open Questions for the
alternative (also changing the response field to the enum) and why this spec does not
choose it by default.

**Acceptance criteria:**
- Response JSON shape for `groupBy` is unchanged from today for the three valid inputs,
  modulo casing (today emits the lowercase value passed in by the caller verbatim, e.g.
  `"material"`; after this change it emits the enum's `ToString()`, e.g. `"Material"` —
  called out explicitly here since it is a visible wire-format change; see Open Questions).

## Non-Functional Requirements

### NFR-1: Error response shape for invalid `groupBy` must not silently change
Today, an invalid `groupBy` query value reaches the handler and the handler returns
`200 OK`-shaped `GetDailyConsumptionBreakdownResponse { Success = false, Error = "Invalid
GroupBy value '...'. Must be one of: material, product, order." }`, which the controller
then maps to `BadRequest` (400) via `return response.Success ? Ok(response) : BadRequest(...)`.
After this change, an invalid `groupBy` fails **model binding** before the action method
runs. ASP.NET Core's default behavior for a `[FromQuery]` enum parameter that fails to bind
depends on whether `[ApiController]` is present (it is, here): with `[ApiController]`, an
unbindable required parameter with no `[Required]`/nullable annotation issue produces an
automatic `400 Bad Request` via the built-in `InvalidModelStateResponseFactory`, but the
**payload shape differs** from today's `{ error: "..." }` — it is the framework's default
`ValidationProblemDetails` JSON. This is a user-visible contract change for the error path
and must be called out to the architect as a decision point (see Open Questions) — the
architect's Specification Amendments section should state explicitly whether this
shape change is accepted as-is, or whether a custom invalid-model-state handler / explicit
`ModelState.IsValid` check + custom `BadRequest(new { error = ... })` is required to
preserve the current `{ error: "..." }` shape.

### NFR-2: No behavior change for valid inputs
For all three valid `groupBy` values, the response body (aside from the `groupBy` field's
casing per FR-5) and grouping logic must be byte-for-byte identical to today's behavior.
No new grouping mode, no change to `BuildGroupByMaterial`/`BuildGroupByProduct`/
`BuildGroupByOrder`, no change to sorting or `Details` shape.

### NFR-3: Test coverage carries over
`GetDailyConsumptionBreakdownHandlerTests.cs` currently constructs requests with string
literals (`GroupBy = "material"`, `"order"`, `"product"`, `"invalid"`) and includes a
`GroupBy_InvalidValue_ReturnsError` test asserting the handler-level runtime validation.
Because invalid-value validation moves to the ASP.NET Core model-binding layer (outside the
handler), `GroupBy_InvalidValue_ReturnsError` as written can no longer be expressed as a
handler unit test — the handler will no longer be reachable with an invalid enum value in
practice, though it remains theoretically callable directly with an out-of-range enum cast
(e.g. `(ConsumptionGroupBy)99`), which the discard arm's `ArgumentOutOfRangeException`
still covers. Existing valid-value tests (`GroupByMaterial_...`, `GroupByOrder_...`,
`GroupByProduct_...`) must be updated to use `ConsumptionGroupBy.Material` /
`.Order` / `.Product` in place of the string literals and continue to pass unchanged in
their assertions.

## Data Model
No persisted data model changes. `ConsumptionGroupBy` is a request-only, non-persisted
enum — it is not written to the database and has no relationship to the domain-layer
`ConsumptionType` enum (`Domain/Features/PackingMaterials/Enums/ConsumptionType.cs`), which
models a different, persisted concept (how a packing material's consumption is *measured*:
per-order/per-product/per-day) and must not be confused with or merged into this new type.

## API / Interface Design
- **Endpoint**: `GET /api/packing-materials/consumption` (unchanged route/method).
- **Query parameters**: `date` (unchanged, `string?`, manually parsed `yyyy-MM-dd`),
  `groupBy` (changed from `string = "material"` to `ConsumptionGroupBy = ConsumptionGroupBy.Material`).
  Accepted wire values for `groupBy` after the change: ASP.NET Core's default enum model
  binder accepts the member name case-insensitively (`material`, `Material`, `MATERIAL`
  all bind to `ConsumptionGroupBy.Material`) or the underlying numeric value (`0`, `1`, `2`
  if no explicit values are assigned) — this is a **superset** of today's accepted string
  values, which only accepted the exact lowercase words (case-insensitively, since the
  `HashSet` used `OrdinalIgnoreCase`) `material`/`product`/`order`. Numeric binding
  (`?groupBy=0`) is new and was not previously possible; this is a minor scope addition
  inherent to ASP.NET Core's default enum binder and is not something this change can avoid
  without a custom model binder, which is out of scope (see Out of Scope).
- **Response shape**: unchanged except `groupBy` field casing (see FR-5, NFR-1).

## Dependencies
None beyond what already exists in the module (MediatR, ASP.NET Core, EF Core via
`IPackingMaterialRepository`). No new NuGet packages, no new external services.

## Out of Scope
- Changing `GetDailyConsumptionBreakdownResponse.GroupBy` from `string` to the enum type
  (kept as `string` per FR-5's default choice — flagged as an open question).
- Writing a custom `[FromQuery]` enum binder/filter to reject numeric or out-of-range
  values with a bespoke `{ error: "..." }" shape — only in scope if the architect's
  Specification Amendments decide NFR-1's shape-preservation is required.
- Any change to the `date` query parameter, its validation, or its error response.
- Any change to `ConsumptionType` (the module's other, persisted enum) or to any other
  PackingMaterials endpoint.
- Frontend changes: the generated TypeScript client (`frontend/src/api/generated/api-client.ts`)
  regenerates automatically on the next `npm run build` (per
  `docs/development/api-client-generation.md`); no frontend call site currently invokes
  `packingMaterials_GetDailyConsumptionBreakdown` (grep found no hook/component usage), so
  no manual frontend code changes are anticipated, only the auto-regenerated client file.

## Open Questions

None. Two decision points came up during analysis and are resolved here with assumptions
(both are architecture-level judgment calls, not product/business questions, so they are
settled now rather than left blocking — the architect phase should confirm or override
either in its Specification Amendments if it disagrees):

1. **Error response shape for invalid `groupBy` (NFR-1).** Assumption: adopt ASP.NET Core's
   default `ValidationProblemDetails` 400 payload for an unparseable `groupBy` rather than
   building custom plumbing to preserve the current `{ error: "Invalid GroupBy value
   '...'..." }` shape. No frontend or external consumer of this specific error path was
   found (grep of `frontend/src` found no call site for this endpoint at all), so the
   simpler framework-default path is preferred over adding an `InvalidModelStateResponseFactory`
   override or manual `ModelState.IsValid` check purely to preserve wire-format nostalgia.
2. **Response `GroupBy` field type (FR-5).** Assumption: leave
   `GetDailyConsumptionBreakdownResponse.GroupBy` as `string` (assigned via
   `request.GroupBy.ToString()`), matching the issue's suggested fix, which only asks for
   the request property to change. The request/response asymmetry this leaves behind is
   minor and pre-existing in spirit (the response also already stringly-echoes the request
   today); revisiting it is reasonable future cleanup but not part of this finding's scope.

## Status: COMPLETE
