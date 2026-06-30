### task: delete-dead-files-and-strip-interface

**Goal:** Remove three dead methods from `IEshopOrderClient` and their implementations in `ShoptetOrderClient`. Delete three now-unused files (`EshopOrderInfo.cs`, `ShoptetEshopResponse.cs`, `UpdateNotesRequest.cs`). Remove the `Times.Never` test assertion that references the deleted `SetInternalNoteAsync`. Verify the build, format, and tests all pass.

**Context:**
- This is a pure dead-code removal. Zero production behavior changes.
- The project is a .NET 8 Clean Architecture monorepo.
- Run all commands from the worktree root: `/home/user/worktrees/feature-3259-Arch-Review-Shoptetorders-Ieshoporderclient-Carrie`

**Files to delete:**
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/EshopOrderInfo.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/ShoptetEshopResponse.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/UpdateNotesRequest.cs`

**Files to modify:**
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs`
  — Remove declaration: `Task SetInternalNoteAsync(string orderCode, string note, CancellationToken ct = default);`
  — Remove declaration + XML doc: `Task<List<EshopOrderInfo>> GetRecentOrdersByEmailAsync(string email, int count, CancellationToken ct = default);`
  — Remove declaration + XML doc: `Task<Dictionary<int, string>> GetOrderStatusNamesAsync(CancellationToken ct = default);`

- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs`
  — Remove method body: `SetInternalNoteAsync` (uses `CreateOrderRemarkRequest`, `CreateOrderRemarkData`)
  — Remove NOTE comment + method body: `GetRecentOrdersByEmailAsync` (uses `EshopOrderInfo`, `MapToOrderInfo`)
  — Remove method body: `GetOrderStatusNamesAsync` (uses `ShoptetEshopResponse`)
  — Remove private helper: `MapToOrderInfo`

- `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/BlockOrderProcessingHandlerTests.cs`
  — Remove 3-line `clientMock.Verify` block that asserts `SetInternalNoteAsync` is never called

**Implementation steps:**

1. Delete the three files:
```bash
rm backend/src/Anela.Heblo.Application/Features/ShoptetOrders/EshopOrderInfo.cs
rm backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/ShoptetEshopResponse.cs
rm backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/UpdateNotesRequest.cs
```

2. Edit `IEshopOrderClient.cs` — remove the three dead declarations.

3. Edit `ShoptetOrderClient.cs` — remove blocks for `SetInternalNoteAsync`, `GetRecentOrdersByEmailAsync` (plus its NOTE comment), `GetOrderStatusNamesAsync`, and the `MapToOrderInfo` private helper.

4. Edit `BlockOrderProcessingHandlerTests.cs` — remove the `clientMock.Verify` block for `SetInternalNoteAsync` (the 3 lines containing `SetInternalNoteAsync`, `Times.Never`).

5. Build, format, test:
```bash
dotnet build backend/
dotnet format backend/ --verify-no-changes
dotnet test backend/
```

6. Commit:
```bash
git add -A
git commit -m "refactor: remove dead IEshopOrderClient methods and associated types

Removes SetInternalNoteAsync, GetRecentOrdersByEmailAsync, and
GetOrderStatusNamesAsync from IEshopOrderClient and ShoptetOrderClient —
none have callers in production source. Deletes EshopOrderInfo,
ShoptetEshopResponse, and UpdateNotesRequest (CreateOrderRemarkRequest)
which existed solely to support these methods. Removes the Times.Never
assertion for SetInternalNoteAsync from BlockOrderProcessingHandlerTests."
```

**Acceptance criteria:**
- `IEshopOrderClient.cs` no longer has `SetInternalNoteAsync`, `GetRecentOrdersByEmailAsync`, `GetOrderStatusNamesAsync`
- `EshopOrderInfo.cs`, `ShoptetEshopResponse.cs`, `UpdateNotesRequest.cs` do not exist
- `dotnet build backend/` exits 0
- `dotnet format backend/ --verify-no-changes` exits 0
- `dotnet test backend/` all pass
