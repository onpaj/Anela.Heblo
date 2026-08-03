# Design — Gate `DepartmentsController` with `[FeatureAuthorize]`

No UI section: this change is backend-only. The two frontend consumers (`FinancialOverview`, `InvoiceClassification`) are already authenticated and feature-gated; no frontend code changes.

## Component design

### 1. `DepartmentsController` (`backend/src/Anela.Heblo.API/Controllers/DepartmentsController.cs`)

Single change: add a class-level `FeatureAuthorizeAttribute` using its existing OR-semantics `params Feature[]` constructor, plus the corresponding `using`. No other member changes.

```csharp
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetDepartments;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[FeatureAuthorize(Feature.Finance_FinancialOverview, Feature.Purchase_InvoiceClassification)]
public class DepartmentsController : BaseApiController
{
    private readonly IMediator _mediator;

    public DepartmentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [ProducesResponseType(typeof(GetDepartmentsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GetDepartmentsResponse>> GetDepartments(CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetDepartmentsRequest(), cancellationToken);
        return HandleResponse(response);
    }
}
```

Notes on the mechanism (verified against `FeatureAuthorizeAttribute.cs` and `AccessRoles.generated.cs`):
- The `params Feature[]` constructor sets `Feature = features[0]` (`Feature.Finance_FinancialOverview`, order-dependent — first arg wins for the `.Feature` property) and `Roles = string.Join(",", ...)` = `"finance.financial_overview.read,purchase.invoice_classification.read"`.
- `AuthorizeAttribute.Roles` is interpreted by ASP.NET Core's role-check as OR: the caller passes if they hold **any** listed role. This is the exact mechanism `RecurringJobsController.GetRecurringJob` already relies on (`FeatureAuthorize(Feature.Jobs_Trigger, Feature.Jobs_Disable, Feature.Admin_Administration)`), so no new authorization infrastructure is needed.
- Both `AccessRoles.FinanceFinancialOverviewRead` (`finance.financial_overview.read`) and `AccessRoles.PurchaseInvoiceClassificationRead` (`purchase.invoice_classification.read`) already exist in the generated role table — no codegen/access-matrix change required.
- Added `[ProducesResponseType(StatusCodes.Status401Unauthorized)]` for OpenAPI accuracy, matching the pattern already used on `RecurringJobsController` actions — purely a Swagger annotation, no behavioral effect. (Optional; drop if it's judged out of scope by the implementer — either way is fine since it doesn't change runtime behavior.)

No changes to `GetDepartmentsHandler`, `IDepartmentQueryService`, `FlexiDepartmentQueryService`, `GetDepartmentsRequest`/`Response`, or `DepartmentDto`.

### 2. New test: `DepartmentsControllerAuthorizationTests.cs`

Path: `backend/test/Anela.Heblo.Tests/Authorization/DepartmentsControllerAuthorizationTests.cs`.

Mirrors `ManufactureSettingsControllerAuthorizationTests.cs` structurally, but — per plan FR-2's explicit caution — asserts on `attribute.Roles` rather than `attribute.Feature`, because the multi-feature constructor collapses `.Feature` to only the first argument (`Finance_FinancialOverview`), which would make a `.Feature.Should().Be(...)` assertion pass without actually proving the second role is covered.

```csharp
using System.Reflection;
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class DepartmentsControllerAuthorizationTests
{
    [Fact]
    public void Controller_IsGatedByFeatureAuthorize()
    {
        var attribute = typeof(DepartmentsController).GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().NotBeNull(
            "the department list exposes internal FlexiBee accounting structure and " +
            "must not be reachable without authentication");
        attribute!.Roles.Should().Contain(AccessRoles.FinanceFinancialOverviewRead);
        attribute.Roles.Should().Contain(AccessRoles.PurchaseInvoiceClassificationRead);
    }

    [Fact]
    public void GetDepartments_DoesNotAllowAnonymous()
    {
        var method = typeof(DepartmentsController).GetMethod(nameof(DepartmentsController.GetDepartments));

        method!.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
    }
}
```

This asserts the *presence and coverage* of the gate, not the OR-vs-AND wiring itself (that's a framework/attribute-plumbing concern already covered elsewhere — `RecurringJobsController`'s precedent — not this controller's job to re-verify). It fails against the current (attribute-less) controller and passes once FR-1 is applied, satisfying the plan's acceptance criteria.

### 3. Existing files — untouched

- `backend/test/Anela.Heblo.Tests/Controllers/DepartmentsControllerTests.cs`: constructs the controller directly and invokes the action in-process, bypassing ASP.NET Core's authorization middleware — unaffected by the attribute, left as-is (per plan FR-3).
- `frontend/.../useDepartments.ts` and its consumers (`FinancialOverview`, `InvoiceClassification`): both already run under sessions holding the target features, so no change needed and none is made.
- `AuthenticationExtensions.cs` (`FallbackPolicy`): explicitly out of scope per the plan — not touched.

## Data schemas

No schema changes. `GetDepartmentsRequest` (empty), `GetDepartmentsResponse { Departments: DepartmentDto[] }`, `DepartmentDto { Id, Name }` are unaffected — this is a pure authorization-metadata change with no effect on wire shape. No OpenAPI-client regeneration is triggered by attribute changes to `[Authorize]`-family attributes (route, verbs, and DTOs are unchanged), so no frontend `api-client` regen step is required.

## Interfaces (behavioral contract)

| Caller | Before | After |
|---|---|---|
| Unauthenticated | `200 OK` with department list | `401 Unauthorized` |
| Authenticated, no `Finance_FinancialOverview` or `Purchase_InvoiceClassification` | `200 OK` | `403 Forbidden` |
| Authenticated, holds either feature (Read) | `200 OK` | `200 OK` (unchanged) |

Route, verb (`GET /api/departments`), and response body shape are unchanged in the success case.

## Verification plan

1. `dotnet build` — confirm the new `using Anela.Heblo.Domain.Features.Authorization;` resolves and the attribute application compiles.
2. Run `backend/test/Anela.Heblo.Tests` — specifically the new `Authorization/DepartmentsControllerAuthorizationTests.cs` (both facts pass) plus the existing `Controllers/DepartmentsControllerTests.cs` (unaffected — in-process invocation bypasses middleware) and the broader `Authorization` folder (no regressions in sibling tests).
3. `dotnet format` — repo validation requirement.
4. `git diff --stat` — confirm only `DepartmentsController.cs` and the new test file changed; no frontend files touched.

This matches the plan's rough-plan steps 1–5 with no deviation; the design step adds no new components beyond what FR-1/FR-2 specify.
