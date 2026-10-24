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

