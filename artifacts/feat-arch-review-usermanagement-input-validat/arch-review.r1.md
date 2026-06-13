```markdown
# Architecture Review: FluentValidation for GetGroupMembersRequest

## Skip Design: true

## Architectural Fit Assessment

The feature aligns cleanly with the project's Vertical Slice + MediatR convention. The `Validators/` subfolder under each Application feature is the documented pattern (`docs/architecture/filesystem.md`), and FluentValidation + `ValidationBehavior<TRequest,TResponse>` (note: spelled "Behavior" in code, not "Behaviour" as the spec says) is the project-wide pipeline. UserManagement is the outlier — every comparable module (Bank, Catalog, Authorization, Photobank, Packaging, Smartsupp, etc.) already follows this pattern, so the change is small but corrects a real layering violation.

**Integration points**:
1. New validator in `Application/Features/UserManagement/Validators/`.
2. DI wiring in `UserManagementModule.cs` (validator registration + pipeline behavior registration for this request type).
3. Cleanup of the null-check + warning log in `GraphService.GetGroupMembersAsync`.
4. **Exception conversion at the MCP boundary** — see Decision 2 below. This is the only non-obvious piece of the design and the spec under-specifies it.

## Proposed Architecture

### Component Overview

```
                      ┌────────────────────────────────┐
                      │ UserManagementController (HTTP) │
                      │   [Required] string groupId     │
                      └──────────────┬─────────────────┘
                                     │
┌──────────────────────────┐         │      ┌──────────────────────────────┐
│ UserManagementMcpTools   │         │      │ MediatR pipeline             │
│ (MCP)                    │         ▼      │  1. ValidationBehavior<T,R>  │
│   _mediator.Send(req) ───┼────────────────▶    ↑ throws ValidationExc.   │
│   try { ... }            │                │    on failure                │
│   catch ValidationExc →  │                │  2. GetGroupMembersHandler   │
│      McpException        │                │     → IGraphService          │
└──────────────────────────┘                └──────────────────────────────┘
                                                            │
                                                            ▼
                                            ┌──────────────────────────┐
                                            │ GraphService (no null    │
                                            │   guard for groupId)     │
                                            └──────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Explicit DI registration (NOT assembly scanning)
**Options considered:**
- A) Auto-discover validators via `AddValidatorsFromAssembly` (what the spec implies)
- B) Register the validator and the per-request pipeline behavior explicitly in `UserManagementModule.cs`

**Chosen approach:** B.

**Rationale:** I grepped the entire backend — `AddValidatorsFromAssembly` is **not used anywhere**. Every feature module that has validators registers them explicitly:
```csharp
services.AddScoped<IValidator<TRequest>, TValidator>();
services.AddScoped<IPipelineBehavior<TRequest, TResponse>,
                   ValidationBehavior<TRequest, TResponse>>();
```
(See `BankModule.cs:25-28`, `AuthorizationModule.cs:20-22`, `CatalogModule.cs:127-133`, `PhotobankModule.cs:72-82`.) The `ValidationBehavior` is **not registered as an open generic** anywhere in composition root; it is bound per-request. Skipping either registration is a silent no-op. The spec's "no manual DI registration required if assembly scanning is already in place" is incorrect for this codebase and must be reversed in implementation.

#### Decision 2: Convert `ValidationException` to `McpException` at the MCP tool boundary
**Options considered:**
- A) Catch `ValidationException` in `GetGroupMembersHandler` and return `Success = false, ErrorCode = ValidationError` (won't work — the pipeline behavior throws **before** the handler runs, so the handler's `catch` is unreachable for pipeline-level validation failures).
- B) Add a global `IExceptionHandler` / middleware that maps `ValidationException` → a `BaseResponse`-shaped failure for both HTTP and MCP (not the project's pattern — no global handler exists today; the Bank controller catches it locally).
- C) Catch `ValidationException` in `UserManagementMcpTools.GetGroupMembers` and re-throw as `McpException` with a structured payload, mirroring the existing `if (!response.Success) throw new McpException(...)` pattern.

**Chosen approach:** C.

**Rationale:** This is the smallest change that delivers FR-3's intent ("surface invalid input as a failure, not a silent empty result") and matches the existing MCP tool error-handling style. A pipeline `ValidationException` would otherwise leak out as a generic MCP transport error with no `ErrorCode`. The HTTP path is unaffected — `[Required]` already short-circuits at model binding, so the new validator is defence-in-depth there. The spec hand-waves this as "surfaced naturally by the ValidationBehaviour"; it is not natural and needs explicit code at the MCP boundary.

Encoding choice for the MCP exception message, to mirror the success-path format already in the file:
```csharp
catch (FluentValidation.ValidationException ex)
{
    throw new McpException(
        $"[{ErrorCodes.ValidationError}] {string.Join(" | ",
            ex.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"))}");
}
```

