# Specification: User Identity Resolution Fix for GiftPackageManufactureService

## Summary
`GiftPackageManufactureService` currently resolves the current user internally via `ICurrentUserService`, violating ADR-005's rule that identity resolution happens only inside MediatR handlers. This change removes `ICurrentUserService` from the service entirely, has the two calling handlers (`CreateGiftPackageManufactureHandler`, `DisassembleGiftPackageHandler`) resolve the user and pass a plain `string userName` into the service methods, and updates the corresponding interface and tests to match. No behavioral/business-logic change is intended — this is a pure architecture-compliance refactor.

## Background
ADR-005 (`docs/architecture/development_guidelines.md`, §"User Identity Resolution") establishes exactly one place where `ICurrentUserService` may be resolved: inside MediatR handlers. Application-layer services must not depend on it. `GiftPackageManufactureService` (`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs`) injects `ICurrentUserService` in its constructor (line 18/28) and calls `_currentUserService.GetCurrentUser().Name ?? "System"` directly inside `CreateManufactureAsync()` (line 155) and `DisassembleGiftPackageAsync()` (line 236).

Both call sites — `CreateGiftPackageManufactureHandler.Handle()` and `DisassembleGiftPackageHandler.Handle()` — currently do nothing with identity; they just forward the request to the service. This means identity resolution has silently migrated from the handler layer into an Application-layer service, coupling that service implicitly to the HTTP request lifecycle (since the concrete `CurrentUserService` depends on `IHttpContextAccessor`) and hiding a web-context dependency behind an Application abstraction. This was flagged by the daily arch-review routine (2026-09-05) as an ADR-005 violation and is being fixed proactively, independent of any current background-job or non-HTTP caller of this service (none exists today — see Non-Functional Requirements and Out of Scope).

## Functional Requirements

### FR-1: Remove `ICurrentUserService` dependency from `GiftPackageManufactureService`
Remove the `ICurrentUserService _currentUserService` field and the corresponding constructor parameter from `GiftPackageManufactureService`. The class must no longer reference `Anela.Heblo.Domain.Features.Users.ICurrentUserService` or call `GetCurrentUser()` anywhere.

**Acceptance criteria:**
- `GiftPackageManufactureService`'s constructor no longer accepts an `ICurrentUserService` parameter.
- No remaining reference to `ICurrentUserService` or `_currentUserService` in `GiftPackageManufactureService.cs`.
- The `using Anela.Heblo.Domain.Features.Users;` import is removed from `GiftPackageManufactureService.cs` if nothing else in the file needs it.

### FR-2: `CreateManufactureAsync` accepts `userName` as a parameter
Change the signature of `IGiftPackageManufactureService.CreateManufactureAsync` and its implementation in `GiftPackageManufactureService` to accept a new `string userName` parameter, inserted after `allowStockOverride` and before `cancellationToken`:

```csharp
Task<GiftPackageManufactureDto> CreateManufactureAsync(
    string giftPackageCode,
    int quantity,
    bool allowStockOverride,
    string userName,
    CancellationToken cancellationToken = default);
```

Inside the implementation, replace `_currentUserService.GetCurrentUser().Name ?? "System"` (line 155) with the incoming `userName` parameter, passed directly into the `GiftPackageManufactureLog` constructor. The `[DisplayName("GiftPackageManufacture-{0}-{1}")]` attribute (and the matching one on the interface, `[DisplayName("GiftPackageManufacture-{0}-{1}x")]`) is left as-is (positional placeholders `{0}`/`{1}` refer to the first two parameters, `giftPackageCode`/`quantity`, which are unaffected by this change).

**Acceptance criteria:**
- `IGiftPackageManufactureService.CreateManufactureAsync` signature includes `string userName` between `allowStockOverride` and `cancellationToken`.
- `GiftPackageManufactureService.CreateManufactureAsync` implementation matches the new interface signature exactly.
- The created `GiftPackageManufactureLog`'s `CreatedBy` is populated from the passed-in `userName` argument, not from any internally resolved user.
- No fallback-to-"System" default logic remains inside the service; if a caller wants a "System" fallback, the caller (handler) supplies it explicitly.

### FR-3: `DisassembleGiftPackageAsync` accepts `userName` as a parameter
Change the signature of `IGiftPackageManufactureService.DisassembleGiftPackageAsync` and its implementation to accept a new `string userName` parameter, inserted after `quantity` and before `cancellationToken`:

```csharp
Task<GiftPackageDisassemblyDto> DisassembleGiftPackageAsync(
    string giftPackageCode,
    int quantity,
    string userName,
    CancellationToken cancellationToken = default);
```

Inside the implementation, replace `_currentUserService.GetCurrentUser().Name ?? "System"` (line 236) with the incoming `userName` parameter, passed directly into the `GiftPackageManufactureLog` constructor (the `GiftPackageOperationType.Disassembly` overload).

