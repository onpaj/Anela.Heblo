Plan saved to `docs/superpowers/plans/2026-06-03-dashboard-tile-drilldown-routekey.md`.

**Summary of the plan:**
- **11 tasks**, TDD-ordered, each with bite-sized steps and full code:
  1. Pre-flight checks (route exists, resolver exists, repo methods unchanged, tree clean).
  2. Create shared `DashboardTileDrillDown` C# DTO under `Application/Features/Dashboard/Contracts/`.
  3. Write failing xUnit tests for `DataQualityStatusTile` (3 status branches, asserting `drillDown.routeKey == "dataQuality"`).
  4. Migrate `DataQualityStatusTile.cs` to emit the new DTO via a single `DrillDownRouteKey` constant.
  5. Write failing xUnit tests for `DqtYesterdayStatusTile` (no_data, Completed×2, Running, Failed, throw, metadata).
  6. Migrate `DqtYesterdayStatusTile.cs` similarly, preserving the existing logger in `catch`.
  7. Extend `drillDownRoutes.ts` registry with `dataQuality → /automation/data-quality` and write 6 resolver tests.
  8. Write 3 failing Jest+RTL tests for `DataQualityTile.tsx` covering navigate + unknown-key warn.
  9. Migrate `DataQualityTile.tsx` to use `resolveDrillDown` + `useNavigate()`.
  10. Write 8 failing Jest+RTL tests for `DqtYesterdayStatusTile.tsx` covering all visual branches.
  11. Migrate `DqtYesterdayStatusTile.tsx` similarly, preserving `data-testid` and `formatYesterdayLabel`.
  12. Final validation: `dotnet build`/`test`/`format`, `npm run build`/`lint`, dashboard test sweep, **grep guard** confirming backend tile files contain zero frontend paths.

All four arch-review amendments (NFR-3 wording, drop dual-emit, backend payload tests, grep guard) are explicitly addressed and traced in the self-review table at the end.