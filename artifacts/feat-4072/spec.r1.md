# Specification: Remove Duplicated Validation from SetGiftSettingHandler

## Summary
`SetGiftSettingHandler` re-implements, in `if`-blocks, the same three rules that `SetGiftSettingValidator` already enforces via the `ValidationBehavior<TRequest, TResponse>` MediatR pipeline registered in `GiftSettingsModule`. This spec defines the removal of the handler's redundant validation code so a single source of truth (the FluentValidation validator) governs `SetGiftSettingCommand` correctness, and aligns the resulting error behavior with the rest of the codebase's pipeline-driven validation error shape (HTTP 400 `ProblemDetails` via `ValidationExceptionHandler`) instead of the handler's ad-hoc `SetGiftSettingResponse.ErrorCode = ValidationError` shape. Existing unit tests that assert the old in-handler failure behavior must be updated to match the new contract.

## Background
`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingHandler.cs` (verified, lines 33–58) contains three `if`-blocks that duplicate rules already declared in `SetGiftSettingValidator.cs` (verified, lines 9, 13–14):

| Rule | Validator | Handler |
|------|-----------|---------|
| `ThresholdCzk > 0` when `IsEnabled` | `SetGiftSettingValidator.cs:13` | `SetGiftSettingHandler.cs:35–41` |
| `Text` not empty when `IsEnabled` | `SetGiftSettingValidator.cs:14` | `SetGiftSettingHandler.cs:43–49` |
| `Text.Length ≤ 50` | `SetGiftSettingValidator.cs:9` (via `.MaximumLength(50)`) | `SetGiftSettingHandler.cs:52–58` (via local `MaxTextLength` constant) |

`GiftSettingsModule.cs:16–17` registers `SetGiftSettingValidator` as `IValidator<SetGiftSettingCommand>` and registers `ValidationBehavior<SetGiftSettingCommand, SetGiftSettingResponse>` as a MediatR `IPipelineBehavior`. Verified in `ValidationBehavior.cs`: the behavior runs all registered validators before `next()` (i.e., before the handler executes) and, on any failure, `throw`s a FluentValidation `ValidationException` — it never calls into the handler. This exception is caught globally by `ValidationExceptionHandler.cs` (registered as an `IExceptionHandler`), which returns an HTTP 400 with a `ProblemDetails` body carrying an `errors: [{ propertyName, errorMessage }]` extension array.

This means the handler's three `if`-blocks are dead code under normal operation — they only execute if the pipeline behavior is somehow bypassed, which the DI registration in `GiftSettingsModule` prevents. Two problems follow:

1. **Maintenance burden / drift risk**: any future rule change (e.g., raising the max length from 50 to 100) requires editing both `SetGiftSettingValidator.cs` and `SetGiftSettingHandler.cs`, and nothing enforces that the two stay in sync.
2. **Inconsistent error contract**: when the *validator* rejects a command, the caller receives an HTTP 400 `ProblemDetails` with an `errors` array (via `ValidationExceptionHandler`). When the handler's dead-code path were ever to execute instead, the caller would receive an HTTP 200 (or whatever status the controller maps `SetGiftSettingResponse` to) with `Success = false`, `ErrorCode = ErrorCodes.ValidationError`, and a `Params` dictionary containing a raw English `message` string (e.g. `"ThresholdCzk must be greater than zero when enabled."`) — a shape inconsistent with the pipeline's structured, localizable error convention used elsewhere. Removing the dead code eliminates this latent inconsistency entirely, since the handler-side path can never again be reached.

## Functional Requirements

### FR-1: Remove duplicated validation logic from SetGiftSettingHandler
Delete the three `if`-blocks in `SetGiftSettingHandler.Handle` (verified at lines 33–58) that re-check: `command.IsEnabled && command.ThresholdCzk <= 0`, `command.IsEnabled && string.IsNullOrEmpty(command.Text)`, and `command.Text?.Length > MaxTextLength`. The handler must retain only:
1. Resolving the current user via `ICurrentUserService.GetCurrentUser()` and returning `ErrorCodes.Unauthorized` when `currentUser.Id` is null/empty (this is an authorization concern, not covered by `SetGiftSettingValidator`, and is out of scope for removal).
2. Constructing a `GiftSetting` domain entity from the (now pipeline-validated) command.
3. Calling `_repository.SaveAsync(...)`.
4. Returning a successful `SetGiftSettingResponse`.