**Acceptance criteria:**
- `IGiftPackageManufactureService.DisassembleGiftPackageAsync` signature includes `string userName` between `quantity` and `cancellationToken`.
- `GiftPackageManufactureService.DisassembleGiftPackageAsync` implementation matches the new interface signature exactly.
- The created disassembly `GiftPackageManufactureLog`'s `CreatedBy` (and therefore `GiftPackageDisassemblyDto.DisassembledBy`) is populated from the passed-in `userName` argument.

### FR-4: Handlers resolve identity and pass it to the service
Update `CreateGiftPackageManufactureHandler` and `DisassembleGiftPackageHandler` to inject `ICurrentUserService`, resolve the current user in `Handle()`, and pass `user.Name ?? "System"` as the new `userName` argument to the service call.

`CreateGiftPackageManufactureHandler`:
```csharp
public class CreateGiftPackageManufactureHandler : IRequestHandler<CreateGiftPackageManufactureRequest, CreateGiftPackageManufactureResponse>
{
    private readonly IGiftPackageManufactureService _giftPackageService;
    private readonly ICurrentUserService _currentUserService;

    public CreateGiftPackageManufactureHandler(
        IGiftPackageManufactureService giftPackageService,
        ICurrentUserService currentUserService)
    {
        _giftPackageService = giftPackageService;
        _currentUserService = currentUserService;
    }

    public async Task<CreateGiftPackageManufactureResponse> Handle(CreateGiftPackageManufactureRequest request, CancellationToken cancellationToken)
    {
        var user = _currentUserService.GetCurrentUser();
        var manufacture = await _giftPackageService.CreateManufactureAsync(
            request.GiftPackageCode,
            request.Quantity,
            request.AllowStockOverride,
            user.Name ?? "System",
            cancellationToken);

        return new CreateGiftPackageManufactureResponse
        {
            Manufacture = manufacture
        };
    }
}
```

`DisassembleGiftPackageHandler`: same pattern — inject `ICurrentUserService`, resolve `user` at the top of `Handle()`, pass `user.Name ?? "System"` as the new argument to `DisassembleGiftPackageAsync`, keeping the existing `try`/`catch` blocks and error-response mapping unchanged.

**Acceptance criteria:**
- Both handlers' constructors accept an additional `ICurrentUserService currentUserService` parameter and store it in a `_currentUserService` field.
- Both handlers call `_currentUserService.GetCurrentUser()` exactly once per `Handle()` invocation, before calling the service.
- Both handlers pass `user.Name ?? "System"` as the `userName` argument to the respective service method call.
- Existing error handling in `DisassembleGiftPackageHandler` (catching `InvalidOperationException` and `ArgumentException` from the service call) is preserved unchanged.
- Neither handler exposes `UserId`/`ModifiedBy`/`UserName` as a client-settable field on `CreateGiftPackageManufactureRequest` or `DisassembleGiftPackageRequest` (identity stays server-resolved, per ADR-005).

