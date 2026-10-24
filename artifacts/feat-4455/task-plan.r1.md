# Implementation Plan: Decouple GiftPackageManufactureService from IManufactureClient

**Goal:** Replace the direct `IManufactureClient` dependency in Logistics with a Logistics-owned `IGiftPackageBomSource`, implemented by a Manufacture adapter.
**Architecture:** Consumer-owned contract in `Logistics/Contracts`, provider adapter in `Manufacture/Infrastructure`, DI in `ManufactureModule`. See `arch-review.r1.md`, `design.r1.md`.
**Tech stack:** .NET 8, xUnit, Moq. Validate with `dotnet build`, `dotnet format`, targeted `dotnet test`.

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

### task: add-manufacture-adapter
Implement the contract in Manufacture and register it. Depends on: add-bom-contract.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureGiftPackageBomAdapter.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureGiftPackageBomAdapterTests.cs`

- [ ] Write the failing tests first: (1) maps `ProductPart` list to `LogisticsGiftPackagePart` preserving order and all three fields, (2) empty client result gives empty list, (3) client exception propagates, (4) the cancellation token and set code are forwarded to `IManufactureClient.GetSetPartsAsync` (Moq verify).
- [ ] Implement `internal sealed class ManufactureGiftPackageBomAdapter : IGiftPackageBomSource` with ctor `(IManufactureClient client)`, following the style of `ManufactureCatalogSourceAdapter`. Check whether the test project has `InternalsVisibleTo` for the Application assembly (sibling adapter tests will show); if not, use the same approach sibling tests use.
- [ ] In `ManufactureModule.AddManufactureModule`, next to the `IInventoryReservationService` / `ICatalogManufactureSource` registrations, add `services.AddScoped<IGiftPackageBomSource, ManufactureGiftPackageBomAdapter>();` with a comment: Manufacture implements Logistics' IGiftPackageBomSource; DI owned by provider.
- [ ] Run `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ManufactureGiftPackageBomAdapter"` and expect pass.
- [ ] Commit: `refactor(manufacture): add GiftPackageBom adapter for Logistics`.

### task: switch-consumer
Point `GiftPackageManufactureService` at the new contract and update tests. Depends on: add-manufacture-adapter.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufacture/GiftPackageManufactureAtomicityIntegrationTests.cs`
- Check: `backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/*Tests.cs` (module/handler tests) for DI or mock setups that need the new service dependency.

- [ ] In the service: replace field/ctor param `IManufactureClient _manufactureClient` with `IGiftPackageBomSource _bomSource` (same ctor position); remove `using Anela.Heblo.Domain.Features.Manufacture;` if no longer needed; call `_bomSource.GetSetPartsAsync(giftPackageCode, cancellationToken)`; the rest of the loop is unchanged (property names are identical).
- [ ] In `GiftPackageManufactureServiceTests`: replace `Mock<IManufactureClient>` with `Mock<IGiftPackageBomSource>`, and `CreateTestProductParts()` returns `List<LogisticsGiftPackagePart>`; keep all assertions.
- [ ] In the atomicity integration tests: same replacement in the helper signatures at the `new GiftPackageManufactureService(` call and the `ProductPart` setups.
- [ ] `grep -rn "IManufactureClient" backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture` and fix anything that builds the service or depends on `IManufactureClient` for gift packages.
- [ ] Run `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~GiftPackage"` and expect pass.
- [ ] Commit: `refactor(logistics): GiftPackageManufactureService uses IGiftPackageBomSource`.

### task: cleanup-architecture-allowlist
Remove the obsolete boundary exceptions. Depends on: switch-consumer.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

- [ ] Delete the two entries (`... -> IManufactureClient` and `... -> ProductPart`) and their comments from `LogisticsAllowlist`; leave the set empty and update the lead comment to "Empty -- no active violations." (match the style of `PurchaseAllowlist`).
- [ ] Run `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ModuleBoundariesTests"` and expect pass. If a stale-allowlist test exists it should now pass too.
- [ ] Commit: `test(architecture): drop Logistics -> Manufacture allowlist entries`.

### task: final-validation
Full validation and docs. Depends on: cleanup-architecture-allowlist.

- [ ] `cd backend && dotnet build` expect no errors; `dotnet format` and check `git diff` only touches intended files.
- [ ] `dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~GiftPackage|FullyQualifiedName~ModuleBoundaries|FullyQualifiedName~Logistics|FullyQualifiedName~Manufacture"` expect pass; also verify the DI container resolves `IGiftPackageManufactureService` (existing module/DI tests, or a quick check in `GiftPackageManufactureModuleTests`).
- [ ] Check `docs/processes/INDEX.md` for a gift-package process doc owning the touched code; if present, bump `verified_at` (refactor, no behaviour change) and run `python3 scripts/process-docs/check.py index`.
- [ ] Optionally add a note to `docs/architecture/development_guidelines.md` only if it lists remaining violations of this pattern for Logistics (do not otherwise touch docs).
- [ ] Commit any remaining changes: `chore: validation and docs for IGiftPackageBomSource`.