#### Decision 3: Keep `appRoleValue` guard in `GraphService.GetAppRoleMembersAsync` — out of scope
**Chosen approach:** Touch only `GetGroupMembersAsync` lines 119-124. Leave the analogous null-check in `GetAppRoleMembersAsync` (lines 275-279) alone. The brief and spec are explicitly scoped to `GetGroupMembersRequest`; widening the surgery now invites unrelated regressions. Flag as follow-up (see Specification Amendments).

## Implementation Guidance

### Directory / Module Structure

Create:
```
backend/src/Anela.Heblo.Application/Features/UserManagement/
└── Validators/
    └── GetGroupMembersRequestValidator.cs
```

Modify:
```
backend/src/Anela.Heblo.Application/Features/UserManagement/
├── Services/GraphService.cs                      # remove lines 119-124
└── UserManagementModule.cs                       # add validator + behavior DI

backend/src/Anela.Heblo.API/MCP/Tools/
└── UserManagementMcpTools.cs                     # catch ValidationException
```

Test files affected (see Risks for the critical one):
```
backend/test/Anela.Heblo.Tests/Features/UserManagement/
├── GetGroupMembersHandlerTests.cs                # delete/rename Handle_WithEmptyGroupId_CallsGraphService
└── Validators/GetGroupMembersRequestValidatorTests.cs   # new
```
There is no existing `GraphService.GetGroupMembersAsync` test that asserts the null-check branch returns `[]` — the current `GraphServiceTests.cs` covers the HTTP-success path only — so no existing GraphService test needs to be updated. (The misplaced **handler** test `Handle_WithEmptyGroupId_CallsGraphService` does need attention; see Risks.)

### Interfaces and Contracts

`GetGroupMembersRequestValidator`:
```csharp
public class GetGroupMembersRequestValidator : AbstractValidator<GetGroupMembersRequest>
{
    public GetGroupMembersRequestValidator()
    {
        RuleFor(x => x.GroupId)
            .NotEmpty()
            .WithMessage("GroupId is required.");
    }
}
```

DI in `UserManagementModule.AddUserManagement` (add **outside** the mock/real branch — validation applies to both):
```csharp
services.AddScoped<IValidator<GetGroupMembersRequest>, GetGroupMembersRequestValidator>();
services.AddScoped<
    IPipelineBehavior<GetGroupMembersRequest, GetGroupMembersResponse>,
    ValidationBehavior<GetGroupMembersRequest, GetGroupMembersResponse>>();
```

Required `using` additions to `UserManagementModule.cs`: `FluentValidation`, `MediatR`, `Anela.Heblo.Application.Common.Behaviors`, `Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers`, `Anela.Heblo.Application.Features.UserManagement.Validators`.

MCP tool wrapping (the `GetGroupMembers` method already has no top-level try/catch; add one only around `_mediator.Send`):

```csharp
GetGroupMembersResponse response;
try
{
    response = await _mediator.Send(request, cancellationToken);
}
catch (FluentValidation.ValidationException ex)
{
    throw new McpException(
        $"[{ErrorCodes.ValidationError}] {string.Join(" | ",
            ex.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"))}");
}
```

### Data Flow

**HTTP path (unchanged behaviour for valid + invalid inputs):**
`GET /api/UserManagement/group-members?groupId=...`
→ `[Required]` model binding (rejects null/empty at HTTP-400 before MediatR)
→ `_mediator.Send` → `ValidationBehavior` (no-op for valid input)
→ `GetGroupMembersHandler` → `IGraphService.GetGroupMembersAsync`
→ `HandleResponse` → 200/Ok with `GetGroupMembersResponse`.

**MCP path, valid `groupId` (unchanged):**
Tool call → `_mediator.Send` → `ValidationBehavior` passes → handler → service → JSON-serialized success response.

