Plan saved to `docs/superpowers/plans/2026-06-07-inject-timeprovider-manufacture-handlers.md`.

Summary of the 5-task plan:

1. **Task 1** — Inject `TimeProvider` into `GetManufactureOutputHandler`; snapshot `now` once and reuse for the date range upper bound and the gap-filling loop terminus (replaces 3 `DateTime.Now` reads).
2. **Task 2** — Inject `TimeProvider` into `CalculateBatchPlanHandler`; replace the `DateTime.Now` fallback in `ResolveSalesRanges` (1 read).
3. **Task 3** — Update `GetManufactureOutputHandlerTests` fixture (pass `Mock<TimeProvider>`); add two failing-then-passing time-shift tests pinning the upper bound and the gap-filling loop terminus to `FixedClock = 2026-03-15T10:00:00Z`.
4. **Task 4** — Update `CalculateBatchPlanHandlerTests` fixture; add a test for the fallback branch (`TimePeriod == null && ToDate == null`) asserting `DateRange.To == FixedClock.DateTime` and `From == FixedClock.DateTime.AddDays(-30)`.
5. **Task 5** — Solution-wide `dotnet format` + `dotnet build` + `dotnet test`, then grep audit confirming zero `DateTime.Now`/`UtcNow`/`Today` remains in either handler.

Key implementation decisions baked into the plan, all reflecting arch-review amendments verified against the live code:
- `Mock<TimeProvider>` via Moq — **not** `FakeTimeProvider` (testing package is not referenced and the project convention forbids adding it).
- `TimeProvider` appended as the final constructor parameter on both handlers, mirroring `CreateManufactureOrderHandler.cs:17–31`.
- `DateRange.From` / `DateRange.To` (verified — `record DateRange(DateTime From, DateTime To)`).
- Existing tests have to be updated in the same PR or the test project won't compile — called out explicitly with a "red baseline" verification step.