### task: wire-handler-desired-state-name

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderHandler.cs:57-66`
- Modify (test): `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderHandlerTests.cs`

**Depends on:** `add-desired-state-name-option` (needs `PrintPickingListOptions.DesiredStateName` to exist).

- [ ] **Step 1: Update the existing test to assert on the configured name, not the hardcoded literal, and watch it fail**

Open `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderHandlerTests.cs`. The `Handle_NonDefaultDesiredStateId_ChecksConfiguredValueNotHardcoded26` test currently reads:

```csharp
    [Fact]
    public async Task Handle_NonDefaultDesiredStateId_ChecksConfiguredValueNotHardcoded26()
    {
        var handler = new PrintExpeditionOrderHandler(
            _service.Object,
            _client.Object,
            Options.Create(new PrintPickingListOptions { DesiredStateId = 99 }),
            new Mock<ILogger<PrintExpeditionOrderHandler>>().Object);

        // Status 99 (the configured DesiredStateId) must now be rejected as invalid state.
        _client.Setup(c => c.GetOrderStatusIdAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(99);

        var result = await handler.Handle(
            new PrintExpeditionOrderRequest { OrderCode = "0001234" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ExpeditionOrderInvalidState);
        result.Params!["currentStatusName"].Should().Be("Balí se");
        _service.Verify(s => s.PrintPickingListAsync(It.IsAny<ExpeditionPickingRequest>(), It.IsAny<IList<string>?>(), It.IsAny<CancellationToken>()), Times.Never);

        // Status 26 (the old hardcoded value) must no longer be special-cased and should proceed to print.
        _client.Setup(c => c.GetOrderStatusIdAsync("0005678", It.IsAny<CancellationToken>()))
            .ReturnsAsync(26);
        _service.Setup(s => s.PrintPickingListAsync(It.IsAny<ExpeditionPickingRequest>(), It.IsAny<IList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExpeditionPickingResult { ExportedFiles = new List<string>(), TotalCount = 1 });

        var secondResult = await handler.Handle(
            new PrintExpeditionOrderRequest { OrderCode = "0005678" }, CancellationToken.None);

        secondResult.Success.Should().BeTrue();
        _service.Verify(s => s.PrintPickingListAsync(It.IsAny<ExpeditionPickingRequest>(), It.IsAny<IList<string>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
```

Change it to configure a non-default `DesiredStateName` too, and assert against that configured value instead of the hardcoded literal — this is what actually proves the name, like the ID, is now sourced from configuration:

```csharp
    [Fact]
    public async Task Handle_NonDefaultDesiredStateId_ChecksConfiguredValueNotHardcoded26()
    {
        var handler = new PrintExpeditionOrderHandler(
            _service.Object,
            _client.Object,
            Options.Create(new PrintPickingListOptions { DesiredStateId = 99, DesiredStateName = "Custom State" }),
            new Mock<ILogger<PrintExpeditionOrderHandler>>().Object);

        // Status 99 (the configured DesiredStateId) must now be rejected as invalid state,
        // reported with the configured DesiredStateName — not the old hardcoded "Balí se".
        _client.Setup(c => c.GetOrderStatusIdAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(99);

        var result = await handler.Handle(
            new PrintExpeditionOrderRequest { OrderCode = "0001234" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ExpeditionOrderInvalidState);
        result.Params!["currentStatusName"].Should().Be("Custom State");
        _service.Verify(s => s.PrintPickingListAsync(It.IsAny<ExpeditionPickingRequest>(), It.IsAny<IList<string>?>(), It.IsAny<CancellationToken>()), Times.Never);

        // Status 26 (the old hardcoded value) must no longer be special-cased and should proceed to print.
        _client.Setup(c => c.GetOrderStatusIdAsync("0005678", It.IsAny<CancellationToken>()))
            .ReturnsAsync(26);
        _service.Setup(s => s.PrintPickingListAsync(It.IsAny<ExpeditionPickingRequest>(), It.IsAny<IList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExpeditionPickingResult { ExportedFiles = new List<string>(), TotalCount = 1 });

        var secondResult = await handler.Handle(
            new PrintExpeditionOrderRequest { OrderCode = "0005678" }, CancellationToken.None);

        secondResult.Success.Should().BeTrue();
        _service.Verify(s => s.PrintPickingListAsync(It.IsAny<ExpeditionPickingRequest>(), It.IsAny<IList<string>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
```

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PrintExpeditionOrderHandlerTests.Handle_NonDefaultDesiredStateId_ChecksConfiguredValueNotHardcoded26"`
Expected: **FAIL** — `result.Params!["currentStatusName"]` is still `"Balí se"` (the handler hasn't changed yet), not `"Custom State"`.

- [ ] **Step 2: Implement the fix in the handler**

Open `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderHandler.cs`. The desired-state branch (lines 57-66) currently reads:

```csharp
        if (currentStatusId == _options.Value.DesiredStateId)
        {
            return new PrintExpeditionOrderResponse(
                ErrorCodes.ExpeditionOrderInvalidState,
                new Dictionary<string, string>
                {
                    { "orderCode", request.OrderCode },
                    { "currentStatusName", "Balí se" },
                });
        }
```

Change the hardcoded literal to read from the options:

```csharp
        if (currentStatusId == _options.Value.DesiredStateId)
        {
            return new PrintExpeditionOrderResponse(
                ErrorCodes.ExpeditionOrderInvalidState,
                new Dictionary<string, string>
                {
                    { "orderCode", request.OrderCode },
                    { "currentStatusName", _options.Value.DesiredStateName },
                });
        }
```

- [ ] **Step 3: Run the updated test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PrintExpeditionOrderHandlerTests.Handle_NonDefaultDesiredStateId_ChecksConfiguredValueNotHardcoded26"`
Expected: **PASS**.

- [ ] **Step 4: Run the full handler test class to confirm no regressions**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PrintExpeditionOrderHandlerTests"`
Expected: **PASS** — all tests green, including `Handle_OrderInNonPrintableState_ReturnsInvalidStateError` (the `[InlineData(26, "Balí se")]` case still passes because default options still yield `DesiredStateName = "Balí se"`).

- [ ] **Step 5: Run the full backend test suite**

Run: `cd backend && dotnet test`
Expected: **PASS** — no unrelated regressions.

- [ ] **Step 6: Run `dotnet format` and confirm clean**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: no formatting violations. If violations are reported, run `dotnet format` (without `--verify-no-changes`) and re-stage the affected files.

- [ ] **Step 7: Commit**

```bash
cd backend
git add src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderHandler.cs test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderHandlerTests.cs
git commit -m "fix(expedition-list): use configured DesiredStateName instead of hardcoded 'Balí se'"
```

---

## Self-Review

**Spec coverage:**
- FR-1 (add `DesiredStateName` to `PrintPickingListOptions`, default `"Balí se"`, plus `appsettings.json` symmetry entry) → `add-desired-state-name-option`, Steps 1-2.
- FR-2 (handler reads `_options.Value.DesiredStateName`; existing test updated to assert the configured name; `[InlineData(26, "Balí se")]` case and all other existing tests continue to pass) → `wire-handler-desired-state-name`, Steps 1-4.
- NFR-1 (backward compatibility: unchanged default behavior) → guaranteed by the default value chosen in Step 1 of the first task, and directly verified by Step 4 of the second task (the `InlineData(26, "Balí se")` case).
- NFR-2 (no new external dependencies/migrations) → plan introduces zero new packages, services, or migrations.
- Data Model / API / Interface Design sections (no changes) → confirmed no task touches `PrintExpeditionOrderResponse`, contracts, or the frontend.

**Placeholder scan:** No "TBD"/"handle appropriately"/unshown code — every step shows the exact before/after code and exact commands with expected output.

**Type consistency:** `DesiredStateName` is `string` everywhere it's introduced (option property, appsettings.json string value, test's `"Custom State"` literal, handler's dictionary value assignment) — no signature drift across tasks.
