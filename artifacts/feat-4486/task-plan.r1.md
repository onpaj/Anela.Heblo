# Task Plan

### task: refactor-breakdown-handler
Edit `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetDailyConsumptionBreakdown/GetDailyConsumptionBreakdownHandler.cs` per design.r1.md: replace `GetAllWithAllocationsAsync` with `GetMaterialNamesByIdsAsync` (distinct consumed ids), pass `materialNameById` to the three Build methods, remove the duplicate dictionary builds.
**Done when:** `dotnet build` and `dotnet format` clean; handler compiles.

### task: verify-tests
Run `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetDailyConsumptionBreakdownHandlerTests.cs` and the PackingMaterials test folder; add a test asserting `GetAllWithAllocationsAsync` is not used / unknown material id yields "Unknown" if not already covered (MockPackingMaterialRepository already implements `GetMaterialNamesByIdsAsync`; adjust the mock only if it needs call tracking).
**Done when:** all PackingMaterials tests pass.
