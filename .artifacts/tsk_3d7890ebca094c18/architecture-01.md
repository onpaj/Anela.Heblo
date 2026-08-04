# Architecture Assessment — GetWarehouseStatisticsHandler: TimeProvider + hardcoded capacity constant

## Verdict

**Approved as designed.** This is a small, well-scoped consistency fix with no architectural risk. I re-verified every factual claim in `design-01.md` against the current source (not the plan/design's paraphrase of it) and found no discrepancies. No changes to the design are needed.

## Verification performed

Read directly, not trusted from prior artifacts:

- `GetWarehouseStatisticsHandler.cs` — confirmed `const double warehouseCapacityKg = 3000;` (line 29) and `DateTime.UtcNow` (line 44) exactly as described; single-constructor-arg (`ICatalogRepository`) confirmed.
- `CatalogConstants.cs` — confirmed it's a `public static class` with `public const` (int) and `public static readonly` (DateTime) fields, each with an XML doc comment. Adding `public const double WarehouseCapacityKg` fits this shape directly.
- `GetWarehouseStatisticsResponse.cs` — confirmed `LastUpdated` is `DateTime` (not `DateTimeOffset`), and confirmed the pre-existing dead default `WarehouseCapacityKg { get; set; } = 8500` that the design correctly flags as out-of-scope noise (always overwritten in `Handle()`).
- `GetCatalogDetailHandler.cs` / `GetProductMarginsHandler.cs` — confirmed both inject `TimeProvider` via constructor and call `_timeProvider.GetUtcNow()`; confirmed `.Date` and `.DateTime` usages respectively, exactly as the design states. `.UtcDateTime` (the design's chosen conversion) is correct for a `DateTime`-typed field wanting `Kind=Utc` — neither sibling's conversion is a copy-paste fit here, so the design's reasoning for diverging is sound rather than arbitrary.
- DI registration — confirmed `TimeProvider.System` is registered via `services.AddSingleton(TimeProvider.System)` in `ServiceCollectionExtensions.cs:130`. No new DI wiring is needed; MediatR handler resolution already satisfies the added constructor parameter through the container.
- Test pattern — confirmed `GetCatalogDetailHandlerTests.cs` uses `Mock<TimeProvider>` with `.Setup(tp => tp.GetUtcNow()).Returns(new DateTimeOffset(currentDate))`, not `FakeTimeProvider`. The design correctly overrides the plan's initial (wrong) assumption of `FakeTimeProvider` — this is exactly the kind of design-stage correction that should happen, and it did.
- Grepped the full `backend` tree for `GetWarehouseStatisticsHandler(` — the only construction site is the handler's own constructor declaration. No test, factory, or manual `new` call exists anywhere that would break from the added `TimeProvider` parameter. The added constructor arg is safe.

## Alignment with existing patterns

Both changes bring this handler in line with conventions already established twice over in the same module:

- **TimeProvider injection** — identical shape to `GetCatalogDetailHandler` and `GetProductMarginsHandler`: constructor parameter, `_timeProvider` field, `_timeProvider.GetUtcNow()` call. No new pattern introduced.
- **CatalogConstants** — the class already holds exactly this kind of "business constant with no good other home" (`ALL_HISTORY_MONTHS_THRESHOLD`, `HISTORY_FLOOR_DATE`). Adding `WarehouseCapacityKg` there is additive, not a new convention.

One legitimate naming inconsistency the design already surfaced and made a deliberate call on: the two existing constants use `SCREAMING_CASE`, the new one will be `PascalCase`. I agree with the design's choice to use `PascalCase` — it matches standard C# const style used elsewhere in the codebase, and the finding's own suggested fix already specifies `WarehouseCapacityKg`. Not propagating the `SCREAMING_CASE` style further is the right call; this file already isn't stylistically pure, and matching the finding's requested name avoids a needless second naming debate.

## Scope check

The design stays inside the boundary the plan set: two files touched (`GetWarehouseStatisticsHandler.cs`, `CatalogConstants.cs`) plus one new test file. It correctly declines to:
- touch the dead `WarehouseCapacityKg = 8500` default in the response DTO (unrelated pre-existing issue, correctly flagged but left alone),
- make the capacity externally configurable (finding only asked for discoverability, not configurability),
- change the utilization formula or response contract.

This restraint is correct — nothing here should expand past the finding.

## Test coverage decision

The design resolves the plan's open question (add a test or not) by adding `GetWarehouseStatisticsHandlerTests.cs` following the exact `Mock<TimeProvider>` pattern from the sibling test file. This is the right call: the finding's stated rationale for the `TimeProvider` change is testability of `LastUpdated`, so shipping the fix without a test that exercises it would leave the justification unverified. No new pattern is introduced — it's a mechanical copy of an established test shape.

## Risks

None material. This is a pure refactor (no behavior change, no schema change, no API contract change) with a single, verified call site. The only prerequisite worth naming explicitly for implementation:

- **Confirm `LastUpdated`'s consumers tolerate `Kind=Utc`** (vs. today's `DateTime.UtcNow`, which also produces `Kind=Utc`) — trivial, since `.UtcDateTime` preserves the same `Kind` the current code already produces. No behavior change in practice, just noting it's not a blind swap of conversion methods.

No other risks or open questions remain; the design is implementation-ready.

```json
{"outcome": "done", "summary": "Verified design-01.md against actual source (handler, CatalogConstants, sibling handlers, response DTO, DI registration, existing test pattern, and a repo-wide construction-site search) — all claims hold exactly. Design approved as-is; wrote architecture-01.md with no required changes."}
```
