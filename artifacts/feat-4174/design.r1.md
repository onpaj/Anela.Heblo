# Design: Test coverage for `ListFlagsHandler.IsOverridden` DTO branch

## Component Design

### `ListFlagsHandlerTests` (new)
- **Location:** `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs`
- **Namespace:** `Anela.Heblo.Tests.Features.FeatureFlags.UseCases.ListFlags`
- **Responsibility:** Exercise `ListFlagsHandler.Handle` end-to-end against mocked collaborators, asserting the override-merge branch (`IsOverridden`/`UpdatedBy`/`UpdatedAt`) behaves correctly for the has-override, no-override, and case-mismatch cases, plus a baseline pass-through check (`Key`/`Description`/`DefaultValue`/count).
- **Collaborators (both mocked with Moq, constructor-injected per test class field, matching `ClearFlagOverrideHandlerTests`):**
  - `Mock<IFeatureFlagOverrideRepository>` — stubs `GetAllAsync(CancellationToken)`.
  - `Mock<IFeatureFlagChecker>` — stubs `IsEnabledAsync(string, bool, CancellationToken)` with a catch-all `It.IsAny<string>()` setup that echoes `defaultValue`, since `ListFlagsHandler` calls it once per `FeatureFlagRegistry.All` entry via `Task.WhenAll` and every entry must resolve.
- **System under test:** `ListFlagsHandler` (unchanged, production code) — constructed fresh per test as `new ListFlagsHandler(_repoMock.Object, _checkerMock.Object)`.
- **No new production interfaces, no new abstractions.** This is a test-only component; nothing here is consumed by other code.

### Test method breakdown
| Test method | FR covered | Arrange | Assert |
|---|---|---|---|
| `Handle_FlagHasMatchingOverride_SetsIsOverriddenTrueAndPopulatesAuthorAndDate` | FR-1 | `GetAllAsync` returns one `FeatureFlagOverride` with `Key = FeatureFlagKeys.LabelPrintingEnabled` | DTO for that key: `IsOverridden == true`, `UpdatedBy` == seeded value, `UpdatedAt` == seeded value |
| `Handle_FlagHasNoOverride_SetsIsOverriddenFalseAndNullsAuthorAndDate` | FR-2 | `GetAllAsync` returns an empty list | DTO for a chosen key: `IsOverridden == false`, `UpdatedBy == null`, `UpdatedAt == null` |
| `Handle_OverrideKeyCaseDiffersFromRegistryKey_IsTreatedAsNoMatch` | FR-3 | `GetAllAsync` returns one override whose `Key` is an uppercased variant of `FeatureFlagKeys.LabelPrintingEnabled` | DTO for the correctly-cased registry key: `IsOverridden == false`, `UpdatedBy == null`, `UpdatedAt == null` (proves `StringComparer.Ordinal` case-sensitivity) |
| `Handle_AlwaysReturnsOneDtoPerRegisteredFlag_WithFieldsCopiedFromDefinition` | FR-4 | `GetAllAsync` returns an empty list; default checker stub | `response.Flags.Should().HaveCount(FeatureFlagRegistry.All.Count)`; for one flag, `Key`/`Description`/`DefaultValue` match the corresponding `FeatureFlagRegistry` entry |

All four are independent `[Fact]` methods (no shared mutable state beyond fresh mocks per test, per xUnit's per-test-class-instance default). No `[Theory]`/`[InlineData]` — matches house convention in this test package.

## Data Schemas
No database schema, API request/response shape, or event payload changes — this is a test-only addition against existing, unchanged types:

- `FeatureFlagOverride` (Domain) — used as-is in test arrange blocks via object initializer:
  ```csharp
  new FeatureFlagOverride
  {
      Key = FeatureFlagKeys.LabelPrintingEnabled,
      IsEnabled = true,
      UpdatedBy = "jane@example.com",
      UpdatedAt = new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc),
  }
  ```
- `FlagStatusDto` (Application/Contracts) — read-only assertion target, not constructed by the test.
- `ListFlagsRequest` / `ListFlagsResponse` — `ListFlagsRequest` is a parameterless request object (`new ListFlagsRequest()`); `ListFlagsResponse.Flags` is the `List<FlagStatusDto>` asserted against.

No fixtures, builders, or shared test-data files are introduced — each test's arrange block is self-contained per the existing convention in `ClearFlagOverrideHandlerTests.cs` / `UpsertFlagOverrideHandlerTests.cs`.
