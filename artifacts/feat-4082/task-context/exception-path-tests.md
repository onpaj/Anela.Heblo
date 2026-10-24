### task: exception-path-tests
Write failing tests first in `McpBadRequestMiddlewareTests.cs` (reuse the `CreateContext` helper and the Moq `ILogger<McpBadRequestMiddleware>` pattern used by the `PostBadRequest_*` tests):
1. `PostThrows_LogsOneWarning5933WithExceptionType_AndRethrowsSameInstance` - `next` throws `new ArgumentException("x")`; assert `Assert.ThrowsAsync<ArgumentException>` returns the same instance, exactly one Warning with `EventId.Id == 5933 && Name == "McpRequestException"`, message contains `ExceptionType=ArgumentException`, `McpSessionIdPresent=` and no 5932 log.
2. `PostThrows_WithSessionHeader_LogsEightCharPrefixOnly` - header `Mcp-Session-Id: 1234567890abcdef`; log contains `12345678***` style prefix (match `McpTelemetryHelpers.TruncateSessionId`) and not the full value; also set an `Authorization: Bearer secret` header and assert the log never contains `secret`.
3. `GetThrows_WithEventStreamAccept_LogsWarning5933AndRethrows`.
4. `PostThrows_OperationCanceledWithAbortedRequest_DoesNotLog` - set `context.RequestAborted` to a cancelled token and throw `OperationCanceledException`; assert no Warning logged and exception still propagates.
5. `PostThrowsOperationCanceled_WithoutAbort_Logs` (optional sanity).
Run `dotnet test backend/test/Anela.Heblo.Tests --filter McpBadRequestMiddlewareTests` and confirm the new tests fail (no 5933 emitted). Commit tests.

