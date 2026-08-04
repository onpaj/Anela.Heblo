# Design: Require authentication on `ManufactureSettingsController`

No UI section — this is a backend-only authorization fix. The single frontend
consumer (`useManufactureSettings.ts`) already calls through
`getAuthenticatedApiClient()` and needs no code change; the generated
TypeScript client is unaffected because the route, verb, and response shape
are unchanged.

## Component design

### `ManufactureSettingsController`

Bring the controller's shape in line with its four siblings
(`ManufacturedProductInventoryController` is the closest structural match:
one class-level `[FeatureAuthorize]`, `BaseApiController`, `HandleResponse`).

**Before** (`backend/src/Anela.Heblo.API/Controllers/ManufactureSettingsController.cs`):

```csharp
[ApiController]
[Route("api/manufacture/settings")]
public class ManufactureSettingsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ManufactureSettingsController(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    [HttpGet]
    [AllowAnonymous]
    public Task<GetManufactureSettingsResponse> GetSettings(CancellationToken cancellationToken)
        => _mediator.Send(new GetManufactureSettingsRequest(), cancellationToken);
}
```

**After:**

```csharp
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureSettings;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Manufacture_ManufactureOrders)]
[ApiController]
[Route("api/manufacture/settings")]
public class ManufactureSettingsController : BaseApiController
{
    private readonly IMediator _mediator;

    public ManufactureSettingsController(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    [HttpGet]
    public async Task<ActionResult<GetManufactureSettingsResponse>> GetSettings(CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetManufactureSettingsRequest(), cancellationToken);
        return HandleResponse(response);
    }
}
```

Changes, each traceable to the finding:
- `ControllerBase` → `BaseApiController`: gains `HandleResponse` and the
  `Logger` helper, matching every sibling controller in the module.
- Drop `Microsoft.AspNetCore.Authorization` using / `[AllowAnonymous]`: the
  controller falls under `DefaultPolicy`
  (`RequireAuthenticatedUser()` + `RequireRole(AccessRoles.Base)`) plus the
  added feature gate, instead of opting out.
- `[FeatureAuthorize(Feature.Manufacture_ManufactureOrders)]` at class level
  (default `AccessLevel.Read`): the only known consumers are on the
  Manufacture Orders creation/detail flow, so this reuses the existing
  feature bucket rather than introducing a new `Feature` enum value (which
  would require a codegen change to `Feature.generated.cs` /
  `AccessRoles.generated.cs` / `AccessMatrix.generated.cs` — out of scope per
  the plan).
- Action returns `Task<ActionResult<GetManufactureSettingsResponse>>` via
  `HandleResponse`, so a future `Success == false` response maps to the
  correct HTTP status instead of always 200.

No changes to `GetManufactureSettingsHandler`, `GetManufactureSettingsRequest`,
or `ManufactureErpOptions` — this is purely a transport/authorization change.

## Data schemas

Unchanged. `GetManufactureSettingsRequest` (no fields),
`GetManufactureSettingsResponse` (`ManufactureGroupId: string?`, plus the
inherited `BaseResponse` fields `Success`/`ErrorCode`/`Params`) keep their
existing JSON shape for authorized callers. The only contract change is
non-structural: the endpoint now requires an authenticated caller holding
`Manufacture_ManufactureOrders` Read (or higher, or `SuperUser`); anonymous
or under-permissioned callers get 401/403 instead of 200.

| | Before | After |
|---|---|---|
| `GET /api/manufacture/settings` (anonymous) | 200 + body | 401 |
| `GET /api/manufacture/settings` (authenticated, no Manufacture permission) | 200 + body | 403 |
| `GET /api/manufacture/settings` (authenticated, `Manufacture_ManufactureOrders` Read+) | 200 + body | 200 + body (unchanged) |

## Test design

