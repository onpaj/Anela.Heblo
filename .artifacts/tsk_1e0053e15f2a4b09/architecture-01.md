# Architecture check: Inject TimeProvider into TimePeriodResolver

## Verdict

**Design in `design-01.md` is sound and matches the codebase's actual invariants. Approved as-is — no changes required before implementation.**

This is a single-seam, mechanical DI refactor with no contract or schema surface. I re-verified every factual claim the design and plan make against the current repo state (not just against each other) rather than taking them on faith, since this step exists to catch drift between a design and reality.

## What I checked against live source

**1. `TimePeriodResolver.cs` "before" state.** Read `backend/src/Anela.Heblo.Application/Common/TimePeriods/TimePeriodResolver.cs` in full — matches the design's "Before" snippet exactly: parameterless class, `var now = DateTime.UtcNow;` on line 7, five private static `ResolveForX(DateTime now)` helpers untouched by the proposal. Confirms the change is a true single-seam edit (constructor + one assignment line).

**2. `TimeWindowParser` precedent shape.** Read `Features/Analytics/Services/TimeWindowParser.cs` — constructor injection of `TimeProvider` into a `private readonly` field is exactly as the design describes, and it does call `_timeProvider.GetLocalNow().Date`, confirming the design's noted deviation is real, not invented.

**3. `Dev_Guidelines_time.md` rule.** Read the doc directly — the "✅ CORRECT" business-logic example is verbatim `result.Date = _timeProvider.GetUtcNow().DateTime;`, and `GetLocalNow()` in business logic is explicitly labeled "❌ WRONG". The design's choice to diverge from `TimeWindowParser`'s local-time precedent and instead use `GetUtcNow().DateTime` is the textually correct reading of the governing doc, not just a judgment call — **confirmed correct**, and additionally the file's own history (`docs/superpowers/plans/2026-05-12-shared-time-period-module.md`) shows `TimePeriodResolver` originated with `DateTime.UtcNow`, i.e. UTC was always the intended semantic for this class specifically.

**4. `GetUtcNow().DateTime` is the dominant house pattern, not an isolated choice.** Grepped `_timeProvider.GetUtcNow().DateTime` across `backend/src` — **45 call sites**, spanning Catalog, Dashboard, Manufacture, Logistics, DataQuality, and the Flexi adapter. Notably, `CalculateBatchPlanHandler.cs:61` — one of the two production consumers of `ITimePeriodResolver` — already uses `_timeProvider.GetUtcNow().DateTime` for its own `endDate` fallback, right alongside its call into `_timePeriodResolver.Resolve(...)`. This means the resolver's `now` and the handler's own `now` will, after this fix, come from the *same* injected `TimeProvider` instance for the first time — a coherence improvement not explicitly called out in the design but a natural consequence of it.

**5. DI wiring.** Read `ApplicationModule.cs:70` (`services.AddScoped<ITimePeriodResolver, TimePeriodResolver>();` — unchanged, no edit needed) and `ServiceCollectionExtensions.cs:130` (`services.AddSingleton(TimeProvider.System);`). A scoped service depending on a singleton is the safe direction (no captive-dependency risk, which only occurs the other way round). Design's "no DI change needed" claim is correct.

**6. `Kind` semantics — a subtlety the design doesn't mention but is a non-issue.** `DateTimeOffset.DateTime` returns `DateTimeKind.Unspecified`, whereas the original `DateTime.UtcNow` has `Kind.Utc`. This is a real, if minor, behavioral shift in the value's `Kind` tag (though not its instant). I checked whether either consumer (`CalculateBatchPlanHandler`, `GetManufacturingStockAnalysisHandler`) or `DateRange` inspects `.Kind` anywhere — grep found none, and `DateRange` (`sealed record DateRange(DateTime From, DateTime To)`) does no Kind-aware comparison. Since this exact `Kind.Unspecified` pattern is already how the other 45 call sites in this codebase behave, this is consistent with house convention, not a new risk. Worth a one-line mention to the implementer so it isn't mistaken for an oversight, but not a blocker.

