### task: no-override-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs` (add one test method to the class created in the previous task)

Covers spec FR-2 (no-override path).

- [ ] **Step 1: Add the no-override test**

Add this method to the `ListFlagsHandlerTests` class (after `Handle_FlagHasMatchingOverride_SetsIsOverriddenTrueAndPopulatesAuthorAndDate`):

```csharp
    [Fact]
    public async Task Handle_FlagHasNoOverride_SetsIsOverriddenFalseAndNullsAuthorAndDate()
    {
        _repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FeatureFlagOverride>());

        var response = await CreateHandler().Handle(new ListFlagsRequest(), CancellationToken.None);

        var dto = response.Flags.Single(f => f.Key == FeatureFlagKeys.LabelPrintingEnabled);
        dto.IsOverridden.Should().BeFalse();
        dto.UpdatedBy.Should().BeNull();
        dto.UpdatedAt.Should().BeNull();
    }
```

- [ ] **Step 2: Run the tests to verify both pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ListFlagsHandlerTests"`

Expected: `Passed! - Failed: 0, Passed: 2, Skipped: 0`

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs
git commit -m "test(feature-flags): cover ListFlagsHandler no-override path (#4174)"
```

---
