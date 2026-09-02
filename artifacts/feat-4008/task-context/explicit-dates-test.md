### task: explicit-dates-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs` (append inside the class body)

Covers spec FR-2 — confirms explicit request dates are passed through unchanged, not overwritten by the 30-day default.

- [ ] **Step 1: Write the test**

Add this `[Fact]` inside the class body:

```csharp
    [Fact]
    public async Task Handle_ExplicitDates_PassesThemThroughUnchanged()
    {
        // Arrange
        var explicitFrom = new DateTime(2026, 1, 5);
        var explicitTo = new DateTime(2026, 1, 20);
        var request = new GetIssuedInvoiceSyncStatsRequest
        {
            FromDate = explicitFrom,
            ToDate = explicitTo
        };

        _repositoryMock
            .Setup(r => r.GetSyncStatsAsync(explicitFrom, explicitTo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IssuedInvoiceSyncStats());

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        _repositoryMock.Verify(
            r => r.GetSyncStatsAsync(explicitFrom, explicitTo, It.IsAny<CancellationToken>()),
            Times.Once);
    }
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetIssuedInvoiceSyncStatsHandlerTests.Handle_ExplicitDates_PassesThemThroughUnchanged"`
Expected: `Passed! - Failed: 0, Passed: 1`

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs
git commit -m "test(invoices): cover GetIssuedInvoiceSyncStatsHandler explicit date pass-through"
```

---
