# Design: DepartmentsController — route through MediatR with a proper DTO

No user interface is introduced or changed by this design (existing consumer components keep the same `Department[]` shape and behavior); the UX/UI section is omitted.

## Component design

Three new files in `Anela.Heblo.Application`, one rewritten controller in `Anela.Heblo.API`, and matching frontend updates. All new types follow the `GetGroupMembers` use case in the same module (`Application/Features/UserManagement/UseCases/GetGroupMembers/`) verbatim — same folder shape, same base classes, same error-handling shape — so the module stays internally consistent.

### `DepartmentDto` (contract)
`Anela.Heblo.Application/Features/UserManagement/Contracts/DepartmentDto.cs`

Responsibility: wire-shape for a department, decoupled from the `InvoiceClassification` domain entity. Plain class (project rule: DTOs are never records), two `string` properties, no domain `using`.

```csharp
namespace Anela.Heblo.Application.Features.UserManagement.Contracts;

public class DepartmentDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
}
```

### `GetDepartmentsRequest` / `GetDepartmentsResponse` / `GetDepartmentsHandler` (use case)
`Anela.Heblo.Application/Features/UserManagement/UseCases/GetDepartments/`

- **`GetDepartmentsRequest : IRequest<GetDepartmentsResponse>`** — no properties (no input to validate; FR-5 in the plan already rules out a validator).
- **`GetDepartmentsResponse : BaseResponse`** — `public List<DepartmentDto> Departments { get; set; } = new();`
- **`GetDepartmentsHandler : IRequestHandler<GetDepartmentsRequest, GetDepartmentsResponse>`** — sole collaborator is `IDepartmentClient` (constructor-injected, resolved from `Domain.Features.InvoiceClassification`, already registered in DI by the Flexi adapter — no new registration needed). Responsibility: call `GetDepartmentsAsync`, map each domain `Department` to a `DepartmentDto`, wrap in a success/failure `BaseResponse`. On any exception, catch and return `ErrorCode = ErrorCodes.InternalServerError` with an empty list — mirrors `GetGroupMembersHandler`'s generic `catch (Exception ex)` branch. `IDepartmentClient` exposes no more specific exception types than the generic case (no `*AuthException`/`*ServiceException` analogues exist for this client), so only the one catch-all branch is needed — narrower branches like `GetGroupMembersHandler`'s `GraphServiceAuthException`/`GraphServiceException`/`UnauthorizedAccessException` don't apply here.
- MediatR auto-discovers the handler via assembly scan (`ApplicationModule.cs:64`, `AddMediatR(cfg => cfg.RegisterServicesFromAssembly(...))`) — no explicit DI registration required, consistent with every other module (`CatalogModule.cs`, `OrgChartModule.cs`, etc. all note "MediatR handlers are automatically registered by AddMediatR scan").

```csharp
// GetDepartmentsRequest.cs
using MediatR;
namespace Anela.Heblo.Application.Features.UserManagement.UseCases.GetDepartments;
public class GetDepartmentsRequest : IRequest<GetDepartmentsResponse> { }

// GetDepartmentsResponse.cs
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Shared;
namespace Anela.Heblo.Application.Features.UserManagement.UseCases.GetDepartments;
public class GetDepartmentsResponse : BaseResponse
{
    public List<DepartmentDto> Departments { get; set; } = new();
}

// GetDepartmentsHandler.cs
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using MediatR;
using Microsoft.Extensions.Logging;
namespace Anela.Heblo.Application.Features.UserManagement.UseCases.GetDepartments;

public class GetDepartmentsHandler : IRequestHandler<GetDepartmentsRequest, GetDepartmentsResponse>
{
    private readonly IDepartmentClient _departmentClient;
    private readonly ILogger<GetDepartmentsHandler> _logger;

    public GetDepartmentsHandler(IDepartmentClient departmentClient, ILogger<GetDepartmentsHandler> logger)
    {
        _departmentClient = departmentClient;
        _logger = logger;
    }

    public async Task<GetDepartmentsResponse> Handle(GetDepartmentsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var departments = await _departmentClient.GetDepartmentsAsync(cancellationToken);

            return new GetDepartmentsResponse
            {
                Success = true,
                Departments = departments.Select(d => new DepartmentDto { Id = d.Id, Name = d.Name }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle GetDepartments");

            return new GetDepartmentsResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.InternalServerError,
                Departments = new List<DepartmentDto>()
            };
        }
    }
}
```

