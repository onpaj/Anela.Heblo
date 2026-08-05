### task: defer-stockup-persist-in-transport-box-receive-and-fix-tests

**Goal**

This is the actual bug fix. Change the single call site inside
`ChangeTransportBoxStateHandler.HandleReceived` to pass `persistImmediately: false`, so that the
`StockUpOperation` inserts it stages are **not** flushed immediately — they ride along with
`Handle`'s existing box-update `SaveChangesAsync` call (unchanged, still at the end of `Handle`),
making the two writes commit as one atomic unit (FR-1). Combined with the idempotency pre-check
added in `add-persist-immediately-and-idempotency-to-stockup-processing-service`, retrying a Receive
whose operations were partially created in a prior interrupted attempt now succeeds instead of
permanently failing on a unique-constraint violation (FR-2).

This task depends on the previous task's new `ILogisticsStockOperationService.CreateOperationAsync`
signature: `Task CreateOperationAsync(string documentNumber, string productCode, int amount, LogisticsStockOperationSource sourceType, int sourceId, CancellationToken cancellationToken = default, bool persistImmediately = true)`.

No control-flow restructuring is needed or wanted: `HandleReceived` already runs, in full, before
`transition.ChangeStateAsync`, `_repository.UpdateAsync(box, cancellationToken)`, and
`_repository.SaveChangesAsync(cancellationToken)` execute later in `Handle` (lines 126, 134-135 of
`ChangeTransportBoxStateHandler.cs`) — do not touch those lines, do not touch `Handle`'s control
flow at all. The only production-code change in this task is the one call site inside
`HandleReceived`.

This task also must fix every Moq `Setup`/`Verify` expression across the test suite that targets
`CreateOperationAsync` and omits the new 7th parameter — because the C# compiler bakes the omitted
parameter's default value (`true`) into the compiled expression tree at the call site, any such
`Setup`/`Verify` will now only match invocations where `persistImmediately == true`, but the
production call being fixed here now always passes `false`. Left unfixed, this breaks
`ChangeTransportBoxStateHandlerTests` (compile succeeds, but `Verify` assertions fail at runtime).

**Files to touch**

1. `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`
2. `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs`

**No change needed (verify only) —** `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs`. Its `CreateOperationAsync` mock `Setup` (around line 193-201) omits the `persistImmediately` argument, and `GiftPackageManufactureService.cs`'s four real call sites (in `CreateManufactureAsync` and `DisassembleGiftPackageAsync`) also omit it — both sides resolve the same compiled-in default of `true`, so the existing setup still matches and this file requires no edits. You must still run its test suite in Step 6 below to confirm this (FR-3: no regression to the shared-service's other consumer).

**Step 1 — write the failing test first**

Open
`backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs`.
Add a new `[Fact]` immediately after the existing
`Handle_InTransitToReceived_DistinctProductCodes_CreatesOneOperationPerProduct` test method (which
currently ends at line 405 with `);` followed by a closing `}` — insert the new test right after
that method's closing `}`, before the next existing test
`Handle_InTransitToReceived_RoundsFractionalAmounts`):

```csharp

    [Fact]
    public async Task Handle_InTransitToReceived_PassesPersistImmediatelyFalse()
    {
        // Arrange — Receive must defer the SaveChangesAsync for StockUpOperation creation so
        // it commits atomically with the box's own state-transition SaveChangesAsync (FR-1):
        // both writes share the same ApplicationDbContext instance and must be flushed together.
        var box = CreateTestBoxWithItems(TransportBoxState.InTransit);
        SetupReceivedTransitionMocks(box);

        var request = new ChangeTransportBoxStateRequest { BoxId = 1, NewState = TransportBoxState.Received };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>(),
                false),
            Times.Once);
    }
```

This test will fail to compile right now, because `ILogisticsStockOperationService.CreateOperationAsync`
already has the 7-parameter signature (from the previous task) — so it compiles fine — but at
runtime it will **fail** the `Times.Once` assertion, because current production code (before Step 3
below) calls `CreateOperationAsync` without naming `persistImmediately`, which resolves to `true`,
not `false`.

**Step 2 — run this one test and confirm it fails**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Handle_InTransitToReceived_PassesPersistImmediatelyFalse"
```

Expected: the test runs and fails (Moq `Verify` throws `MockException: ... Expected invocation on
the mock at least once, but was never performed` or similar, because no invocation matching
`persistImmediately == false` occurred).

**Step 3 — implement the production fix**

In
`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`,
inside the `HandleReceived` method, replace this call (currently):

```csharp
            await _stockOperationService.CreateOperationAsync(
                documentNumber,
                group.ProductCode,
                group.Amount,
                LogisticsStockOperationSource.TransportBox,
                box.Id,
                cancellationToken);
