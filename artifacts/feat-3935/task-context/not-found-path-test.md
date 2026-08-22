### task: not-found-path-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/DeleteManufactureDifficultyHandlerTests.cs` (append inside the class body, after the constructor)

Covers spec FR-1.

- [ ] **Step 1: Write the test**

Add this `[Fact]` inside the class body:

```csharp
    [Fact]
    public async Task Handle_NotFound_ReturnsFailureAndPerformsNoFurtherWork()
    {
        // Arrange
        var request = new DeleteManufactureDifficultyRequest { Id = 42 };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureDifficultySetting?)null);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.Message.Should().Be("ManufactureDifficultyHistory with ID 42 not found");

        _repositoryMock.Verify(
            r => r.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _catalogRepositoryMock.Verify(
            r => r.RefreshManufactureDifficultySettingsData(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DeleteManufactureDifficultyHandlerTests.Handle_NotFound_ReturnsFailureAndPerformsNoFurtherWork"`
Expected: `Passed! - Failed: 0, Passed: 1`

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/DeleteManufactureDifficultyHandlerTests.cs
git commit -m "test(catalog): cover DeleteManufactureDifficultyHandler not-found path"
```

---
