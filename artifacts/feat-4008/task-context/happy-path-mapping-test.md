### task: happy-path-mapping-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs` (append inside the class body)

Covers spec FR-4 — asserts every response field is mapped one-to-one from the repository's `IssuedInvoiceSyncStats`, including the computed `SyncSuccessRate` (which has no setter on the domain type — `TotalInvoices`/`SyncedInvoices` are chosen so the computed rate is distinctive and easy to assert, per arch-review Risk row 3).

- [ ] **Step 1: Write the test**

Add this `[Fact]` inside the class body:

```csharp
    [Fact]
    public async Task Handle_RepositoryReturnsStats_MapsAllFieldsOntoResponse()
    {
        // Arrange
        var request = new GetIssuedInvoiceSyncStatsRequest
        {
            FromDate = new DateTime(2026, 2, 1),
            ToDate = new DateTime(2026, 2, 28)
        };
        var lastSync = new DateTime(2026, 2, 27, 14, 30, 0);
        var stats = new IssuedInvoiceSyncStats
        {
            TotalInvoices = 200,
            SyncedInvoices = 150,   // SyncSuccessRate = 150/200*100 = 75
            UnsyncedInvoices = 50,
            InvoicesWithErrors = 12,
            CriticalErrors = 3,
            LastSyncTime = lastSync
        };

        _repositoryMock
            .Setup(r => r.GetSyncStatsAsync(request.FromDate.Value, request.ToDate.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.TotalInvoices.Should().Be(200);
        response.SyncedInvoices.Should().Be(150);
        response.UnsyncedInvoices.Should().Be(50);
        response.InvoicesWithErrors.Should().Be(12);
        response.CriticalErrors.Should().Be(3);
        response.LastSyncTime.Should().Be(lastSync);
        response.SyncSuccessRate.Should().Be(75m);
    }
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetIssuedInvoiceSyncStatsHandlerTests.Handle_RepositoryReturnsStats_MapsAllFieldsOntoResponse"`
Expected: `Passed! - Failed: 0, Passed: 1`

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs
git commit -m "test(invoices): cover GetIssuedInvoiceSyncStatsHandler happy-path field mapping"
```

---
