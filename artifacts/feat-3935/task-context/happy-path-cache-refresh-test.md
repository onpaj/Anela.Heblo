### task: happy-path-cache-refresh-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/DeleteManufactureDifficultyHandlerTests.cs` (append inside the class body)

Covers spec FR-2 — this is the test that directly guards against the coverage-gap issue's stated risk (cache-refresh dropped or given the wrong `productCode`).

- [ ] **Step 1: Write the test**

Add this `[Fact]` inside the class body:

```csharp
    [Fact]
    public async Task Handle_ExistingEntry_DeletesRefreshesCacheInOrderAndReturnsSuccess()
    {
        // Arrange
        var request = new DeleteManufactureDifficultyRequest { Id = 11 };
        var existing = new ManufactureDifficultySetting
        {
            Id = 11,
            ProductCode = "PROD-HAPPY",
            DifficultyValue = 2,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31)
        };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var callSequence = new MockSequence();
        _repositoryMock
            .InSequence(callSequence)
            .Setup(r => r.DeleteAsync(request.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _catalogRepositoryMock
            .InSequence(callSequence)
            .Setup(r => r.RefreshManufactureDifficultySettingsData(existing.ProductCode, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Message.Should().Be("Manufacture difficulty deleted successfully");

        _repositoryMock.Verify(
            r => r.DeleteAsync(request.Id, It.IsAny<CancellationToken>()),
            Times.Once);

        // Crux of the original coverage gap: the cache refresh must receive the
        // deleted entity's ProductCode, not any value derived from the request.
        _catalogRepositoryMock.Verify(
            r => r.RefreshManufactureDifficultySettingsData(existing.ProductCode, It.IsAny<CancellationToken>()),
            Times.Once);
    }
```

Note: because both mock setups are registered `InSequence(callSequence)`, Moq will throw a `MockException` at invocation time if `RefreshManufactureDifficultySettingsData` is ever called before `DeleteAsync` — this is what proves the ordering requirement from FR-2, not a separate assertion.

- [ ] **Step 2: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DeleteManufactureDifficultyHandlerTests.Handle_ExistingEntry_DeletesRefreshesCacheInOrderAndReturnsSuccess"`
Expected: `Passed! - Failed: 0, Passed: 1`

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/DeleteManufactureDifficultyHandlerTests.cs
git commit -m "test(catalog): cover DeleteManufactureDifficultyHandler delete+cache-refresh happy path"
```

---