```

with:

```csharp
            await _stockOperationService.CreateOperationAsync(
                documentNumber,
                group.ProductCode,
                group.Amount,
                LogisticsStockOperationSource.TransportBox,
                box.Id,
                cancellationToken,
                persistImmediately: false);
```

Do not change anything else in this file — not `Handle`, not the rest of `HandleReceived`, not any
other handler method.

**Step 4 — run the new test again and confirm it passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Handle_InTransitToReceived_PassesPersistImmediatelyFalse"
```

**Step 5 — fix the now-broken existing Moq expressions in `ChangeTransportBoxStateHandlerTests.cs`**

Run the full test class to see the breakage first:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ChangeTransportBoxStateHandlerTests"
```

Expected at this point: several existing tests fail (their `Verify` calls implicitly expect
`persistImmediately == true` because they omit the parameter, but the real call now always passes
`false`). Fix each occurrence below, in this same file.

**(a)** The constructor's shared `_stockUpProcessingServiceMock` setup. Replace:

```csharp
        _stockUpProcessingServiceMock
            .Setup(x => x.CreateOperationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
```

with:

```csharp
        _stockUpProcessingServiceMock
            .Setup(x => x.CreateOperationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .Returns(Task.CompletedTask);
```

**(b)** The generic "no calls happened" assertion. This exact block of code appears **twice**
verbatim in the file — once in `Handle_OpenedToQuarantine_DoesNotCreateStockUpOperations` and once
in `Handle_OpenedToReserve_NullLocation_ReturnsTransportBoxStateChangeError`. Replace **both**
occurrences of:

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
```

with:

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>(),
                It.IsAny<bool>()),
            Times.Never);
```

(`Times.Never` assertions are unaffected either way by the missing 7th parameter since no invocation
occurs in those two tests at all, but keep them consistent with the rest of the file so a future
signature change doesn't silently start passing them for the wrong reason.)

**(c)** The generic "exactly one call happened" assertion (all-`It.IsAny`, `Times.Once`). This exact
block appears **three times** verbatim — in `Handle_QuarantineToReceived_CreatesStockUpOperations`,
and twice more as the second `Verify` inside
`Handle_InTransitToReceived_AggregatesDuplicateProductCodes_IntoSingleStockUpOperation` and
`Handle_ReserveToReceived_AggregatesDuplicateProductCodes_IntoSingleStockUpOperation`. Replace **all
three** occurrences of:

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
```

with:

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>(),
                It.IsAny<bool>()),
            Times.Once);
```

**(d)** The literal-argument assertion for the aggregated `"BOX-000001-P-001"`/amount-8 case. This
exact block appears **twice** verbatim — as the first `Verify` in
`Handle_InTransitToReceived_AggregatesDuplicateProductCodes_IntoSingleStockUpOperation` and as the
first `Verify` in `Handle_ReserveToReceived_AggregatesDuplicateProductCodes_IntoSingleStockUpOperation`.
Replace **both** occurrences of:

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                "BOX-000001-P-001",
                "P-001",
                8,
                LogisticsStockOperationSource.TransportBox,
                1,
                It.IsAny<CancellationToken>()),
            Times.Once);
```

with:

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                "BOX-000001-P-001",
                "P-001",
                8,
                LogisticsStockOperationSource.TransportBox,
                1,
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()),
            Times.Once);
```

**(e)** In `Handle_InTransitToReceived_DistinctProductCodes_CreatesOneOperationPerProduct`, there are
three `Verify` calls. Replace the first one (P-001, amount 2):

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                "BOX-000001-P-001", "P-001", 2,
                LogisticsStockOperationSource.TransportBox, 1, It.IsAny<CancellationToken>()),
            Times.Once);
```

with:

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                "BOX-000001-P-001", "P-001", 2,
                LogisticsStockOperationSource.TransportBox, 1, It.IsAny<CancellationToken>(),
                It.IsAny<bool>()),
            Times.Once);
```

Replace the second one (P-002, amount 4):

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                "BOX-000001-P-002", "P-002", 4,
                LogisticsStockOperationSource.TransportBox, 1, It.IsAny<CancellationToken>()),
            Times.Once);
```

with:

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                "BOX-000001-P-002", "P-002", 4,
                LogisticsStockOperationSource.TransportBox, 1, It.IsAny<CancellationToken>(),
                It.IsAny<bool>()),
            Times.Once);
```

Replace the third one (generic, `Times.Exactly(2)`):

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
```

with:

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>(),
                It.IsAny<bool>()),
            Times.Exactly(2));