**Key constraint discovered while reading the test infrastructure:**
`HebloWebApplicationFactory` runs with `UseMockAuth=true`
(`appsettings.Test.json:12`), and `MockAuthenticationHandler`
(`backend/src/Anela.Heblo.API/Infrastructure/Authentication/MockAuthenticationHandler.cs:21-51`)
**unconditionally authenticates every request as a `SuperUser`**, regardless
of whether an `Authorization` header is present. `AuthorizationIntegrationTests.Me_UnderMockAuth_IsSuperUser_WithAllPermissions`
confirms this is the established, relied-upon behavior. This means the
existing `GetSettings_ShouldBeReachableAnonymously` test — which clears the
`Authorization` header and asserts 200 — is not actually exercising
anonymous-vs-authenticated behavior at all: it would return 200 whether or
not `[AllowAnonymous]` is present, because the mock handler never looks at
the header. So an HTTP-integration test through `HebloWebApplicationFactory`
**cannot** prove or disprove the authorization gate. This mirrors why
`DiagnosticsControllerTests` and the `Anela.Heblo.Tests.Authorization`
suite (`GridLayoutsControllerAuthorizationTests`,
`StockUpOperationsControllerAuthorizationTests`,
`WeatherForecastControllerAuthorizationTests`,
`DashboardControllerAuthorizationTests`) verify gating via **reflection on
attributes**, not live HTTP calls — that's the pattern to follow here
instead of the plan's originally-proposed 401/403-over-HTTP approach.

### 1. Replace the anonymous-access test with a reflection-based authorization test

In `backend/test/Anela.Heblo.Tests/Features/Manufacture/Settings/GetManufactureSettingsEndpointTests.cs`,
remove `GetSettings_ShouldBeReachableAnonymously` (it asserts the vulnerable
behavior and can't be meaningfully repurposed under mock auth) and add a new
test file `backend/test/Anela.Heblo.Tests/Authorization/ManufactureSettingsControllerAuthorizationTests.cs`,
following the `GridLayoutsControllerAuthorizationTests` / `DiagnosticsControllerTests`
shape:

```csharp
using System.Reflection;
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class ManufactureSettingsControllerAuthorizationTests
{
    [Fact]
    public void Controller_IsGatedByFeatureAuthorize()
    {
        var attribute = typeof(ManufactureSettingsController).GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().NotBeNull(
            "the settings endpoint exposes the tenant's Entra ID manufacture-group id " +
            "and must sit behind the same gate as its sibling Manufacture controllers");
        attribute!.Feature.Should().Be(Feature.Manufacture_ManufactureOrders);
    }

    [Fact]
    public void GetSettings_DoesNotAllowAnonymous()
    {
        var method = typeof(ManufactureSettingsController).GetMethod(nameof(ManufactureSettingsController.GetSettings));

        method!.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
    }
}
```

This also gets a free assist from `GateConsistencyTests.EveryGatedEndpoint_HasFeatureAuthorize`
(`backend/test/Anela.Heblo.Tests/Authorization/GateConsistencyTests.cs`): once
`[AllowAnonymous]` is gone and `[FeatureAuthorize]` is present, this
controller stops being an exception and starts being enforced by the
existing repo-wide consistency check — no changes needed to that test.

### 2. Keep the two existing content tests, unchanged

`GetSettings_ShouldReturnSuccessAndCorrectContentType` and
`GetSettings_ShouldExposeManufactureGroupIdField` use `_client` from
`HebloWebApplicationFactory.CreateClient()`, which is authenticated as
`SuperUser` by the mock handler — they continue to pass unmodified after the
gate is added, since `SuperUser` satisfies any `[FeatureAuthorize]` check via
`AccessRoles.SuperUser`'s wildcard path. No changes required to these two
tests.

### 3. No permission-denied HTTP test

Given the mock-auth constraint above, there is no way to construct an
authenticated-but-under-permissioned HTTP client against this factory to
assert 403 end-to-end (the same reason no other `[FeatureAuthorize]`
controller in the codebase has such a test). Coverage for "wrong permission
→ 403" already exists once, generically, at the framework level via
`PermissionAuthorizationResultHandler` and `FeatureAuthorizeAttributeTests`;
it does not need to be re-proven per controller. This matches the plan's
open question and resolves it: skip a per-controller 403 test, with this
justification recorded here instead of left as a TODO in code.

## Verification plan

1. `dotnet build` — confirms the base-class change, using directives, and
   action-signature change compile.
2. `dotnet format` — style conformance.
3. `dotnet test --filter "FullyQualifiedName~ManufactureSettings|FullyQualifiedName~GateConsistencyTests|FullyQualifiedName~AuthorizationIntegrationTests"` —
   confirms:
   - the new/updated authorization tests pass,
   - the two unmodified content tests still pass,
   - `GateConsistencyTests.EveryGatedEndpoint_HasFeatureAuthorize` and
     `EveryMenuPath_FeatureHasController` still pass with this controller now
     included in the gated set.
4. Grep frontend for any other caller of `/api/manufacture/settings` besides
   `useManufactureSettings.ts` to confirm no anonymous frontend path exists
   (static check only, per plan's stated scope).