### FR-5: Update existing unit tests to match new signatures
Update the three test files that reference the changed types so the suite continues to compile and pass:
- `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs` — remove `ICurrentUserService` mock and constructor argument from `GiftPackageManufactureService` construction; update `CreateManufactureAsync`/`DisassembleGiftPackageAsync` call sites (and any `Setup`/`Verify` on them) to pass an explicit `userName` string instead of relying on `_currentUserServiceMock.Setup(x => x.GetCurrentUser())`; assert `CreatedBy`/`DisassembledBy` against the literal passed-in username.
- `backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/DisassembleGiftPackageHandlerTests.cs` — add an `ICurrentUserService` mock to `CreateSut()`, set up `GetCurrentUser()` to return a test `CurrentUser`, and update the `_serviceMock.Setup`/`Verify` calls on `DisassembleGiftPackageAsync` to include the expected `userName` argument (e.g. matching the mocked user's `Name`, or `"System"` when testing the null-name fallback).
- Add or update handler-level test coverage for `CreateGiftPackageManufactureHandler` (create one if none currently exists — none was found under `test/Anela.Heblo.Tests/Application/GiftPackageManufacture/`) mirroring the same pattern: mock `ICurrentUserService`, verify the resolved `userName` is forwarded to `IGiftPackageManufactureService.CreateManufactureAsync`.
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — re-run and confirm it still passes; no changes are anticipated here, but this test enforces architectural boundaries and previously did not fail on this violation, so its ruleset should be checked for whether it can be extended to catch "Application service takes `ICurrentUserService` dependency" in the future (see Out of Scope).

**Acceptance criteria:**
- All three existing test files compile and their tests pass after the FR-1–FR-4 changes, with no test still injecting `ICurrentUserService` into `GiftPackageManufactureService`.
- At least one test per handler explicitly verifies that the value returned by `ICurrentUserService.GetCurrentUser().Name` (or `"System"` when `Name` is null) is the exact `userName` value forwarded to the service call.
- `dotnet build` and the full `dotnet test` (or at minimum the `Anela.Heblo.Tests` project) succeed with no new failures.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected — this is a pure parameter-passing refactor with no added I/O, allocations, or algorithmic changes. No new performance testing is required.

### NFR-2: Security
No new security surface is introduced. `userName` continues to be resolved server-side from `ICurrentUserService` inside the handler (never from a client-supplied field on the request DTO), preserving the existing anti-spoofing guarantee already required by ADR-005 for all identity-bearing fields. `CreateGiftPackageManufactureRequest` and `DisassembleGiftPackageRequest` must not gain a client-settable `UserId`/`UserName`/`ModifiedBy` field as part of this change.

### NFR-3: Backward compatibility
`IGiftPackageManufactureService` is an internal Application-layer interface consumed only by `CreateGiftPackageManufactureHandler` and `DisassembleGiftPackageHandler` (confirmed: no other production call sites exist in `backend/src`). Changing its method signatures is a breaking change to that interface's contract, but since both call sites live in this same PR and are updated together, no external consumer is affected. This is not an HTTP API contract change — `CreateGiftPackageManufactureRequest`/`Response` and `DisassembleGiftPackageRequest`/`Response` DTOs, and therefore the generated OpenAPI client, are unaffected.

## Data Model
No data model changes. `GiftPackageManufactureLog` (domain entity in `Anela.Heblo.Domain.Features.Logistics.GiftPackageManufacture`) already accepts a `createdBy` (or equivalent) string in its constructor(s); this change only alters where that string value originates (handler-resolved `userName` parameter instead of a service-internal `ICurrentUserService` call). No schema or migration changes are required.

## API / Interface Design

**Changed interface — `IGiftPackageManufactureService`:**

| Method | Before | After |
| --- | --- | --- |
| `CreateManufactureAsync` | `(string giftPackageCode, int quantity, bool allowStockOverride, CancellationToken cancellationToken = default)` | `(string giftPackageCode, int quantity, bool allowStockOverride, string userName, CancellationToken cancellationToken = default)` |
| `DisassembleGiftPackageAsync` | `(string giftPackageCode, int quantity, CancellationToken cancellationToken = default)` | `(string giftPackageCode, int quantity, string userName, CancellationToken cancellationToken = default)` |

**Changed constructors:**
- `GiftPackageManufactureService`: removes `ICurrentUserService currentUserService` parameter.
- `CreateGiftPackageManufactureHandler`: adds `ICurrentUserService currentUserService` parameter.
- `DisassembleGiftPackageHandler`: adds `ICurrentUserService currentUserService` parameter.

No changes to the public HTTP endpoints, MediatR request/response DTOs (`CreateGiftPackageManufactureRequest/Response`, `DisassembleGiftPackageRequest/Response`), or any controller. No changes to `ICurrentUserService`, `CurrentUser`, or DI registration (`GiftPackageManufactureModule.cs`) — `ICurrentUserService` is already registered in the container by `UsersModule.cs` (`Anela.Heblo.API`) and handlers already resolve dependencies through standard constructor injection, so no new DI wiring is needed beyond adding the constructor parameters.

## Dependencies
- `ICurrentUserService` / `CurrentUser` (`Anela.Heblo.Domain.Features.Users`) — already available for injection into MediatR handlers; no changes to this interface.
- MediatR (existing) — handler resolution and DI already wire `ICurrentUserService` into other handlers in the codebase following this exact pattern (e.g., referenced `CreateNewTransportBoxHandler` per ADR-005 guidance), so no new library or registration is needed.
- No external service, new NuGet package, or database dependency is introduced.

## Out of Scope
- Any change to `GiftPackageManufactureModule.cs` DI registration — `ICurrentUserService` is already registered application-wide.
- Introducing an actual background-job (e.g., Hangfire) caller of `GiftPackageManufactureService` — the `[DisplayName(...)]` attributes on `CreateManufactureAsync` exist for job-display naming conventions already used elsewhere in the codebase, but no Hangfire trigger currently invokes this method; this fix removes the *coupling risk* for such a future caller, it does not add one.
- Any change to `GetAvailableGiftPackagesAsync` or `GetGiftPackageDetailAsync` — neither resolves user identity and both are unaffected.
- Any change to the `GiftPackageManufactureLog` domain entity's constructor signatures.
- Extending `ModuleBoundariesTests.cs` (or any other architecture test) with an automated rule that would catch this class of violation going forward — a reasonable future follow-up, but not implemented here.
- Any change to the frontend, the generated OpenAPI/TypeScript client, or any HTTP contract — this fix is entirely internal to the Application layer's service/handler boundary.
- Any change to logging behavior, log levels, or the `_logger.LogInformation`/`LogDebug` call sites already present in the service (they are unaffected by where `userName` comes from).

## Open Questions
None.

## Status: COMPLETE
