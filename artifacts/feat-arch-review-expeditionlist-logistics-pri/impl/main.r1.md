All three tasks are complete. Here is the output summary:

---

# Implementation: Remove dead mutable static `PrintPickingListRequest.DefaultCarriers`

## What was implemented

Eliminated a global-mutation hazard by removing the public-setter static `DefaultCarriers` property from `PrintPickingListRequest`. The lone integration test consumer was retargeted to `ExpeditionPickingRequest.DefaultCarriers` — the canonical read-only constant that all production code already uses. No behavior change; carrier set `{ Zasilkovna, GLS, PPL, Osobak }` is identical.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs` — removed the `public static IList<Carriers> DefaultCarriers { get; set; }` property (lines 16–22); all other members preserved
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs` — added `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;`; changed line 88 from `PrintPickingListRequest.DefaultCarriers` to `ExpeditionPickingRequest.DefaultCarriers`

## Tests

- All 4,766 non-integration tests passed (`dotnet test --filter "Category!=Integration"`)
- `PickingListIntegrationTests` excluded from CI (requires live Shoptet store); the test file compiles cleanly and will exercise the production carrier set on next manual run

## How to verify

```bash
# Confirm zero references to the removed property
grep -rn 'PrintPickingListRequest\.DefaultCarriers' --include='*.cs' backend/

# Confirm canonical constant is intact
grep -A 6 'public static IList<Carriers> DefaultCarriers' backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs

# Run tests
cd backend && dotnet test Anela.Heblo.sln --filter "Category!=Integration"

# Inspect commit
git show --stat HEAD
```

## Notes

The code quality reviewer flagged a pre-existing using-directive ordering issue (`Domain` before `Application.Logistics.Picking`) in the test file. This ordering predates this change and is preserved unchanged per the surgical-changes policy in CLAUDE.md. `dotnet format` ran without reordering them, confirming the project has no enforced ordering rule for this.

## PR Summary

Removed `PrintPickingListRequest.DefaultCarriers` — a public-setter static property that duplicated the already read-only `ExpeditionPickingRequest.DefaultCarriers` used by all production callers. The mutation hazard was eliminated and the lone integration test consumer was redirected to the canonical constant, so the test now exercises the same carrier set as production code.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs` — deleted the static `DefaultCarriers` property; all other members untouched
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs` — added `.Contracts` using directive; retargeted line 88 to `ExpeditionPickingRequest.DefaultCarriers`

## Status
DONE