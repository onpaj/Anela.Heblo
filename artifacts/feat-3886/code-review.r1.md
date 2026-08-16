## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeSchedulerTests.cs:73-88` — `WaitForMergeAsync` reimplements the exact `Task.WhenAny(signal.Task, Task.Delay(SignalTimeout)); winner.Should().Be(signal.Task, because);` body that `AwaitSignalAsync` already provides. `WaitForMergeAsync` could just call `await AwaitSignalAsync(callbackEntered, because);` followed by `await sut.WaitForCurrentMergeAsync();`, removing the duplicated `Task.WhenAny` block.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeSchedulerTests.cs:213` — `Options_(debounce: Debounce, maxInterval: MaxInterval)` passes the same values the parameterless `Options_()` already defaults to; the explicit arguments are redundant.