```

**(f)** In `Handle_InTransitToReceived_RoundsFractionalAmounts`, replace:

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                It.IsAny<string>(), "P-001", 3,
                It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
```

with:

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                It.IsAny<string>(), "P-001", 3,
                It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>(),
                It.IsAny<bool>()),
            Times.Once);
```

**Step 6 — run the full test class and the GiftPackageManufactureServiceTests class**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ChangeTransportBoxStateHandlerTests"
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufactureServiceTests"
```

All tests in both classes must pass. `GiftPackageManufactureServiceTests` requires no code changes
(see "No change needed" note above) — this run is to confirm FR-3 (no regression to the shared
service's other consumer).

**Step 7 — run the full backend test suite, build, and format check**

```bash
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
dotnet test Anela.Heblo.sln
```

All must succeed. `dotnet test Anela.Heblo.sln` (not just the filtered subsets from earlier steps)
must show zero failures — this catches any other test file in the solution with a
`CreateOperationAsync` expectation that wasn't in the file list enumerated above.

**Step 8 — commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs
git commit -m "Defer StockUpOperation persistence in TransportBox Receive so it commits atomically with the box state transition"
```

**Acceptance criteria**

- `dotnet build Anela.Heblo.sln` succeeds with no errors.
- `dotnet format Anela.Heblo.sln --verify-no-changes` reports no changes.
- `dotnet test Anela.Heblo.sln` passes with zero failures across the whole solution.
- `ChangeTransportBoxStateHandler.HandleReceived`'s call to `_stockOperationService.CreateOperationAsync`
  passes `persistImmediately: false` as its 7th argument; no other line in
  `ChangeTransportBoxStateHandler.cs` is changed.
- `ChangeTransportBoxStateHandlerTests.cs` has a passing
  `Handle_InTransitToReceived_PassesPersistImmediatelyFalse` test that asserts the call includes a
  literal `false` for `persistImmediately`, and every other pre-existing `Setup`/`Verify` targeting
  `CreateOperationAsync` in that file includes an explicit 7th argument (`It.IsAny<bool>()`).
- `GiftPackageManufactureServiceTests.cs` is unmodified and its tests still pass unchanged (FR-3).

---

## Self-review notes (writing-plans skill, Self-Review section)

- **Spec coverage:** FR-1 (atomic persistence) is satisfied by deferring `SaveChangesAsync` via
  `persistImmediately: false` in Task 3, riding on `Handle`'s existing single `SaveChangesAsync`
  call (Task 3, Step 3) — no explicit transaction is introduced anywhere, consistent with the
  CI-enforced `scripts/check-no-managed-tx.sh` constraint stated explicitly in Task 3's goal and the
  plan preamble. FR-2 (idempotent retry) is satisfied by the `GetByDocumentNumberAsync` pre-check
  added in Task 1. FR-3 (no regression to `GiftPackageManufactureService`) is satisfied by the
  `persistImmediately = true` default at every layer (verified explicitly, with no code change
  required, in Task 3's "No change needed" note and Step 6). FR-4 (error surfacing) requires no
  dedicated code change per the spec ("no new dedicated exception type or error code is required")
  — it is a natural consequence of FR-2's skip-instead-of-throw behavior, already covered by Task 1.
  NFR-1 (performance: at most one extra existence-check query per product) is satisfied — Task 1
  adds exactly one `GetByDocumentNumberAsync` call per `CreateOperationAsync` invocation, no batching
  required per the spec ("not mandatory for this fix"). NFR-3 (unique index remains, no migration) —
  no schema-touching file appears anywhere in this plan.
- **No placeholders:** every task gives exact file paths, exact current code (verified against the
  live repository files read during planning, not just the arch-review/design excerpts), exact new
  code, and exact commands with what to expect. No "TBD", no "similar to Task N" shortcuts — every
  duplicated Moq block in Task 3 Step 5 is spelled out in full at each occurrence.
- **Type/signature consistency:** `IStockUpProcessingService.CreateOperationAsync` (Task 1) →
  `ILogisticsStockOperationService.CreateOperationAsync` (Task 2, pass-through, `sourceType` typed
  as `LogisticsStockOperationSource` instead of `StockUpSourceType`, mapped by
  `LogisticsStockOperationAdapter.MapSourceType`, unchanged from today) → the one call site in
  `ChangeTransportBoxStateHandler.HandleReceived` (Task 3) — all three signatures place
  `bool persistImmediately = true` immediately after the `CancellationToken` parameter, consistently
  named, consistently defaulted. Verified against the actual current file contents on disk, not
  assumed from the design doc.
