# Specification: Unit Tests for BlockOrderProcessingHandler

## Summary

`BlockOrderProcessingHandler` contains the sole safety-critical branch in the ShoptetOrders module — a source-state guard that prevents blocking orders already in terminal states. This specification defines the unit-test suite that must be added to protect that guard and its surrounding logic from silent regression.

## Background

The daily architecture review (2026-06-23) found that `BlockOrderProcessingHandler.Handle` has no handler-level unit tests. The existing test files (`ShoptetOrderClientTests.cs`, `ShoptetOrderClient_SetAdditionalFieldTests.cs`) cover only the Shoptet HTTP adapter. The handler's source-state guard is safety-critical: a misconfigured `AllowedBlockSourceStateIds` (empty array, or a wrong status ID) would silently allow — or silently block — orders that should not be touched. Integration tests exist but are environment-gated and do not run in PR CI, leaving handler logic fully uncovered during development.

Investigation of the codebase revealed that a file at `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/BlockOrderProcessingHandlerTests.cs` already exists and covers most of the scenarios called for in the brief. This specification therefore documents the **complete intended test coverage** so it can be verified against the existing file and gaps can be filled.

## Functional Requirements

### FR-1: Happy path — status in allowed list, empty remark

When `GetOrderStatusIdAsync` returns a status ID present in `AllowedBlockSourceStateIds`, the handler must:
- Call `UpdateStatusAsync` with `request.OrderCode` and `settings.BlockedStatusId`.
- Call `GetEshopRemarkAsync`, then `UpdateEshopRemarkAsync` with exactly `request.Note` (no leading newline) when the existing remark is empty or whitespace.
- Return a response where `Success = true` and `ErrorCode` is null.

**Acceptance criteria:**
- `UpdateStatusAsync` is called exactly once with the correct `orderCode` and `BlockedStatusId`.
- `UpdateEshopRemarkAsync` is called exactly once; the written remark equals `request.Note`.
- `response.Success` is `true`.

### FR-2: Happy path — status in allowed list, existing remark appended

When the order already has a non-empty eshop remark, the handler must append the note with a `\n` separator.

**Acceptance criteria:**
- `UpdateEshopRemarkAsync` is called with `"{existingRemark}\n{request.Note}"`.
- `response.Success` is `true`.

### FR-3: Invalid source state — status not in allowed list

When `GetOrderStatusIdAsync` returns a status ID **not** present in `AllowedBlockSourceStateIds`:
- The handler must return a failure response with `ErrorCode = ShoptetOrderInvalidSourceState`.
- `UpdateStatusAsync` must **not** be called.
- Remark methods must **not** be called.

**Acceptance criteria:**
- `response.Success` is `false`.
- `response.ErrorCode` equals `ErrorCodes.ShoptetOrderInvalidSourceState`.
- `UpdateStatusAsync` is never invoked (verified via mock).
- `GetEshopRemarkAsync` and `UpdateEshopRemarkAsync` are never invoked.

### FR-4: Status API throws — internal error, no remark side-effect

When `GetOrderStatusIdAsync` throws any exception:
- The handler must return `ErrorCode = InternalServerError`.
- `UpdateStatusAsync` must not be called.
- Remark methods must not be called.
- The exception must be logged at `LogLevel.Error`.

**Acceptance criteria:**
- `response.Success` is `false`.
- `response.ErrorCode` equals `ErrorCodes.InternalServerError`.
- No call to `UpdateStatusAsync`, `GetEshopRemarkAsync`, or `UpdateEshopRemarkAsync`.

### FR-5: Status update throws — internal error, no remark side-effect

When `UpdateStatusAsync` throws:
- The handler must return `ErrorCode = InternalServerError`.
- Remark methods must not be called.

**Acceptance criteria:**
- `response.Success` is `false`.
- `response.ErrorCode` equals `ErrorCodes.InternalServerError`.
- `GetEshopRemarkAsync` and `UpdateEshopRemarkAsync` are never invoked.

### FR-6: Remark fetch throws non-cancellation exception — success, warning logged

When `GetEshopRemarkAsync` throws a non-`OperationCanceledException`:
- The handler must still return success (block 1 completed).
- The exception must be logged at `LogLevel.Warning`; the log message must contain `request.OrderCode`.
- `UpdateEshopRemarkAsync` must not be called.

**Acceptance criteria:**
- `response.Success` is `true`.
- Logger received exactly one `LogLevel.Warning` call whose message includes the order code.
- `UpdateEshopRemarkAsync` is never invoked.

