All 9 tasks complete. Here's the final state:

**Build:** 0 errors  
**Tests:** 3,615 passed (0 failed), both `ModuleBoundariesTests` rows GREEN  
**Format:** `dotnet format --verify-no-changes` clean  
**Grep:** No remaining `IManufacturedProductInventoryRepository` references in Logistics source

**What was delivered:**

- `IInventoryReservationService` + `ConsumeInventoryResult` owned by `Logistics.Contracts`
- `ManufactureInventoryReservationAdapter` (`internal sealed`) in `Manufacture.Infrastructure`, registered via DI in `ManufactureModule`
- 7 adapter unit tests covering all outcome branches and the no-`SaveChangesAsync` invariant
- `ModuleBoundariesTests` upgraded to `[Theory]` with two parameterized rows: `Leaflet→KnowledgeBase` and `Logistics→Manufacture`
- All three handlers migrated: `AddItemToBoxHandler`, `RemoveItemFromBoxHandler`, `ChangeTransportBoxStateHandler`, with their test files updated to mock `IInventoryReservationService`
- `LogisticsAllowlist` covering two pre-existing out-of-scope violations (`GiftPackageManufactureService→IManufactureClient` and `→ProductPart`) with justification comments and removal criteria