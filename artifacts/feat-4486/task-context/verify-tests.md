### task: verify-tests
Run `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetDailyConsumptionBreakdownHandlerTests.cs` and the PackingMaterials test folder; add a test asserting `GetAllWithAllocationsAsync` is not used / unknown material id yields "Unknown" if not already covered (MockPackingMaterialRepository already implements `GetMaterialNamesByIdsAsync`; adjust the mock only if it needs call tracking).
**Done when:** all PackingMaterials tests pass.
