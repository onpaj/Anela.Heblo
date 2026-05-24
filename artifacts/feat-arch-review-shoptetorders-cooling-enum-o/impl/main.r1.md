Implementation complete. Here's the summary:

---

## What was done

The `Cooling` enum was relocated from `Anela.Heblo.Domain.Features.Catalog` to `Anela.Heblo.Domain.Shared` in a single atomic commit (`e8a82557`).

**Files changed:** 24 total
- 1 file created (`Shared/Cooling.cs`), 1 deleted (`Catalog/Cooling.cs`)
- 3 Domain files, 7 Application files, 3 Adapter files, 9 test files — using directives only

**All gates passed:**
- `dotnet build` — 0 errors, 0 new warnings
- `dotnet format --verify-no-changes` — clean
- `dotnet ef migrations has-pending-model-changes` — no pending changes
- Full test suite — 4453+ tests pass

**Two deviations from plan (both correct):**
1. `CarrierCoolingControllerTests.cs` was misclassified as category C in the plan — it had no other Catalog types, so the unused Catalog using was removed (category B)
2. `ShoptetIntegrationTestFixture.cs` received missing mocks for `ICarrierCoolingRepository` and `IGiftSettingRepository` — a pre-existing integration test gap discovered during the test run