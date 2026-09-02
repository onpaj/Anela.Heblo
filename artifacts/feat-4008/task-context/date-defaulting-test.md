### task: date-defaulting-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs` (append inside the class body, after the constructor)

Covers spec FR-1 — this is the test that directly guards against the coverage-gap issue's stated risk (a sign flip or wrong date source silently shifting the reported window). Uses an exact `It.Is<DateTime>` predicate on both arguments, comparing `.Date` only, per arch-review Decision 1/2.

- [ ] **Step 1: Write the test**

Add this `[Fact]` inside the class body:

```csharp
    [Fact]
    public async Task Handle_BothDatesNull_DefaultsToTrailing30DayWindow()
    {
        // Arrange
        var request = new GetIssuedInvoiceSyncStatsRequest
        {
            FromDate = null,
            ToDate = null
        };
        var expectedFrom = DateTime.Now.Date.AddDays(-30);
        var expectedTo = DateTime.Now.Date;

        _repositoryMock
            .Setup(r => r.GetSyncStatsAsync(
                It.Is<DateTime>(d => d.Date == expectedFrom),
                It.Is<DateTime>(d => d.Date == expectedTo),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IssuedInvoiceSyncStats());

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        _repositoryMock.Verify(
            r => r.GetSyncStatsAsync(
                It.Is<DateTime>(d => d.Date == expectedFrom),
                It.Is<DateTime>(d => d.Date == expectedTo),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetIssuedInvoiceSyncStatsHandlerTests.Handle_BothDatesNull_DefaultsToTrailing30DayWindow"`
Expected: `Passed! - Failed: 0, Passed: 1`

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs
git commit -m "test(invoices): cover GetIssuedInvoiceSyncStatsHandler date-range defaulting"
```

---
