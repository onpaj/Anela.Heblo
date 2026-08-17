# Design: Test Coverage for DeleteManufactureDifficultyHandler

## Component Design

### `DeleteManufactureDifficultyHandlerTests` (new)
- **Location:** `backend/test/Anela.Heblo.Tests/Features/Catalog/DeleteManufactureDifficultyHandlerTests.cs`
- **Responsibility:** Unit-test the three execution branches of `DeleteManufactureDifficultyHandler.Handle` (not-found, happy-path delete+cache-refresh, exception-caught) without any production code change.
- **Collaborators (all mocked with Moq):**
  - `Mock<IManufactureDifficultyRepository>` — stub `GetByIdAsync`/`DeleteAsync`.
  - `Mock<ICatalogRepository>` — stub/verify `RefreshManufactureDifficultySettingsData`.
  - `Mock<ILogger<DeleteManufactureDifficultyHandler>>` — bare mock, not asserted on.
- **System under test:** `DeleteManufactureDifficultyHandler`, constructed once per test (or in the constructor, matching sibling test convention) with the three mocks' `.Object`.
- **Test cases (map 1:1 to spec FRs):**
  1. `Handle_NotFound_ReturnsFailureAndPerformsNoFurtherWork` (FR-1)
  2. `Handle_ExistingEntry_DeletesRefreshesCacheInOrderAndReturnsSuccess` (FR-2)
  3. `Handle_DeleteAsyncThrows_ReturnsFailureWithoutPropagating` (FR-3 case A)
  4. `Handle_RefreshCacheThrows_ReturnsFailureWithoutPropagating` (FR-3 case B)

## Data Schemas

No new or changed schemas. Test fixtures use the existing types as-is:

```csharp
// Request (existing, unchanged)
new DeleteManufactureDifficultyRequest { Id = 42 };

// Entity returned by GetByIdAsync (existing, unchanged)
new ManufactureDifficultySetting
{
    Id = 42,
    ProductCode = "PROD-DEL",
    // other fields as needed by the entity's required members
};

// Response assertions (existing shape, unchanged)
// DeleteManufactureDifficultyResponse : BaseResponse
//   - Success (bool, inherited)
//   - Message (string?)
// Note: ErrorCode/Params (also inherited from BaseResponse) are NOT set by
// this handler and must not be asserted on.
```

No API/event payload changes — this handler's public contract (`DeleteManufactureDifficultyRequest` → `DeleteManufactureDifficultyResponse`) is unchanged.