The unused `MaxTextLength` constant (line 10) must be removed from the handler; it is no longer referenced once the length check is deleted. The 50-character limit remains enforced solely by `SetGiftSettingValidator.cs:9` (`.MaximumLength(50)`) — a named constant may optionally be introduced there for clarity, but this is not required by this spec.

**Acceptance criteria:**
- `SetGiftSettingHandler.cs` no longer contains any `ThresholdCzk`, `Text` emptiness, or `Text.Length` checks — the only conditional logic remaining is the current-user/authorization check.
- The `MaxTextLength` constant and `ErrorCodes.ValidationError` reference are removed from `SetGiftSettingHandler.cs` (the `Unauthorized` `ErrorCodes` usage remains).
- `SetGiftSettingValidator.cs` is unchanged (no new rules added, no rules removed) — it remains the sole enforcement point for `ThresholdCzk`, `Text` non-empty, and `Text` max length.
- `GiftSettingsModule.cs` is unchanged — `ValidationBehavior<SetGiftSettingCommand, SetGiftSettingResponse>` remains registered as the pipeline behavior ahead of the handler.
- A `SetGiftSettingCommand` with `IsEnabled = true`, `ThresholdCzk = 0` never reaches `SetGiftSettingHandler.Handle` when dispatched through MediatR (`IMediator.Send`) with the full pipeline wired up (i.e., through the module's DI registration or an integration-style test that includes the pipeline behavior) — the `ValidationException` is thrown by `ValidationBehavior` before the handler runs.
- Directly unit-testing `SetGiftSettingHandler.Handle` in isolation (validator/behavior bypassed, as the existing unit tests do) with an invalid command (e.g., `IsEnabled = true, ThresholdCzk = 0`) now **succeeds** and calls `SaveAsync`, because the handler itself performs no such validation — this is the expected, intended behavior change and must be reflected in the updated unit tests (see FR-2).

### FR-2: Update existing unit tests to match the new handler contract
`backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingHandlerTests.cs` currently contains three tests that invoke `SetGiftSettingHandler.Handle` directly (bypassing the MediatR pipeline and `ValidationBehavior`) and assert `Success == false` for invalid input:
- `Handle_ReturnsFailure_WhenEnabledWithZeroThreshold`
- `Handle_ReturnsFailure_WhenEnabledWithEmptyText`
- `Handle_ReturnsFailure_WhenTextExceedsMaxLength`

After FR-1, these three tests will fail (the handler will now succeed and save for these previously-"invalid" inputs) because they invoke the handler directly, without the pipeline behavior in front of it. These tests must be removed or rewritten to reflect that the handler no longer performs this validation — equivalent coverage for the *validation rules themselves* already exists in `SetGiftSettingValidatorTests.cs` and must be preserved/confirmed there.

The two tests that exercise behavior the handler still owns must be kept unchanged:
- `Handle_SavesSetting_WhenDisabled`
- `Handle_SavesSetting_WhenEnabledWithValidValues`
- `Handle_ReturnsUnauthorized_WhenCurrentUserIdIsNullOrEmpty`

**Acceptance criteria:**
- The three now-invalid handler-level failure tests are removed from `SetGiftSettingHandlerTests.cs` (or rewritten to assert the new success-and-save behavior, with a comment noting that end-to-end rejection of these inputs is enforced by the pipeline, not the handler).
- `SetGiftSettingValidatorTests.cs` contains (or is extended to contain, if a gap is found) a test for each of the three rules: `ThresholdCzk > 0` when enabled, `Text` not empty when enabled, `Text.Length ≤ 50` — verified independently of `SetGiftSettingHandler`.
- Optionally, an integration-level test (or a new one added under this spec) exercises `SetGiftSettingCommand` through the full MediatR pipeline (`IMediator.Send`, with `GiftSettingsModule`'s DI registrations in place) with an invalid payload and asserts that a `FluentValidation.ValidationException` is thrown — demonstrating end-to-end enforcement now rests entirely on the pipeline. This is recommended but not mandatory for this change to be considered complete, since it exercises infrastructure (`ValidationBehavior`) that is not itself being modified.
- `dotnet test` for the `Anela.Heblo.Tests` project passes with no regressions after the changes.

## Non-Functional Requirements

### NFR-1: Behavioral equivalence for valid/authorized requests
For any `SetGiftSettingCommand` that already passes `SetGiftSettingValidator` today, the end-to-end behavior (HTTP status, response body, persisted `GiftSetting` entity) after this change must be byte-for-byte identical to before the change. This is a refactor of dead code only — no change in externally observable behavior for valid inputs is permitted.

### NFR-2: No change to the public error contract for invalid requests
Before this change, an invalid command (e.g., `IsEnabled = true, ThresholdCzk = 0`) submitted through the real API (controller → MediatR → pipeline) was already rejected by `ValidationBehavior` before ever reaching the handler's dead-code path, per the verified pipeline registration in `GiftSettingsModule.cs`. Therefore this change does not alter the response any real API caller receives for invalid input — callers already get the pipeline's HTTP 400 `ProblemDetails` shape (`errors: [{ propertyName, errorMessage }]`) today, and will continue to get exactly that after the handler's dead code is removed. This NFR exists to make explicit that FR-1 is a pure internal cleanup with zero observable API contract change, and to rule out any accidental behavior change to the controller or exception handling pipeline.

### NFR-3: No performance impact
This is a code-deletion change with no new I/O, allocations, or pipeline stages. No performance testing beyond existing test suite execution is required.

## Data Model
No data model changes. `GiftSetting` (domain entity, `Anela.Heblo.Domain.Features.Logistics.GiftSettings`) and its persistence via `IGiftSettingRepository.SaveAsync` are unaffected — the handler's construction call (`new GiftSetting(command.IsEnabled, command.ThresholdCzk, command.Text ?? string.Empty, currentUser.Id)`) is unchanged in both signature and call site (only the code paths that could prevent reaching this line are removed).

## API / Interface Design
No public API surface changes. `SetGiftSettingCommand`, `SetGiftSettingResponse`, and the associated MVC controller endpoint (wherever `SetGiftSettingCommand` is dispatched from) retain their existing shapes and route. The only change is internal: `SetGiftSettingHandler.Handle` has three fewer conditional branches and one fewer private constant.

Before (handler, relevant excerpt, lines 33–58 — to be deleted):
```csharp
if (command.IsEnabled)
{
    if (command.ThresholdCzk <= 0)
        return new SetGiftSettingResponse { Success = false, ErrorCode = ErrorCodes.ValidationError, Params = ... };

    if (string.IsNullOrEmpty(command.Text))
        return new SetGiftSettingResponse { Success = false, ErrorCode = ErrorCodes.ValidationError, Params = ... };
}

if (command.Text?.Length > MaxTextLength)
    return new SetGiftSettingResponse { Success = false, ErrorCode = ErrorCodes.ValidationError, Params = ... };
```

After (handler body, target shape):
```csharp
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
```

## Dependencies
- `SetGiftSettingValidator` (`FluentValidation.AbstractValidator<SetGiftSettingCommand>`) — must remain registered and unchanged; this change increases reliance on it as the sole validation authority.
- `ValidationBehavior<TRequest, TResponse>` (`Anela.Heblo.Application.Common.Behaviors`) — the generic MediatR pipeline behavior that throws `FluentValidation.ValidationException` on failed validation; already registered per-module (here, in `GiftSettingsModule.AddGiftSettingsModule`). No change required to this shared component.
- `ValidationExceptionHandler` (`Anela.Heblo.API.Infrastructure.ExceptionHandling`) — global `IExceptionHandler` that maps `ValidationException` to HTTP 400 `ProblemDetails`. No change required.
- `GiftSettingsModule.AddGiftSettingsModule` — must continue registering both the validator and the pipeline behavior for `SetGiftSettingCommand`/`SetGiftSettingResponse`; this spec depends on that registration remaining correct and is not itself modifying it.
- Existing test projects: `SetGiftSettingHandlerTests.cs` and `SetGiftSettingValidatorTests.cs` under `backend/test/Anela.Heblo.Tests/Application/GiftSettings/`.

## Out of Scope
- Any change to `SetGiftSettingValidator.cs`'s rules or their values (e.g., raising the 50-character limit) — this spec only removes duplication, it does not change what is validated.
- Any change to `ValidationBehavior`, `ValidationExceptionHandler`, or any other shared pipeline infrastructure.
- Any change to the `Unauthorized` check in the handler (current-user resolution) — this is an authorization concern outside `SetGiftSettingValidator`'s remit and is intentionally retained as-is.
- Auditing or fixing similar validator/handler duplication in other Logistics (or other module) command handlers — this spec is scoped strictly to `SetGiftSettingHandler`. (If the same anti-pattern exists elsewhere, it should be filed as a separate arch-review finding.)
- Introducing a shared/localized error-message resource system for validation messages — not required since the handler-side ad-hoc messages are being deleted rather than reformed.
- Adding a new integration test harness for pipeline-level validation testing if one does not already exist in this codebase; adding such a test is recommended (FR-2) but not mandatory.

## Open Questions
None.

## Status: COMPLETE
