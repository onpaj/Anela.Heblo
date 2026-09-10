# Design: Typed `GroupBy` for PackingMaterials Daily Consumption Breakdown

## Component Design

**`ConsumptionGroupBy` (new enum)**
`Application/Features/PackingMaterials/Contracts/ConsumptionGroupBy.cs`. Three members,
default declaration order (no explicit numeric values — see arch-review Decision 2):
`Material`, `Product`, `Order`. Sole responsibility: represent the closed set of valid
grouping modes for the daily consumption breakdown report. Not persisted, not shared
outside this module, not referenced by any type other than
`GetDailyConsumptionBreakdownRequest` and the controller action that constructs it.

**`GetDailyConsumptionBreakdownRequest` (edited)**
Responsibility unchanged: carries the `Date` and `GroupBy` for one breakdown query. Only
`GroupBy`'s type changes, from `string` (default `"material"`) to `ConsumptionGroupBy`
(default `ConsumptionGroupBy.Material`).

**`GetDailyConsumptionBreakdownHandler` (edited)**
Responsibility unchanged: load consumption rows for the date, group them per the requested
mode, return a `GetDailyConsumptionBreakdownResponse`. Internal change only: the
`ValidGroupByValues` HashSet guard is removed (no longer reachable — invalid values are
now rejected at the API boundary by model binding, before the handler runs), and the
dispatch `switch` operates on the `ConsumptionGroupBy` enum instead of a lowercased string.
The three private grouping methods (`BuildGroupByMaterial`, `BuildGroupByProduct`,
`BuildGroupByOrder`) are untouched — same signatures, same logic, same output shape.

**`PackingMaterialsController.GetDailyConsumptionBreakdown` (edited)**
Responsibility unchanged: parse `date`, bind `groupBy`, send the MediatR request, map
`Success`/`Error` to `Ok`/`BadRequest`. Only the `groupBy` parameter's declared type
changes, from `[FromQuery] string groupBy = "material"` to
`[FromQuery] ConsumptionGroupBy groupBy = ConsumptionGroupBy.Material"` — letting ASP.NET
Core's native model binder perform the string→enum conversion and reject unparseable
values automatically (framework-default `400` + `ValidationProblemDetails`, per
arch-review Decision 3 — no custom error-shape plumbing added).

**`GetDailyConsumptionBreakdownResponse` (unchanged in shape)**
`GroupBy` stays `string`, now populated via `request.GroupBy.ToString()` instead of being
echoed verbatim from the (formerly free-form) input string — per arch-review Decision 4.
No other field changes.

No new components, no new interfaces beyond the one enum, no changes to
`IPackingMaterialRepository` or any persistence type.

## Data Schemas

**Request wire shape** — `GET /api/packing-materials/consumption` query parameters:

| Parameter | Before | After |
|---|---|---|
| `date` | `string?` (manually parsed `yyyy-MM-dd`) | unchanged |
| `groupBy` | `string`, any value accepted by controller, validated later by handler via `HashSet` (accepted: `material`/`product`/`order`, case-insensitive) | `ConsumptionGroupBy`, validated by ASP.NET Core model binding (accepts the enum member name case-insensitively — `material`/`Material`/`MATERIAL` — or its underlying numeric value `0`/`1`/`2`; rejects anything else with `400` before the handler runs) |

**Response wire shape** — `GetDailyConsumptionBreakdownResponse` JSON: unchanged field set
(`success`, `error`, `date`, `groupBy`, `groups`). Only visible difference: on success, the
`groupBy` echo field now reflects the enum's `ToString()` casing (e.g. `"Material"`) rather
than whatever casing the caller supplied in the query string (previously echoed verbatim,
e.g. a caller-supplied `"MATERIAL"` would have echoed back `"MATERIAL"`; now it always
echoes the canonical `"Material"` regardless of the caller's casing). The invalid-`groupBy`
error branch inside the handler (`Success = false, Error = "Invalid GroupBy value..."`) is
removed entirely — that failure mode moves to the HTTP layer as a `400` with the
framework's default `ValidationProblemDetails` body, produced before the handler is ever
invoked.

No database schema changes. No event payload changes. No other endpoint's request/response
schema is affected.
