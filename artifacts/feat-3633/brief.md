## Module
Manufacture

## Finding
Three handlers bypass the module's established `TimeProvider` abstraction and call `DateTime.UtcNow` or `DateTime.Now` directly:

1. `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/GetManufactureProtocolHandler.cs:85`
   ```csharp
   GeneratedAt = DateTime.UtcNow,
   ```

2. `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/ResolveManualAction/ResolveManualActionHandler.cs:54`
   ```csharp
   order.ErpDiscardResidueDocumentNumberDate = DateTime.UtcNow;
   ```

3. `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/ResolveManualAction/ResolveManualActionHandler.cs:66`
   ```csharp
   CreatedAt = DateTime.UtcNow,
   ```

4. `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetSemiproductRecipePdf/GetSemiproductRecipePdfHandler.cs:65`
   ```csharp
   PrintedAt = DateTime.Now,   // ← local time, not UTC
   ```

The module otherwise consistently uses injected `TimeProvider` for all time-stamping: `UpdateManufactureOrderStatusHandler`, `ConfirmProductCompletionWorkflow`, all four `DashboardTiles`, and `ConfirmSemiProductManufactureWorkflow` all inject and call `_timeProvider.GetUtcNow()`.

## Why it matters
- **Untestable timestamps**: Handlers that call `DateTime.UtcNow` / `DateTime.Now` directly cannot be given a deterministic clock in tests. Both `ResolveManualActionHandler` and `GetManufactureProtocolHandler` are covered by test files (`ResolveManualActionHandlerTests.cs`, `GetManufactureProtocolHandlerTests.cs`) — those tests cannot verify the exact timestamps written to the database or returned in responses, only that the fields are non-null.
- **Local-time bug in `GetSemiproductRecipePdfHandler`**: `DateTime.Now` uses the server's local timezone, not UTC. If the server ever runs in a non-UTC timezone (e.g. Europe/Prague), `PrintedAt` will be in local time while every other timestamp in the system is UTC. This is a subtle data inconsistency that will manifest as off-by-one-hour (or worse) values in the generated PDF.
- **Inconsistency**: The pattern `TimeProvider.GetUtcNow()` is already the accepted standard in this module. These three outliers make the codebase inconsistent and invite the same mistake in future handlers.

## Suggested fix
Inject `TimeProvider` into each of the three handlers and replace the static calls:

```csharp
// In constructor:
private readonly TimeProvider _timeProvider;

public ResolveManualActionHandler(
    IManufactureOrderRepository repository,
    ICurrentUserService currentUserService,
    TimeProvider timeProvider,         // add
    ILogger logger) { ... }

// Replace DateTime.UtcNow:
order.ErpDiscardResidueDocumentNumberDate = _timeProvider.GetUtcNow().DateTime;
// ...
CreatedAt = _timeProvider.GetUtcNow().DateTime,
```

Apply the same change to `GetManufactureProtocolHandler` (line 85) and `GetSemiproductRecipePdfHandler` (line 65, also switch from `.Now` to `.GetUtcNow().DateTime`).

`TimeProvider` is already registered as a singleton in Program.cs / framework DI — no new registration needed.

---
_Filed by daily arch-review routine on 2026-07-13._
