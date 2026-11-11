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

