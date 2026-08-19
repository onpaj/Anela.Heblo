### task: clock-advance-regression-test

Prove the service reads the injected clock on every call rather than capturing an instant once — the assertion that would still hold if someone reintroduced a static clock read is not enough on its own.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs` (new test, inserted after `CompleteReceivedBoxesAsync_OperationsSubmitted_LeavesBoxInReceived` and before the `SetupQueryReturns` helper)

- [ ] **Step 1: Write the failing test**

Insert this test method after `CompleteReceivedBoxesAsync_OperationsSubmitted_LeavesBoxInReceived` and before the `private void SetupQueryReturns(...)` helper:

```csharp
    [Fact]
    public async Task CompleteReceivedBoxesAsync_ClockAdvanced_WritesAdvancedTimestamp()
    {
        var box = CreateBox(1, "BOX-001", TransportBoxState.Received);
        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box });

        SetupQueryReturns(box.Id, new List<LogisticsStockOperationStatus>
        {
            CreateStatus("BOX-000001-PROD1", LogisticsStockOperationState.Completed),
        });

        _transportBoxRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _transportBoxRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _timeProvider.Advance(TimeSpan.FromHours(1));

        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

        var expectedTimestamp = FrozenNow.UtcDateTime.AddHours(1);

        box.State.Should().Be(TransportBoxState.Stocked);
        box.LastStateChanged.Should().Be(expectedTimestamp);

        box.StateLog.Should().ContainSingle();
        var stateLogEntry = box.StateLog.Single();
        stateLogEntry.State.Should().Be(TransportBoxState.Stocked);
        stateLogEntry.StateDate.Should().Be(expectedTimestamp);
        stateLogEntry.User.Should().Be("System");
    }
```

- [ ] **Step 2: Run the new test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxCompletionServiceTests"`

Expected: PASS — `Passed! - Failed: 0, Passed: 8`.

Note: this test passes immediately, because the previous task already made the service read the injected clock. That is expected — its purpose is to lock the behaviour in. Step 3 is what proves it is a real guard.

- [ ] **Step 3: Prove the guard bites — temporary sabotage check**

Temporarily change line 111 of `backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs` back to:

```csharp
            box.ToPick(DateTime.UtcNow, "System");
```

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxCompletionServiceTests"`

Expected: FAIL — `Failed: 2` (`AllOperationsCompleted_TransitionsBoxToStocked` and `ClockAdvanced_WritesAdvancedTimestamp`), confirming the regression guard the whole change exists for.

Now revert the sabotage — line 111 must read exactly:

```csharp
            box.ToPick(_timeProvider.GetUtcNow().UtcDateTime, "System");
```

Run: `git diff --stat backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs`

Expected: no output (the file is identical to the last commit, so the sabotage is fully reverted).

- [ ] **Step 4: Confirm no test touches the real clock**

Run: `grep -n "DateTime\.UtcNow\|DateTime\.Now" backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs`

Expected: no output, exit status 1.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs
git commit -m "test(logistics): assert TransportBoxCompletionService honours an advanced fake clock"
```

---

