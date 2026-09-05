### task: rewrite-zero-threshold-test

**Goal**: Rewrite `Handle_ReturnsFailure_WhenEnabledWithZeroThreshold` to assert the handler now succeeds and saves (since this rule will no longer be enforced in the handler), confirm it fails against the current handler, then delete the corresponding `if`-block from `SetGiftSettingHandler.cs` to make it pass.

**Step 1 — Read the current test method (context, no edit).**

File: `backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingHandlerTests.cs`

Current content of the method to change (lines 56-70):

```csharp
    [Fact]
    public async Task Handle_ReturnsFailure_WhenEnabledWithZeroThreshold()
    {
        var command = new SetGiftSettingCommand
        {
            IsEnabled = true,
            ThresholdCzk = 0,
            Text = "DÁREK ZDARMA",
        };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        _repositoryMock.Verify(r => r.SaveAsync(It.IsAny<GiftSetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

**Step 2 — Rewrite the test to assert the new (success) behavior.**

Use the Edit tool on `backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingHandlerTests.cs`.

Replace:

```csharp
    [Fact]
    public async Task Handle_ReturnsFailure_WhenEnabledWithZeroThreshold()
    {
        var command = new SetGiftSettingCommand
        {
            IsEnabled = true,
            ThresholdCzk = 0,
            Text = "DÁREK ZDARMA",
        };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        _repositoryMock.Verify(r => r.SaveAsync(It.IsAny<GiftSetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

With:

```csharp
    [Fact]
    public async Task Handle_SavesSetting_WhenEnabledWithZeroThreshold()
    {
        // ThresholdCzk <= 0 while enabled is rejected end-to-end by ValidationBehavior +
        // SetGiftSettingValidator before the handler ever runs (see SetGiftSettingValidatorTests).
        // The handler itself no longer re-validates this, so calling it directly succeeds.
        var command = new SetGiftSettingCommand
        {
            IsEnabled = true,
            ThresholdCzk = 0,
            Text = "DÁREK ZDARMA",
        };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        _repositoryMock.Verify(r => r.SaveAsync(It.Is<GiftSetting>(g => g.ModifiedBy == "user-1"), It.IsAny<CancellationToken>()), Times.Once);
    }
```

**Step 3 — Run the test and confirm it fails (red) against the current, unmodified handler.**

```bash
cd /home/user/worktrees/feature-4072-Arch-Review-Logistics-Setgiftsettinghandler-Duplic
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SetGiftSettingHandlerTests.Handle_SavesSetting_WhenEnabledWithZeroThreshold"
```

Expected output: **1 test run, 1 failed.** The failure assertion should be `result.Success` expected `True` but found `False` (the handler's still-present `if (command.ThresholdCzk <= 0)` block returns `Success = false`). This confirms the dead code is still there and the test now correctly targets the intended post-refactor behavior.

**Step 4 — Delete the zero-threshold `if`-block from the handler.**

File: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingHandler.cs`

Use the Edit tool. Remove only this block (leave the `if (string.IsNullOrEmpty(command.Text))` block below it in place for now — that is handled in the next task):

Replace:

```csharp
        if (command.IsEnabled)
        {
            if (command.ThresholdCzk <= 0)
                return new SetGiftSettingResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.ValidationError,
                    Params = new Dictionary<string, string> { { "message", "ThresholdCzk must be greater than zero when enabled." } },
                };

            if (string.IsNullOrEmpty(command.Text))
                return new SetGiftSettingResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.ValidationError,
                    Params = new Dictionary<string, string> { { "message", "Text is required when enabled." } },
                };
        }
```

With:

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
```

**Step 5 — Run the same test again and confirm it passes (green).**

```bash
cd /home/user/worktrees/feature-4072-Arch-Review-Logistics-Setgiftsettinghandler-Duplic
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SetGiftSettingHandlerTests.Handle_SavesSetting_WhenEnabledWithZeroThreshold"
```

Expected output: **1 test run, 1 passed.**

**Step 6 — Run the full `SetGiftSettingHandlerTests` class to confirm no other test in the file broke.**

```bash
cd /home/user/worktrees/feature-4072-Arch-Review-Logistics-Setgiftsettinghandler-Duplic
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SetGiftSettingHandlerTests"
```

Expected output: the two remaining "failure" tests (`Handle_ReturnsFailure_WhenEnabledWithEmptyText`, `Handle_ReturnsFailure_WhenTextExceedsMaxLength`) are **expected to still pass** at this point, because their corresponding `if`-blocks are still present in the handler and are untouched by this task. All tests in the file should report **passed** (6 total: the 2 untouched success tests, the 1 unauthorized test, the 1 just-rewritten test, and the 2 not-yet-rewritten failure tests).

**Step 7 — Commit.**

```bash
cd /home/user/worktrees/feature-4072-Arch-Review-Logistics-Setgiftsettinghandler-Duplic
git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingHandler.cs backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingHandlerTests.cs
git commit -m "Remove duplicated zero-threshold validation from SetGiftSettingHandler

SetGiftSettingValidator already enforces ThresholdCzk > 0 when enabled
via the ValidationBehavior pipeline. The handler's own if-block was
dead code under normal DI wiring. Rewrote the corresponding unit test
to assert the handler now succeeds when called directly, since
rejection of this input is owned entirely by the pipeline validator."
```

---

