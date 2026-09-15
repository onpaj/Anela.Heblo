### task: case-mismatch-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs` (add one test method)

Covers spec FR-3: the `StringComparer.Ordinal` lookup contract — a case-differing override key must not match.

- [ ] **Step 1: Add the case-mismatch test**

Add this method to the `ListFlagsHandlerTests` class (after `Handle_FlagHasNoOverride_SetsIsOverriddenFalseAndNullsAuthorAndDate`):

```csharp
    [Fact]
    public async Task Handle_OverrideKeyCaseDiffersFromRegistryKey_IsTreatedAsNoMatch()
    {
        _repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FeatureFlagOverride>
            {
                new()
                {
                    Key = FeatureFlagKeys.LabelPrintingEnabled.ToUpperInvariant(),
                    IsEnabled = false,
                    UpdatedBy = "jane@example.com",
                    UpdatedAt = DateTime.UtcNow,
                },
            });

        var response = await CreateHandler().Handle(new ListFlagsRequest(), CancellationToken.None);

        var dto = response.Flags.Single(f => f.Key == FeatureFlagKeys.LabelPrintingEnabled);
        dto.IsOverridden.Should().BeFalse();
        dto.UpdatedBy.Should().BeNull();
        dto.UpdatedAt.Should().BeNull();
    }
```

Note: `FeatureFlagKeys.LabelPrintingEnabled` is `"is-label-printing-enabled"` — already all-lowercase, so `.ToUpperInvariant()` reliably produces a differently-cased string (`"IS-LABEL-PRINTING-ENABLED"`) that cannot equal the original under any comparer. If a future edit changes that constant's value, `.ToUpperInvariant()` on the constant itself still guarantees a mismatch as long as the key contains at least one letter — it does not hard-code the literal string.

- [ ] **Step 2: Run the tests to verify all three pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ListFlagsHandlerTests"`

Expected: `Passed! - Failed: 0, Passed: 3, Skipped: 0`

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs
git commit -m "test(feature-flags): cover ListFlagsHandler case-mismatch override lookup (#4174)"
```

---