### `DepartmentsController` (rewritten)
`Anela.Heblo.API/Controllers/DepartmentsController.cs`

Responsibility narrows to: accept the HTTP request, dispatch `GetDepartmentsRequest` via `IMediator`, translate the `BaseResponse` into the appropriate HTTP status via the inherited `HandleResponse<T>`. No business logic, no domain-namespace reference.

```csharp
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetDepartments;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DepartmentsController : BaseApiController
{
    private readonly IMediator _mediator;

    public DepartmentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<GetDepartmentsResponse>> GetDepartments(CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetDepartmentsRequest(), cancellationToken);
        return HandleResponse(response);
    }
}
```

No `[FeatureAuthorize]` is added — out of scope per the plan, and the controller has none today (unlike `UserManagementController`, which the sibling pattern otherwise mirrors).

### Frontend: `useDepartments.ts`
`frontend/src/api/hooks/useDepartments.ts`

Responsibility unchanged from the caller's point of view (`Department[]`); only the internal unwrap step changes. The hook keeps its manual `fetch` (it already bypasses the generated client's `departments_GetDepartments()`, and continues to do so — the generated client's method also changes shape but nothing calls it directly). It now expects the `BaseResponse`-derived envelope and unwraps `.departments`:

```ts
queryFn: async (): Promise<Department[]> => {
  const apiClient = getAuthenticatedApiClient();
  const relativeUrl = '/api/departments';
  const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' }
  });

  if (!response.ok) {
    throw new Error(`Failed to fetch departments: ${response.statusText}`);
  }

  const body = await response.json();
  return body.departments;
},
```

Downstream consumers (`FinancialFilters.tsx`, `RuleForm.tsx`, `RulesList.tsx`, `FinancialOverview.tsx`) are untouched — they only see the hook's `Department[]` return type, which is unchanged.

### Generated OpenAPI client
`frontend/src/api/generated/api-client.ts` is regenerated by the standard build-time codegen step (per `docs/development/api-client-generation.md`). `departments_GetDepartments()` changes from `Promise<Department[]>` to `Promise<GetDepartmentsResponse>` (a new generated class with `departments: Department[]`, `success: boolean`, `errorCode?: ...`, mirroring the existing generated `GetFinancialOverviewResponse`/`GetGroupMembersResponse`-style envelope classes). This regeneration is mechanical and not hand-edited; no other file references `departments_GetDepartments()` today (confirmed: `useDepartments.ts` uses a manual `fetch`, not the generated client method), so no other call site needs updating.

## Data schemas

### Request
`GET /api/Departments` — no query parameters, no body. Route and verb unchanged.

### Response (new envelope, replacing the raw array)

```jsonc
// 200 OK
{
  "success": true,
  "errorCode": null,
  "params": null,
  "departments": [
    { "id": "1", "name": "Sales" },
    { "id": "2", "name": "Warehouse" }
  ]
}

// 500 Internal Server Error (IDepartmentClient threw)
{
  "success": false,
  "errorCode": "InternalServerError",
  "params": null,
  "departments": []
}
```

Previously the endpoint returned a bare JSON array (`[{ "id": "1", "name": "Sales" }, ...]`) with no error path — any exception in `IDepartmentClient` surfaced as an unmapped ASP.NET Core 500 with no JSON body in the standard shape. The new envelope is a breaking wire change but is absorbed entirely inside `useDepartments.ts` (FR-4); no other consumer talks to this endpoint directly.

### C# types

| Type | Kind | Shape |
|---|---|---|
| `DepartmentDto` | contract class | `{ Id: string, Name: string }` |
| `GetDepartmentsRequest` | MediatR request | `{}` (marker, no fields) |
| `GetDepartmentsResponse` | MediatR response, `: BaseResponse` | `{ Success: bool, ErrorCode: ErrorCodes?, Params: Dictionary<string,string>?, Departments: List<DepartmentDto> }` |

No database schema changes; `IDepartmentClient` and domain `Department` are untouched, per plan scope.

## Notes carried over from the plan, unchanged
- Module ownership: new files live in module 35's existing `UserManagement/UseCases/` and `UserManagement/Contracts/` folders (matches `GetGroupMembers`/`UserDto`), even though `IDepartmentClient`/`Department` remain in the `InvoiceClassification` domain namespace — that cross-module domain ownership question stays explicitly out of scope.
- `GetDepartmentByIdAsync` is not given a use case — no controller calls it.
- No `[FeatureAuthorize]` added to `DepartmentsController`.
