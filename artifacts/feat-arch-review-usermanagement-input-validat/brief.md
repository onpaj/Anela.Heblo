## Module
UserManagement

## Finding
`GetGroupMembersRequest` has no FluentValidation validator in the Application layer. The only place that validates the `groupId` input is inside `GraphService.GetGroupMembersAsync` (`backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs:52-56`):

```csharp
if (string.IsNullOrWhiteSpace(groupId))
{
    _logger.LogWarning("GroupId is null or empty for MS Entra group lookup");
    return new List<UserDto>();
}
```

The controller (`backend/src/Anela.Heblo.API/Controllers/UserManagementController.cs:29`) applies `[Required]` on the query parameter, which validates at the HTTP binding layer. But:

1. The `UserManagementMcpTools.GetGroupMembers` (`backend/src/Anela.Heblo.API/MCP/Tools/UserManagementMcpTools.cs:24-39`) calls `_mediator.Send(request, ...)` directly, bypassing the controller entirely. An empty or whitespace `groupId` reaches the handler with no rejection.
2. Because `GraphService` silently returns `[]` for an invalid `groupId`, the handler returns `Success = true, Members = []` — an incorrect success response for invalid input.
3. The filesystem spec (`docs/architecture/filesystem.md`) shows `Validators/` as a standard subfolder under Application features, and `development_guidelines.md` lists FluentValidation as the project's validation tool. The pattern is established; this module skips it.

The core violation is SRP: an infrastructure service (`GraphService`) is making a domain decision ("is this groupId acceptable?") that belongs in the Application layer.

## Why it matters
- **Layer boundary**: input validation is an Application-layer concern. Pushing it into an infrastructure service means the Application layer has no enforceable contract on what inputs reach it.
- **MCP path silent failure**: the MCP tool is the only non-HTTP entry point today. It will return `Success: true, Members: []` for an empty `groupId`, which an MCP client will interpret as "the group has no members" rather than "the request was invalid."
- **Related to #2629**: fixing #2629 (let `GraphService` propagate errors) would make the null-groupId path return `Success: false` via the handler's catch — but only accidentally. A validator is the correct primary defence.

## Suggested fix
Add `backend/src/Anela.Heblo.Application/Features/UserManagement/Validators/GetGroupMembersRequestValidator.cs`:

```csharp
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
using FluentValidation;

namespace Anela.Heblo.Application.Features.UserManagement.Validators;

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

Register via the existing `ValidationBehaviour` pipeline (already wired project-wide). Then remove the redundant null-check from `GraphService.GetGroupMembersAsync` lines 52-56. No changes to `IGraphService` or the controller.

---
_Filed by daily arch-review routine on 2026-06-06._