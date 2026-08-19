### task: use-injected-clock-for-transitions

Assert the frozen instant on all three transition kinds first (they fail while the service still reads the wall clock), then switch the three call sites.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs` (assertion blocks of `AllOperationsCompleted_TransitionsBoxToStocked`, `AnyOperationFailed_TransitionsBoxToError`, `NoOperationsForBox_TransitionsToError`)
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs:91,111,131`

- [ ] **Step 1: Write the failing assertions — Stocked transition**

In `CompleteReceivedBoxesAsync_AllOperationsCompleted_TransitionsBoxToStocked`, replace the assertion block that currently starts with `box.State.Should().Be(TransportBoxState.Stocked);` and ends with the `SaveChangesAsync` verification, with:

```csharp
        box.State.Should().Be(TransportBoxState.Stocked);
        box.LastStateChanged.Should().Be(FrozenNow.UtcDateTime);

        box.StateLog.Should().ContainSingle();
        var stateLogEntry = box.StateLog.Single();
        stateLogEntry.State.Should().Be(TransportBoxState.Stocked);
        stateLogEntry.StateDate.Should().Be(FrozenNow.UtcDateTime);
        stateLogEntry.User.Should().Be("System");

        _transportBoxRepositoryMock.Verify(
            x => x.UpdateAsync(box, It.IsAny<CancellationToken>()),
            Times.Once);
        _transportBoxRepositoryMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
```

- [ ] **Step 2: Write the failing assertions — Error from failed operations**

In `CompleteReceivedBoxesAsync_AnyOperationFailed_TransitionsBoxToError`, replace the single assertion line `box.State.Should().Be(TransportBoxState.Error);` with:

```csharp
        box.State.Should().Be(TransportBoxState.Error);
        box.LastStateChanged.Should().Be(FrozenNow.UtcDateTime);

        box.StateLog.Should().ContainSingle();
        var stateLogEntry = box.StateLog.Single();
        stateLogEntry.State.Should().Be(TransportBoxState.Error);
        stateLogEntry.StateDate.Should().Be(FrozenNow.UtcDateTime);
        stateLogEntry.User.Should().Be("System");
```

- [ ] **Step 3: Write the failing assertions — Error from no operations**

In `CompleteReceivedBoxesAsync_NoOperationsForBox_TransitionsToError`, replace the single assertion line `box.State.Should().Be(TransportBoxState.Error);` with:

```csharp
        box.State.Should().Be(TransportBoxState.Error);
        box.LastStateChanged.Should().Be(FrozenNow.UtcDateTime);

        box.StateLog.Should().ContainSingle();
        var stateLogEntry = box.StateLog.Single();
        stateLogEntry.State.Should().Be(TransportBoxState.Error);
        stateLogEntry.StateDate.Should().Be(FrozenNow.UtcDateTime);
        stateLogEntry.User.Should().Be("System");
        stateLogEntry.Description.Should().Be("No stock-up operations found for this box");
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxCompletionServiceTests"`

Expected: FAIL — `Failed: 3, Passed: 4`. Each of the three failures reads roughly `Expected box.LastStateChanged to be <2026-01-15 12:00:00>, but found <…current wall-clock instant…>`, because the service still calls `DateTime.UtcNow`.

- [ ] **Step 5: Replace the three `DateTime.UtcNow` call sites**

In `backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs`, make exactly these three edits inside `ProcessBoxAsync` and nothing else.

Line 91 — replace:

```csharp
            box.Error(DateTime.UtcNow, "System",
                "No stock-up operations found for this box");
```

with:

```csharp
            box.Error(_timeProvider.GetUtcNow().UtcDateTime, "System",
                "No stock-up operations found for this box");
```

Line 111 — replace:

```csharp
            box.ToPick(DateTime.UtcNow, "System");
```

with:

```csharp
            box.ToPick(_timeProvider.GetUtcNow().UtcDateTime, "System");
```

Line 131 — replace:

```csharp
            box.Error(DateTime.UtcNow, "System", errorMessage);
```

with:

```csharp
            box.Error(_timeProvider.GetUtcNow().UtcDateTime, "System", errorMessage);
```

Constraints on these three lines: use `.UtcDateTime`, never `.DateTime`; do not wrap in `DateTime.SpecifyKind(...)`; do not hoist the read into a local at the top of `ProcessBoxAsync` (the branches are mutually exclusive, and the two skip paths at `:138` and `:150` must keep reading the clock zero times). Leave the `"System"` string, both message strings, branch conditions, `UpdateAsync`/`SaveChangesAsync` ordering, returned `BoxProcessingResult` values, and every log statement byte-identical.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxCompletionServiceTests"`

Expected: PASS — `Passed! - Failed: 0, Passed: 7`.

- [ ] **Step 7: Verify no wall-clock read remains in the service**

Run: `grep -n "DateTime\.UtcNow\|DateTime\.Now" backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs`

Expected: no output, exit status 1.

Run: `grep -n "GetUtcNow()\.DateTime\|SpecifyKind" backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs`

Expected: no output, exit status 1. (Any hit here is the `Kind = Unspecified` trap described in the Reference facts — fix it before continuing.)

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs
git commit -m "refactor(logistics): read transition timestamps from injected TimeProvider"
```

---