**7. Consumers use the interface, not the concrete class.** Grepped `ITimePeriodResolver|new TimePeriodResolver(` across `backend/src` and `backend/test`. Production consumers (`CalculateBatchPlanHandler.cs`, `GetManufacturingStockAnalysisHandler.cs`) and their test files (`CalculateBatchPlanHandlerTests.cs`, `GetManufacturingStockAnalysisHandlerTests.cs`) reference only the interface — confirmed, matching FR-5 in the plan. The **only** direct concrete construction anywhere is `TimePeriodResolverTests.cs:8` (`new TimePeriodResolver()`), which the design already targets for the required update.

**8. Test convention and package availability.** Confirmed `Microsoft.Extensions.TimeProvider.Testing` v8.1.0 is already referenced in `Anela.Heblo.Tests.csproj` — no new package needed, as claimed. Read `UpcomingProductionTileTests.cs` as a live precedent: `new FakeTimeProvider(FrozenXxxUtc)` passed straight into the constructor of the class under test, with a comment explaining *why* that particular instant was chosen. The design's `FrozenNowUtc` field + inline `_sut` construction follows this exact convention, including the "why this instant" reasoning the precedent establishes as the house style.

**9. Hand-derived expected `DateRange` values — independently recomputed, not just re-read.** I reimplemented all five `ResolveForX` helpers in a scratch script and fed `now = 2026-02-01T00:00:00`. Every value in the design's table matches exactly:

| Period | Design's claim | Independently recomputed | Match |
|---|---|---|---|
| PreviousQuarter | `[2025-11-01, 2026-01-31]` | `[2025-11-01, 2026-01-31]` | ✅ |
| FutureQuarter | `[2025-02-01, 2025-04-30]` | `[2025-02-01, 2025-04-30]` | ✅ |
| Y2Y | `[2025-02-01, 2026-01-31]` | `[2025-02-01, 2026-01-31]` | ✅ |
| PreviousSeason | `[2025-10-01, 2026-01-31]` | `[2025-10-01, 2026-01-31]` | ✅ |
| Q9M range A | `[2025-08-01, 2026-02-01]` | `[2025-08-01, 2026-02-01]` | ✅ |
| Q9M range B | `[2025-02-01, 2025-05-01]` | `[2025-02-01, 2025-05-01]` | ✅ |

The design's reasoning for picking midnight-on-the-1st (exercises month-boundary arithmetic *and* avoids a leaking time-of-day component in Q9M's untruncated `rangeATo`/`rangeBTo`) is correct: `Q9M` is the only one of the five helpers that doesn't truncate `now` to a `new DateTime(y, m, 1)` boundary, so it's the one case where a non-midnight frozen clock would have produced noisy, un-hand-verifiable expected values.

**10. Current test file's actual state.** Read `TimePeriodResolverTests.cs` in full — confirms it currently has exactly the loose `BeBefore(DateTime.UtcNow)`-style assertions the task description describes, across 9 `[Fact]` methods, with `_sut = new()` on line 8. The design's characterization of "what needs to change" is accurate to the file as it stands today, not a stale read.

## Risks and mitigations

- **Kind change (`Utc` → `Unspecified`) on the resolved `DateTime` values** — mitigated by precedent: this is already the codebase-wide behavior of `GetUtcNow().DateTime` (45 existing call sites), and no consumer inspects `.Kind`. No action needed beyond implementer awareness.
- **None of the other risks called out in the plan's "Open questions" section survive scrutiny as real risks** — the `GetUtcNow()` vs `GetLocalNow()` judgment call is resolved correctly per the doc's explicit "✅/❌" example, not just a reasonable guess.

## Implementation guidance (unchanged from design/plan, confirmed viable)

1. `TimePeriodResolver`: add `private readonly TimeProvider _timeProvider` + constructor; replace `DateTime.UtcNow` → `_timeProvider.GetUtcNow().DateTime`. No other line in the file changes.
2. No DI registration edit — `ApplicationModule.cs:70` and the singleton `TimeProvider.System` registration already compose correctly.
3. `TimePeriodResolverTests.cs`: construct via `new(new FakeTimeProvider(FrozenNowUtc))` with `FrozenNowUtc = 2026-02-01T00:00:00Z`; replace each `BeBefore(DateTime.UtcNow)`-style assertion with the exact `DateRange` values verified in the table above.
4. Verification sequence from the plan (`grep -rn "new TimePeriodResolver("`, `dotnet build`, `dotnet format`, `dotnet test`) is sufficient — no additional checks needed given the interface-only consumption pattern confirmed in point 7 above.

No prerequisites block starting implementation.
