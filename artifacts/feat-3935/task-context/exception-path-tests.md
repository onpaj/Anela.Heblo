### task: exception-path-tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/DeleteManufactureDifficultyHandlerTests.cs` (append inside the class body)

Covers spec FR-3, both cases (A: `DeleteAsync` throws; B: `RefreshManufactureDifficultySettingsData` throws).

- [ ] **Step 1: Write the DeleteAsync-throws test**

Add this `[Fact]` inside the class body:

```csharp
    [Fact]
    public async Task Handle_DeleteAsyncThrows_ReturnsFailureWithoutPropagating()
    {
        // Arrange
        var request = new DeleteManufactureDifficultyRequest { Id = 5 };
        var existing = new ManufactureDifficultySetting
        {
            Id = 5,
            ProductCode = "PROD-ERR",
            DifficultyValue = 1,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31)
        };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _repositoryMock
            .Setup(r => r.DeleteAsync(request.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("delete boom"));

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.Message.Should().Contain("delete boom");

        _catalogRepositoryMock.Verify(
            r => r.RefreshManufactureDifficultySettingsData(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DeleteManufactureDifficultyHandlerTests.Handle_DeleteAsyncThrows_ReturnsFailureWithoutPropagating"`
Expected: `Passed! - Failed: 0, Passed: 1`

- [ ] **Step 3: Write the RefreshManufactureDifficultySettingsData-throws test**

Add this `[Fact]` inside the class body:

```csharp
    [Fact]
    public async Task Handle_RefreshCacheThrows_ReturnsFailureWithoutPropagating()
    {
        // Arrange
        var request = new DeleteManufactureDifficultyRequest { Id = 6 };
        var existing = new ManufactureDifficultySetting
        {
            Id = 6,
            ProductCode = "PROD-ERR2",
            DifficultyValue = 1,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31)
        };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _repositoryMock
            .Setup(r => r.DeleteAsync(request.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _catalogRepositoryMock
            .Setup(r => r.RefreshManufactureDifficultySettingsData(existing.ProductCode, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("refresh boom"));

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.Message.Should().Contain("refresh boom");

        // Proves the throw happened after delete succeeded, not instead of it.
        _repositoryMock.Verify(
            r => r.DeleteAsync(request.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DeleteManufactureDifficultyHandlerTests.Handle_RefreshCacheThrows_ReturnsFailureWithoutPropagating"`
Expected: `Passed! - Failed: 0, Passed: 1`

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/DeleteManufactureDifficultyHandlerTests.cs
git commit -m "test(catalog): cover DeleteManufactureDifficultyHandler exception paths"
```

---
