### task: rewrite-max-length-test

**Goal**: Rewrite `Handle_ReturnsFailure_WhenTextExceedsMaxLength` to assert success/save, confirm red against the current handler, then delete the max-length `if`-block and the now-unused `MaxTextLength` constant, leaving the handler in its final target shape.

**Step 1 — Rewrite the test.**

File: `backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingHandlerTests.cs`

Use the Edit tool. Replace:

```csharp
    [Fact]
    public async Task Handle_ReturnsFailure_WhenTextExceedsMaxLength()
    {
        var command = new SetGiftSettingCommand
        {
            IsEnabled = false,
            ThresholdCzk = 0,
            Text = new string('X', 51),
        };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        _repositoryMock.Verify(r => r.SaveAsync(It.IsAny<GiftSetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

With:

```csharp
    [Fact]
    public async Task Handle_SavesSetting_WhenTextExceedsMaxLength()
    {
        // Text longer than 50 chars is rejected end-to-end by ValidationBehavior +
        // SetGiftSettingValidator before the handler ever runs (see SetGiftSettingValidatorTests).
        // The handler itself no longer re-validates this, so calling it directly succeeds.
        var command = new SetGiftSettingCommand
        {
            IsEnabled = false,
            ThresholdCzk = 0,
            Text = new string('X', 51),
        };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        _repositoryMock.Verify(r => r.SaveAsync(It.Is<GiftSetting>(g => g.ModifiedBy == "user-1"), It.IsAny<CancellationToken>()), Times.Once);
    }
```

**Step 2 — Run the test and confirm it fails (red).**

```bash
cd /home/user/worktrees/feature-4072-Arch-Review-Logistics-Setgiftsettinghandler-Duplic
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SetGiftSettingHandlerTests.Handle_SavesSetting_WhenTextExceedsMaxLength"
```

Expected output: **1 test run, 1 failed** (`result.Success` expected `True`, found `False` — the handler's `if (command.Text?.Length > MaxTextLength)` block still returns `Success = false`).

**Step 3 — Delete the max-length `if`-block and the `MaxTextLength` constant from the handler.**

File: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingHandler.cs`

At this point (after the two prior tasks), the full file reads:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings.UseCases.SetGiftSetting;

public sealed class SetGiftSettingHandler : IRequestHandler<SetGiftSettingCommand, SetGiftSettingResponse>
{
    private const int MaxTextLength = 50;

    private readonly IGiftSettingRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public SetGiftSettingHandler(IGiftSettingRepository repository, ICurrentUserService currentUserService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
    }

    public async Task<SetGiftSettingResponse> Handle(SetGiftSettingCommand command, CancellationToken cancellationToken)
    {
        var currentUser = _currentUserService.GetCurrentUser();
        if (string.IsNullOrEmpty(currentUser.Id))
        {
            return new SetGiftSettingResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.Unauthorized,
            };
        }

        if (command.Text?.Length > MaxTextLength)
            return new SetGiftSettingResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ValidationError,
                Params = new Dictionary<string, string> { { "message", "Text cannot exceed 50 characters." } },
            };

        var setting = new GiftSetting(command.IsEnabled, command.ThresholdCzk, command.Text ?? string.Empty, currentUser.Id);
        await _repository.SaveAsync(setting, cancellationToken);
        return new SetGiftSettingResponse();
    }
}
```

Use the Edit tool, in two edits:

Edit 3a — remove the constant. Replace:

```csharp
public sealed class SetGiftSettingHandler : IRequestHandler<SetGiftSettingCommand, SetGiftSettingResponse>
{
    private const int MaxTextLength = 50;

    private readonly IGiftSettingRepository _repository;
```

With:

```csharp
public sealed class SetGiftSettingHandler : IRequestHandler<SetGiftSettingCommand, SetGiftSettingResponse>
{
    private readonly IGiftSettingRepository _repository;
```

Edit 3b — remove the max-length check. Replace:

```csharp
        if (command.Text?.Length > MaxTextLength)
            return new SetGiftSettingResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ValidationError,
                Params = new Dictionary<string, string> { { "message", "Text cannot exceed 50 characters." } },
            };

        var setting = new GiftSetting(command.IsEnabled, command.ThresholdCzk, command.Text ?? string.Empty, currentUser.Id);
```

With:

```csharp
        var setting = new GiftSetting(command.IsEnabled, command.ThresholdCzk, command.Text ?? string.Empty, currentUser.Id);
```

The resulting full file must now read exactly:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings.UseCases.SetGiftSetting;

public sealed class SetGiftSettingHandler : IRequestHandler<SetGiftSettingCommand, SetGiftSettingResponse>
{
    private readonly IGiftSettingRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public SetGiftSettingHandler(IGiftSettingRepository repository, ICurrentUserService currentUserService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
    }

    public async Task<SetGiftSettingResponse> Handle(SetGiftSettingCommand command, CancellationToken cancellationToken)
    {
        var currentUser = _currentUserService.GetCurrentUser();
        if (string.IsNullOrEmpty(currentUser.Id))
        {
            return new SetGiftSettingResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.Unauthorized,
            };
        }

        var setting = new GiftSetting(command.IsEnabled, command.ThresholdCzk, command.Text ?? string.Empty, currentUser.Id);
        await _repository.SaveAsync(setting, cancellationToken);
        return new SetGiftSettingResponse();
    }
}
```

**Step 4 — Run the test again and confirm it passes (green).**

```bash
cd /home/user/worktrees/feature-4072-Arch-Review-Logistics-Setgiftsettinghandler-Duplic
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SetGiftSettingHandlerTests.Handle_SavesSetting_WhenTextExceedsMaxLength"
```

Expected output: **1 test run, 1 passed.**

**Step 5 — Run the full `SetGiftSettingHandlerTests` class and confirm all 6 tests pass.**

```bash
cd /home/user/worktrees/feature-4072-Arch-Review-Logistics-Setgiftsettinghandler-Duplic
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SetGiftSettingHandlerTests"
```

Expected output: **6 tests run, 6 passed, 0 failed** — `Handle_SavesSetting_WhenDisabled`, `Handle_SavesSetting_WhenEnabledWithValidValues`, `Handle_SavesSetting_WhenEnabledWithZeroThreshold`, `Handle_SavesSetting_WhenEnabledWithEmptyText`, `Handle_SavesSetting_WhenTextExceedsMaxLength`, `Handle_ReturnsUnauthorized_WhenCurrentUserIdIsNullOrEmpty`.

**Step 6 — Commit.**

```bash
cd /home/user/worktrees/feature-4072-Arch-Review-Logistics-Setgiftsettinghandler-Duplic
git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingHandler.cs backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingHandlerTests.cs
git commit -m "Remove duplicated max-length validation from SetGiftSettingHandler

SetGiftSettingValidator already enforces Text.Length <= 50 via the
ValidationBehavior pipeline (RuleFor(x => x.Text).MaximumLength(50)).
The handler's own if-block and the now-unused MaxTextLength constant
were dead code under normal DI wiring. Rewrote the corresponding unit
test to assert the handler now succeeds when called directly, since
rejection of this input is owned entirely by the pipeline validator.
The handler now only performs the current-user authorization check
before constructing and saving the GiftSetting entity."
```

---