### FR-7: Remark update throws non-cancellation exception — success, warning logged

When `UpdateEshopRemarkAsync` throws a non-`OperationCanceledException`:
- The handler must still return success.
- The exception must be logged at `LogLevel.Warning`.

**Acceptance criteria:**
- `response.Success` is `true`.
- Logger received at least one `LogLevel.Warning` call.

### FR-8: OperationCanceledException propagates from remark block

When `GetEshopRemarkAsync` (or `UpdateEshopRemarkAsync`) throws `OperationCanceledException`, the exception must **not** be swallowed — it propagates to the caller.

**Acceptance criteria:**
- `Assert.ThrowsAsync<OperationCanceledException>` (or FluentAssertions equivalent) succeeds.
- No response object is returned.

## Non-Functional Requirements

### NFR-1: Test isolation

Each test must be fully independent. Shared state (mock setup, settings) must be reset per test. The test class may use a constructor-level fixture with a `CreateHandler()` factory method; mock `Setup` calls are per-test.

### NFR-2: Framework alignment

Tests must use xUnit + FluentAssertions + Moq, consistent with the existing test files in the project (`ShoptetOrderClient_SetAdditionalFieldTests.cs`).

### NFR-3: No I/O

All tests are pure unit tests. `IEshopOrderClient` and `ILogger<BlockOrderProcessingHandler>` are mocked via Moq. No network calls, no database, no file system access.

### NFR-4: CI compatibility

The test class must not carry any `[Trait("Category", "Integration")]` or environment-variable guards. It must run unconditionally in `dotnet test`.

## Data Model

No new persistence entities. The test operates on the following in-memory values:

| Symbol | Type | Role |
|---|---|---|
| `BlockOrderProcessingRequest` | class | Handler input: `OrderCode` (string), `Note` (string) |
| `BlockOrderProcessingResponse` | class | Handler output: `Success` (bool), `ErrorCode` (ErrorCodes?), `Params` (dict?) |
| `ShoptetOrdersSettings` | class | `AllowedBlockSourceStateIds` (int[]), `BlockedStatusId` (int) |
| `IEshopOrderClient` | interface | Dependency mock: `GetOrderStatusIdAsync`, `UpdateStatusAsync`, `GetEshopRemarkAsync`, `UpdateEshopRemarkAsync` |

## API / Interface Design

This feature adds no public API surface. The file being created is:

```
backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/BlockOrderProcessingHandlerTests.cs
```

Test class skeleton:

```csharp
public class BlockOrderProcessingHandlerTests
{
    private readonly Mock<IEshopOrderClient> _clientMock = new();
    private readonly Mock<ILogger<BlockOrderProcessingHandler>> _loggerMock = new();
    private readonly ShoptetOrdersSettings _settings = new()
    {
        AllowedBlockSourceStateIds = [26, -2],
        BlockedStatusId = 99
    };

    private BlockOrderProcessingHandler CreateHandler() =>
        new(_clientMock.Object,
            Options.Create(_settings),
            _loggerMock.Object);
}
```

Each test method calls `CreateHandler()`, sets up mocks for the specific scenario, invokes `handler.Handle(request, CancellationToken.None)`, and asserts on the response and mock invocations.

## Dependencies

- `Anela.Heblo.Application` — contains `BlockOrderProcessingHandler`, `BlockOrderProcessingRequest`, `BlockOrderProcessingResponse`, `ShoptetOrdersSettings`, `IEshopOrderClient`, `ErrorCodes`.
- `Microsoft.Extensions.Options` — `IOptions<T>` / `Options.Create(...)`.
- `Microsoft.Extensions.Logging` — `ILogger<T>`, `NullLogger`.
- `xUnit` — test runner (already referenced in `Anela.Heblo.Tests.csproj`).
- `FluentAssertions` — assertion library (already referenced).
- `Moq` — mocking framework (already referenced).

## Out of Scope

- Integration tests against a live or sandboxed Shoptet store.
- Tests for `BlockOrderRequest` (HTTP contract), the MVC controller, or the Shoptet HTTP adapter.
- Tests for the remark read-modify-write race condition (two concurrent calls).
- Tests for `OperationCanceledException` thrown from the critical block (block 1); this is not specially handled and would surface as `InternalServerError` — acceptable but not specified here.
- Any changes to production code.

## Open Questions

None.

## Status: COMPLETE
