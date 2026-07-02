# Design: Bank ImportBankStatementHandler coverage gaps

## Test file
`backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs`

All four new tests are added to the existing `ImportBankStatementHandlerTests` class.

## Test designs

### Test 1: Handle_LogsStaleWarning_WhenWatermarkIsStale
```
Arrange:
  - existingState.RecordSuccess(DateTime.UtcNow.AddDays(-10), ...)
  - _mockStateRepository returns existingState
  - _mockBankClient.GetStatementsAsync returns []
  - Default _handler (StaleWarningDays = 3)

Act: await _handler.Handle(request, CancellationToken.None)

Assert:
  - _mockLogger.Verify(Log(LogLevel.Warning, ...), Times.Once)
```

### Test 2: Handle_DoesNotLogWarning_WhenWatermarkIsFresh
```
Arrange:
  - existingState.RecordSuccess(DateTime.UtcNow.AddDays(-1), ...)
  - _mockStateRepository returns existingState
  - _mockBankClient.GetStatementsAsync returns []

Act: await _handler.Handle(request, CancellationToken.None)

Assert:
  - _mockLogger.Verify(Log(LogLevel.Warning, ...), Times.Never)
```

### Test 3: Handle_FallsBackToInsert_WhenRetryStatementNotFoundInDb
```
Arrange:
  - GetStatementsAsync returns [{ StatementId = "RETRY" }]
  - GetExistingResultsByTransferIdsAsync returns { "RETRY" -> ProcessingError }
  - GetStatementAsync("RETRY") returns { Data = "abo", ItemCount = 1 }
  - ImportStatementAsync(1, "abo") returns Result.Success(true)
  - GetByTransferIdAsync("RETRY") returns null  <-- key: null despite isRetry=true
  - AddAsync returns the created entity
  - Mapper maps to DTO

Act: await _handler.Handle(request, CancellationToken.None)

Assert:
  - AddAsync called once
  - UpdateAsync never called
  - response.SuccessCount == 1
```

### Test 4: Handle_RecordsImportServiceError_WhenImportServiceReturnsFailed
```
Arrange:
  - GetStatementsAsync returns [{ StatementId = "ERR" }]
  - GetExistingResultsByTransferIdsAsync returns {}
  - GetStatementAsync("ERR") returns { Data = "abo", ItemCount = 2 }
  - ImportStatementAsync(1, "abo") returns Result<bool>.Failure("import-error")
  - AddAsync captures the BankStatementImport and returns it
  - Mapper maps to DTO with ImportResult = "import-error"

Act: await _handler.Handle(request, CancellationToken.None)

Assert:
  - AddAsync called with entity where ImportResult == "import-error"
  - response.ErrorCount == 1, SuccessCount == 0
```

## Logger assertion pattern
```csharp
_mockLogger.Verify(
    x => x.Log(
        LogLevel.Warning,
        It.IsAny<EventId>(),
        It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("stale")),
        It.IsAny<Exception?>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
    Times.Once);
```
