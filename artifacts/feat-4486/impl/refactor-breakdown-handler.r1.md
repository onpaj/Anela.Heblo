# Impl: refactor-breakdown-handler r1
Handler now loads names via `GetMaterialNamesByIdsAsync(distinct consumed ids)` instead of `GetAllWithAllocationsAsync`; the three Build methods take `IReadOnlyDictionary<int,string> materialNameById`; duplicate dictionary builds removed. `dotnet build` 0 errors; PackingMaterials tests 85/85 pass; dotnet format clean.
