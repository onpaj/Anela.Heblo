# Design: Remove Duplicated Validation from SetGiftSettingHandler

## Component Design

No new components, no component boundary changes. This is a dead-code deletion confined to one existing handler and its unit tests; the pipeline components below are unchanged and are described only to make the resulting responsibility split explicit.

### `SetGiftSettingHandler` (modified)
`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingHandler.cs`

- **Responsibility after this change:** authorization (current-user resolution) → construct `GiftSetting` → persist → return success. Nothing else.
- **Removed:** the three `if`-blocks re-checking `ThresholdCzk > 0`, `Text` non-empty, and `Text.Length ≤ 50` (each currently returning `SetGiftSettingResponse { Success = false, ErrorCode = ErrorCodes.ValidationError, ... }`), plus the now-unused `private const int MaxTextLength = 50` field.
- **Retained:** the `ICurrentUserService.GetCurrentUser()` call and the `ErrorCodes.Unauthorized` short-circuit when `currentUser.Id` is null/empty — an authorization concern that `SetGiftSettingValidator` has no access to and does not express.
- **Target contract** (from spec's "After" shape, confirmed correct by arch review):

```csharp
public async Task<SetGiftSettingResponse> Handle(SetGiftSettingCommand command, CancellationToken cancellationToken)
{
    var currentUser = _currentUserService.GetCurrentUser();
    if (string.IsNullOrEmpty(currentUser.Id))
    {
        return new SetGiftSettingResponse { Success = false, ErrorCode = ErrorCodes.Unauthorized };
    }

    var setting = new GiftSetting(command.IsEnabled, command.ThresholdCzk, command.Text ?? string.Empty, currentUser.Id);
    await _repository.SaveAsync(setting, cancellationToken);
    return new SetGiftSettingResponse();
}
```

### `SetGiftSettingValidator` (unchanged)
Remains the sole enforcement point for the three rules (`ThresholdCzk > 0` when enabled, `Text` non-empty when enabled, `Text.Length ≤ 50`). No rules added or removed.

### `ValidationBehavior<SetGiftSettingCommand, SetGiftSettingResponse>` / `ValidationExceptionHandler` (unchanged)
Continue to run ahead of the handler via `GiftSettingsModule`'s existing DI registration and to map validation failures to HTTP 400 `ProblemDetails` (`errors: [{ propertyName, errorMessage }]`). No behavior, registration, or module-wiring change.

### `SetGiftSettingHandlerTests` (modified)
`backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingHandlerTests.cs`

- Remove or rewrite the three tests that call `Handle` directly with invalid input and assert `Success == false` (`Handle_ReturnsFailure_WhenEnabledWithZeroThreshold`, `Handle_ReturnsFailure_WhenEnabledWithEmptyText`, `Handle_ReturnsFailure_WhenTextExceedsMaxLength`), since the handler no longer performs this validation and calling it directly now succeeds.
- Keep unchanged: `Handle_SavesSetting_WhenDisabled`, `Handle_SavesSetting_WhenEnabledWithValidValues`, `Handle_ReturnsUnauthorized_WhenCurrentUserIdIsNullOrEmpty`.

### `SetGiftSettingValidatorTests` (unchanged)
Already contains one test per rule (`Validator_Fails_WhenEnabledWithZeroThreshold`, `Validator_Fails_WhenEnabledWithEmptyText`, `Validator_Fails_WhenTextExceeds50Chars_EvenWhenDisabled`) — confirmed by arch review to already give full coverage; no additions required.

## Data Schemas

No data schema, DTO, or API contract changes.

- `SetGiftSettingCommand` — unchanged shape.
- `SetGiftSettingResponse` (a `BaseResponse` subclass, correctly a class per this repo's DTO rule) — unchanged shape. The handler's success path still returns a plain `new SetGiftSettingResponse()`; its only remaining failure path still returns `{ Success = false, ErrorCode = ErrorCodes.Unauthorized }`.
- `GiftSetting` domain entity and `IGiftSettingRepository.SaveAsync` signature — unchanged. Construction call site (`new GiftSetting(command.IsEnabled, command.ThresholdCzk, command.Text ?? string.Empty, currentUser.Id)`) is unchanged.
- HTTP error contract for invalid input — unchanged: HTTP 400 `ProblemDetails` with `errors: [{ propertyName, errorMessage }]`, produced by `ValidationBehavior` + `ValidationExceptionHandler` before the handler ever runs. No caller-observable contract change (NFR-2).