**MCP path, invalid `groupId` (new behaviour):**
Tool call → `_mediator.Send` → `ValidationBehavior` throws `FluentValidation.ValidationException`
→ caught in `UserManagementMcpTools.GetGroupMembers`
→ re-thrown as `McpException("[ValidationError] GroupId: GroupId is required.")`
→ MCP client receives a structured tool error rather than `Success: true, Members: []`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Forgetting either DI registration silently disables the validator — pipeline behaviors are per-request, not open generic. | HIGH | Both `AddScoped<IValidator<...>>` and `AddScoped<IPipelineBehavior<...>>` must land in the same PR; add a focused integration test that resolves `IMediator` from a real DI container and asserts `ValidationException` for empty `GroupId`. |
| The existing handler test `GetGroupMembersHandlerTests.Handle_WithEmptyGroupId_CallsGraphService` (line 72) asserts the *current* swallow-empty behaviour. Left in place, it becomes misleading — it still passes because it bypasses MediatR by calling the handler directly, hiding the fact that real callers can never reach the handler with an empty `GroupId` anymore. | HIGH | Replace it with (a) a unit test on `GetGroupMembersRequestValidator` and (b) an integration test on the MediatR pipeline. Do not just delete it — explicitly invert it to document the new contract. |
| `MockGraphService` may carry the same null-check pattern; if so, removing it from `GraphService` only is asymmetric. | LOW | Quick check of `MockGraphService.GetGroupMembersAsync` during implementation; if it also guards null/empty, remove there too for parity. Out of scope only if `MockGraphService` does not have the guard. |
| Pipeline `ValidationException` propagating uncaught from the HTTP controller would surface as a 500 (no global exception handler exists). This would never happen because `[Required]` short-circuits first — but if a developer ever removes `[Required]`, the regression is silent. | LOW | Don't add a try/catch in the controller (out of scope, and `[Required]` makes it dead code today). Note this dependency in the spec amendment so future controller refactors are aware. |
| The validator's `NotEmpty()` only rejects null and empty; whitespace-only strings (e.g. `"   "`) pass `NotEmpty` but fail `string.IsNullOrWhiteSpace`. Today's `GraphService` guard catches whitespace; the new validator does not. | MEDIUM | Add `.Must(s => !string.IsNullOrWhiteSpace(s))` or use `NotEmpty().Matches(@"\S")`. The spec's acceptance criterion says "null, empty, or whitespace", so the validator must reject all three. Stock `NotEmpty()` alone does not. |

## Specification Amendments

1. **FR-1 acceptance criterion — wrong DI assumption.** Replace:
   > "The validator is automatically discovered and executed by the existing `ValidationBehaviour` MediatR pipeline (no manual DI registration required if assembly scanning is already in place — confirm in implementation)."

   with:
   > "Register both the validator (`IValidator<GetGroupMembersRequest>`) and the request-specific pipeline behavior (`IPipelineBehavior<GetGroupMembersRequest, GetGroupMembersResponse>` → `ValidationBehavior<...>`) explicitly in `UserManagementModule.AddUserManagement`. The project does **not** use `AddValidatorsFromAssembly`; pipeline behaviors are bound per request type, not as open generics."

2. **FR-1 — whitespace rejection.** `RuleFor(x => x.GroupId).NotEmpty()` does not reject whitespace-only strings. Spec must require either `.Must(s => !string.IsNullOrWhiteSpace(s))` or an equivalent rule so the validator actually matches its stated acceptance criterion ("null, empty, or whitespace").

3. **FR-3 — exception conversion is not automatic.** The spec says the failure response is "surfaced naturally by the ValidationBehaviour". It is not. A `FluentValidation.ValidationException` thrown by the pipeline propagates out of `_mediator.Send`; the handler's existing `catch` blocks do not see it (it is thrown before the handler runs). Add to the spec:
   > "In `UserManagementMcpTools.GetGroupMembers`, catch `FluentValidation.ValidationException` and re-throw as `McpException` with `ErrorCode = ValidationError` plus a serialized list of `(PropertyName, ErrorMessage)` pairs. The HTTP path is unaffected because `[Required]` short-circuits at model binding."

4. **NFR-4 — update test scope.** Add:
   > "`GetGroupMembersHandlerTests.Handle_WithEmptyGroupId_CallsGraphService` (line 72) encodes the obsolete contract and must be replaced with (a) `GetGroupMembersRequestValidatorTests` covering null/empty/whitespace rejection and valid acceptance, and (b) a MediatR-pipeline integration test that resolves `IMediator` from a configured `ServiceCollection` and asserts a `ValidationException` for an empty `GroupId`."

5. **Note on naming.** The pipeline class is `ValidationBehavior` (US spelling) throughout the codebase; the spec uses `ValidationBehaviour` (UK spelling). Cosmetic but worth aligning in the spec to avoid confusion during code review.

6. **Follow-up flag (informational, not in scope).** `GraphService.GetAppRoleMembersAsync` carries the same anti-pattern at lines 275-279. A separate brief should add `GetAppRoleMembersRequestValidator` (if/when that use case exists) and remove the guard. Do not bundle it into this PR.

## Prerequisites

None. All required infrastructure is in place:

- **FluentValidation** package is already referenced through other features.
- `ValidationBehavior<TRequest, TResponse>` exists at `backend/src/Anela.Heblo.Application/Common/Behaviors/ValidationBehavior.cs` and is unchanged by this work.
- `UserManagementModule.AddUserManagement` is already wired into `ApplicationModule.cs:91`.
- `ErrorCodes.ValidationError` (0001) already exists with `[HttpStatusCode(HttpStatusCode.BadRequest)]`.

No migrations, config, infrastructure, secrets, or feature-flag changes are required.
```