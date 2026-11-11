### task: refactor-breakdown-handler
Edit `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetDailyConsumptionBreakdown/GetDailyConsumptionBreakdownHandler.cs` per design.r1.md: replace `GetAllWithAllocationsAsync` with `GetMaterialNamesByIdsAsync` (distinct consumed ids), pass `materialNameById` to the three Build methods, remove the duplicate dictionary builds.
**Done when:** `dotnet build` and `dotnet format` clean; handler compiles.

