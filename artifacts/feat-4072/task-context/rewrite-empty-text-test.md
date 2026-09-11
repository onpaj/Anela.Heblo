### task: rewrite-empty-text-test

**Goal**: Rewrite `Handle_ReturnsFailure_WhenEnabledWithEmptyText` to assert success/save, confirm red against the current handler, then delete the empty-text `if`-block.

**Step 1 — Rewrite the test.**

File: `backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingHandlerTests.cs`

Use the Edit tool. Replace:

```csharp
    [Fact]
    public async Task Handle_ReturnsFailure_WhenEnabledWithEmptyText()
    {
        var command = new SetGiftSettingCommand
        {
            IsEnabled = true,
            ThresholdCzk = 1500m,
            Text = string.Empty,
        };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        _repositoryMock.Verify(r => r.SaveAsync(It.IsAny<GiftSetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

With:

```csharp
    [Fact]
    public async Task Handle_SavesSetting_WhenEnabledWithEmptyText()
    {
        // Empty Text while enabled is rejected end-to-end by ValidationBehavior +
        // SetGiftSettingValidator before the handler ever runs (see SetGiftSettingValidatorTests).
        // The handler itself no longer re-validates this, so calling it directly succeeds.
        var command = new SetGiftSettingCommand
        {
            IsEnabled = true,
            ThresholdCzk = 1500m,
            Text = string.Empty,
        };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        _repositoryMock.Verify(r => r.SaveAsync(It.Is<GiftSetting>(g => g.ModifiedBy == "user-1"), It.IsAny<CancellationToken>()), Times.Once);
    }
```

**Step 2 — Run the test and confirm it fails (red).**

```bash
cd /home/user/worktrees/feature-4072-Arch-Review-Logistics-Setgiftsettinghandler-Duplic
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SetGiftSettingHandlerTests.Handle_SavesSetting_WhenEnabledWithEmptyText"
```

Expected output: **1 test run, 1 failed** (`result.Success` expected `True`, found `False` — the handler's `if (string.IsNullOrEmpty(command.Text))` block still returns `Success = false`).

**Step 3 — Delete the empty-text `if`-block from the handler.**

File: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingHandler.cs`

At this point (after `rewrite-zero-threshold-test`), the relevant section reads:

```csharp
        if (command.IsEnabled)
        {
            if (string.IsNullOrEmpty(command.Text))
                return new SetGiftSettingResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.ValidationError,
                    Params = new Dictionary<string, string> { { "message", "Text is required when enabled." } },
                };
        }

        if (command.Text?.Length > MaxTextLength)
            return new SetGiftSettingResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ValidationError,
                Params = new Dictionary<string, string> { { "message", "Text cannot exceed 50 characters." } },
            };
```

Use the Edit tool. Replace:

```csharp
        if (command.IsEnabled)
        {
            if (string.IsNullOrEmpty(command.Text))
                return new SetGiftSettingResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.ValidationError,
                    Params = new Dictionary<string, string> { { "message", "Text is required when enabled." } },
                };
        }

        if (command.Text?.Length > MaxTextLength)
            return new SetGiftSettingResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ValidationError,
                Params = new Dictionary<string, string> { { "message", "Text cannot exceed 50 characters." } },
            };
```

With:

```csharp
        if (command.Text?.Length > MaxTextLength)
            return new SetGiftSettingResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ValidationError,
                Params = new Dictionary<string, string> { { "message", "Text cannot exceed 50 characters." } },
            };
```

(This removes the whole `if (command.IsEnabled) { ... }` wrapper along with the empty-text check inside it, since that wrapper had no other content left. The max-length check below it is untouched here — it is removed in the next task.)

**Step 4 — Run the test again and confirm it passes (green).**

```bash
cd /home/user/worktrees/feature-4072-Arch-Review-Logistics-Setgiftsettinghandler-Duplic
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SetGiftSettingHandlerTests.Handle_SavesSetting_WhenEnabledWithEmptyText"
```

Expected output: **1 test run, 1 passed.**

**Step 5 — Run the full `SetGiftSettingHandlerTests` class.**

```bash
cd /home/user/worktrees/feature-4072-Arch-Review-Logistics-Setgiftsettinghandler-Duplic
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SetGiftSettingHandlerTests"
```

Expected output: **all tests passed** (`Handle_ReturnsFailure_WhenTextExceedsMaxLength` still passes at this point — its `if`-block is still in the handler and untouched here).

**Step 6 — Commit.**

```bash
cd /home/user/worktrees/feature-4072-Arch-Review-Logistics-Setgiftsettinghandler-Duplic
git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingHandler.cs backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingHandlerTests.cs
git commit -m "Remove duplicated empty-text validation from SetGiftSettingHandler

SetGiftSettingValidator already enforces Text non-empty when enabled
via the ValidationBehavior pipeline. The handler's own if-block was
dead code under normal DI wiring. Rewrote the corresponding unit test
to assert the handler now succeeds when called directly, since
rejection of this input is owned entirely by the pipeline validator."
```

---

