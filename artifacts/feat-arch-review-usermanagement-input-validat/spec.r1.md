# Specification: Add FluentValidation for GetGroupMembersRequest

## Summary
Add a FluentValidation validator for `GetGroupMembersRequest` in the UserManagement Application layer, replacing infrastructure-layer null-checks in `GraphService` with a proper Application-layer contract. This closes a silent-failure gap on the MCP entry point and aligns the module with the project's established validation pattern.

## Background
The `GetGroupMembersRequest` use case currently has no FluentValidation validator. Input validation is performed in two places that both fail to enforce the Application-layer contract:

1. **Controller layer** (`UserManagementController.cs:29`) — `[Required]` attribute on the query parameter validates at the HTTP binding layer only.
2. **Infrastructure layer** (`GraphService.GetGroupMembersAsync` lines 52-56) — silently returns an empty list when `groupId` is null or whitespace.

The MCP tool `UserManagementMcpTools.GetGroupMembers` (`UserManagementMcpTools.cs:24-39`) calls `_mediator.Send(request, ...)` directly, bypassing the controller. An invalid `groupId` reaches the handler unvalidated, and because `GraphService` swallows the invalid input, the response is `Success = true, Members = []` — semantically indistinguishable from "the group exists but is empty."

This violates two project conventions:
- **SRP / layer boundaries**: input validation is an Application-layer responsibility; an infrastructure service should not make domain decisions about acceptable inputs (`docs/architecture/development_guidelines.md`).
- **Established structure**: `docs/architecture/filesystem.md` lists `Validators/` as a standard subfolder under Application features, and FluentValidation is the project-wide validation tool wired through `ValidationBehaviour`. Other modules use the pattern; UserManagement skips it.

## Functional Requirements

### FR-1: Add `GetGroupMembersRequestValidator`
Create a FluentValidation validator for `GetGroupMembersRequest` in the Application layer. The validator must reject requests where `GroupId` is null, empty, or whitespace.

**Location:** `backend/src/Anela.Heblo.Application/Features/UserManagement/Validators/GetGroupMembersRequestValidator.cs`

**Acceptance criteria:**
- File exists at the path above and declares `GetGroupMembersRequestValidator : AbstractValidator<GetGroupMembersRequest>`.
- `RuleFor(x => x.GroupId).NotEmpty()` is configured with the message `"GroupId is required."`.
- The validator is automatically discovered and executed by the existing `ValidationBehaviour` MediatR pipeline (no manual DI registration required if assembly scanning is already in place — confirm in implementation).
- Sending a `GetGroupMembersRequest` with a null, empty, or whitespace `GroupId` through `IMediator.Send` results in a `ValidationException` (or the project's standard validation failure response) before the handler executes.

### FR-2: Remove redundant infrastructure-layer null-check
Remove the null/whitespace guard from `GraphService.GetGroupMembersAsync` now that the Application layer enforces the contract.

**Location:** `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs:52-56`

**Acceptance criteria:**
- The block checking `string.IsNullOrWhiteSpace(groupId)` and returning `new List<UserDto>()` is deleted.
- The accompanying `_logger.LogWarning("GroupId is null or empty for MS Entra group lookup")` is deleted.
- `GraphService.GetGroupMembersAsync` is unchanged in all other respects.
- No changes to `IGraphService`, the controller, or the MCP tool.

### FR-3: Correct error response on invalid input via MCP path
With FR-1 in place, the MCP tool path must surface invalid input as a failure, not a silent empty result.

**Acceptance criteria:**
- Calling `UserManagementMcpTools.GetGroupMembers` with an empty or whitespace `groupId` causes the MediatR pipeline to throw a validation exception that the existing handler / exception infrastructure converts into a `Success = false` response (mechanism depends on existing project conventions — surfaced naturally by the `ValidationBehaviour`).
- A valid `groupId` continues to return the existing successful response shape unchanged.
- Controller path (HTTP) behavior is unchanged for callers that already pass `[Required]` validation at the binding layer; for any controller path that bypasses model binding validation, the new validator still applies at the MediatR layer.

## Non-Functional Requirements

### NFR-1: Performance
Negligible. FluentValidation runs in-process on a single string property before any I/O. No measurable impact on the existing Graph API call latency.

### NFR-2: Security
No new attack surface. Adds defence-in-depth: malformed input is rejected earlier in the pipeline rather than reaching the infrastructure layer.

### NFR-3: Maintainability
- The validator is colocated with peer Application-layer validators per `docs/architecture/filesystem.md`.
- The infrastructure layer no longer makes domain decisions about input acceptability, satisfying SRP and clarifying layer responsibilities.

### NFR-4: Testing
Unit tests must cover the validator and the updated service behavior:
- Validator unit tests: rejects null, empty, whitespace `GroupId`; accepts non-empty `GroupId`.
- Existing `GraphService` tests must be updated to remove any expectations of the deleted null-check branch.
- Project test-coverage minimum (80%) applies to new and modified code.

## Data Model
No data model changes. The existing `GetGroupMembersRequest` and `GetGroupMembersResponse` contracts are unchanged.

## API / Interface Design
No public API surface changes. Both entry points — the HTTP controller and the MCP tool — retain their existing signatures and return shapes.

Behavior change on the MCP path only:
- **Before:** invalid `groupId` → `200/Success = true, Members = []`
- **After:** invalid `groupId` → validation failure response per project convention (`Success = false` with validation error details)

## Dependencies
- **FluentValidation** — already in the project, registered via `ValidationBehaviour` MediatR pipeline.
- **Existing `ValidationBehaviour` pipeline** — must auto-pick up validators from the Application assembly (confirm scanning configuration during implementation; this is the project's existing pattern per the brief).
- **No new packages** required.

Related work (not a blocking dependency):
- Issue #2629 (let `GraphService` propagate errors) would change the failure-path response shape for other invalid inputs. This spec is independent and does not require #2629 to ship first; if #2629 lands, the validator remains the primary defence.

## Out of Scope
- Validators for other UserManagement use cases beyond `GetGroupMembersRequest`. Each requires its own brief/spec.
- Changes to `IGraphService`, the controller signature, or the MCP tool signature.
- Format/syntactic validation of `groupId` beyond non-empty (e.g., GUID-format check). MS Entra accepts multiple identifier forms; restricting format risks rejecting valid inputs.
- Refactoring `GraphService` error handling broadly — covered by #2629.
- Localization of the validation message.

## Open Questions
None.

## Status: COMPLETE