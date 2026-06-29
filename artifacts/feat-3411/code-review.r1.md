## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs` (stale-warning tests) — `_mockStateRepository.UpsertAsync` is not explicitly set up; relies on Moq default `Task.CompletedTask`. Works correctly; would be clearer with explicit setup if MockBehavior.Strict is ever adopted.
- `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs` (`Handle_RecordsImportServiceError`) — `BankStatementData { ItemCount = 2 }` was a confusing red herring since `ItemCount` is not passed to `ImportStatementAsync`. Fixed to `ItemCount = 1` for consistency with other tests.
