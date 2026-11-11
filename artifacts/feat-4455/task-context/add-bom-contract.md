### task: add-bom-contract
Create the Logistics-owned contract and model.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/IGiftPackageBomSource.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/Models/LogisticsGiftPackagePart.cs`

- [ ] Create `LogisticsGiftPackagePart` as a class (namespace `Anela.Heblo.Application.Features.Logistics.Contracts.Models`) with `string ProductCode`, `string ProductName` (default `string.Empty`) and `double Amount`.
- [ ] Create `IGiftPackageBomSource` (namespace `Anela.Heblo.Application.Features.Logistics.Contracts`) with `Task<List<LogisticsGiftPackagePart>> GetSetPartsAsync(string setProductCode, CancellationToken cancellationToken = default);` and an XML doc stating it is Logistics-owned and implemented by Manufacture via adapter.
- [ ] No Manufacture namespace usings in either file.
- [ ] Run `cd backend && dotnet build src/Anela.Heblo.Application` and expect success.
- [ ] Commit: `refactor(logistics): add IGiftPackageBomSource contract`.

