All 6 tasks complete. Here is the output summary:

---

# Implementation: Add FluentValidation for GetGroupMembersRequest

## What was implemented

Moved `GetGroupMembersRequest.GroupId` validation from the infrastructure layer into the Application layer using FluentValidation + MediatR `ValidationBehavior`. Added MCP-boundary exception conversion so invalid input surfaces as a structured failure instead of a silent empty result.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/UserManagement/Validators/GetGroupMembersRequestValidator.cs` — New validator using `.Must(!IsNullOrWhiteSpace)` rejecting null/empty/whitespace; message "GroupId is required."
- `backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs` — Added explicit `IValidator<GetGroupMembersRequest>` and `IPipelineBehavior<GetGroupMembersRequest, GetGroupMembersResponse>` DI registrations outside the mock/real if-else branch
- `backend/src/Anela.Heblo.API/MCP/Tools/UserManagementMcpTools.cs` — Added `catch (ValidationException)` around `_mediator.Send` rethrowing as `McpException("[ValidationError] GroupId: GroupId is required.")`
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs` — Deleted `string.IsNullOrWhiteSpace(groupId)` guard + LogWarning + LogInformation "validation passed" from `GetGroupMembersAsync`

## Tests

- `backend/test/Anela.Heblo.Tests/Features/UserManagement/Validators/GetGroupMembersRequestValidatorTests.cs` — 7 unit tests: null, empty, 4 whitespace variants, valid
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GetGroupMembersValidationPipelineTests.cs` — 4 integration tests: 3 invalid GroupId cases assert `ValidationException` before handler runs (MockBehavior.Strict verifies handler never called), 1 valid case confirms handler reached
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GetGroupMembersHandlerTests.cs` — Removed obsolete `Handle_WithEmptyGroupId_CallsGraphService`; 6 tests remain

## How to verify

```bash
cd backend
dotnet build Anela.Heblo.sln
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.UserManagement"
```

All 43 UserManagement tests pass (7 validator unit + 4 pipeline integration + 6 handler + existing GraphService/MockGraphService/ParseMembers/GetAppRoleMembers tests).

## Notes

- Plain `NotEmpty()` does not reject whitespace-only strings in FluentValidation — `.Must(!IsNullOrWhiteSpace)` is the correct approach per spec. The code quality reviewer flagged this as inconsistent with other validators, but it is intentional and required by the spec's whitespace acceptance criterion.
- 38 pre-existing test failures in other modules (KnowledgeBase, Article, Purchase, etc.) are unrelated to this change.
- `GetAppRoleMembersAsync` null guard (lines 266-270) was intentionally left intact — out of scope per spec.

## PR Summary

Adds Application-layer FluentValidation for `GetGroupMembersRequest.GroupId`, closing a silent-failure gap on the MCP entry point. Before this change, calling the MCP tool with an empty `groupId` returned `Success: true, Members: []` — semantically indistinguishable from "group is empty". After this change, invalid input throws `McpException("[ValidationError] GroupId: GroupId is required.")`.

The implementation follows the project's explicit-DI pattern (no `AddValidatorsFromAssembly`), registers both the validator and per-request pipeline behavior in `UserManagementModule`, removes the now-redundant null guard from `GraphService.GetGroupMembersAsync`, and catches `ValidationException` at the MCP tool boundary to produce a structured error rather than an opaque transport failure.

### Changes
- `Validators/GetGroupMembersRequestValidator.cs` — new FluentValidation validator
- `UserManagementModule.cs` — explicit DI registrations for validator + pipeline behavior
- `UserManagementMcpTools.cs` — `catch (ValidationException)` → structured `McpException`
- `GraphService.cs` — deleted redundant null/whitespace guard from `GetGroupMembersAsync`
- `GetGroupMembersRequestValidatorTests.cs` — 7 unit tests (null/empty/whitespace/valid)
- `GetGroupMembersValidationPipelineTests.cs` — 4 integration tests proving pipeline enforces validation
- `GetGroupMembersHandlerTests.cs` — removed obsolete test encoding the old swallow-empty contract

## Status
DONE