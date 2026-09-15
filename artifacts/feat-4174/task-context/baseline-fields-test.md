### task: baseline-fields-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs` (add one test method)

Covers spec FR-4: the response has exactly one DTO per registered flag, with `Key`/`Description`/`DefaultValue` copied from the registry definition.

- [ ] **Step 1: Add the baseline-fields test**

Add this method to the `ListFlagsHandlerTests` class (after `Handle_OverrideKeyCaseDiffersFromRegistryKey_IsTreatedAsNoMatch`):

```csharp
    [Fact]
    public async Task Handle_AlwaysReturnsOneDtoPerRegisteredFlag_WithFieldsCopiedFromDefinition()
    {
        _repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FeatureFlagOverride>());

        var response = await CreateHandler().Handle(new ListFlagsRequest(), CancellationToken.None);

        response.Flags.Should().HaveCount(FeatureFlagRegistry.All.Count);

        var definition = FeatureFlagRegistry.ByKey[FeatureFlagKeys.LabelPrintingEnabled];
        var dto = response.Flags.Single(f => f.Key == FeatureFlagKeys.LabelPrintingEnabled);
        dto.Description.Should().Be(definition.Description);
        dto.DefaultValue.Should().Be(definition.DefaultValue);
    }
```

- [ ] **Step 2: Run the tests to verify all four pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ListFlagsHandlerTests"`

Expected: `Passed! - Failed: 0, Passed: 4, Skipped: 0`

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs
git commit -m "test(feature-flags): cover ListFlagsHandler baseline DTO field mapping (#4174)"
```

---
