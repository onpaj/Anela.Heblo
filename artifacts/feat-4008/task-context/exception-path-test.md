### task: exception-path-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs` (append inside the class body)

Covers spec FR-3 — asserts the full structured-failure response shape, not just `Success == false`, including the exact `Params["ErrorMessage"]` Czech message, and confirms the handler does not rethrow.

- [ ] **Step 1: Write the test**

Add this `[Fact]` inside the class body:

```csharp
    [Fact]
    public async Task Handle_RepositoryThrows_ReturnsStructuredFailure()
    {
        // Arrange
        var request = new GetIssuedInvoiceSyncStatsRequest();

        _repositoryMock
            .Setup(r => r.GetSyncStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("repository failure"));

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.Exception);
        response.Params.Should().NotBeNull();
        response.Params.Should().ContainKey("ErrorMessage")
            .WhoseValue.Should().Be("Chyba při načítání statistik synchronizace faktur");
        response.TotalInvoices.Should().Be(0);
        response.SyncedInvoices.Should().Be(0);
        response.UnsyncedInvoices.Should().Be(0);
        response.InvoicesWithErrors.Should().Be(0);
        response.CriticalErrors.Should().Be(0);
        response.LastSyncTime.Should().BeNull();
        response.SyncSuccessRate.Should().Be(0);
    }
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetIssuedInvoiceSyncStatsHandlerTests.Handle_RepositoryThrows_ReturnsStructuredFailure"`
Expected: `Passed! - Failed: 0, Passed: 1`

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs
git commit -m "test(invoices): cover GetIssuedInvoiceSyncStatsHandler exception path"
```

---
