# Architecture Review: Remove Unused `groupId` Parameter from `AcquireGraphTokenAsync`

## Skip Design: true

## Architectural Fit Assessment

This change is a minimal, self-contained signature clean-up within a single private method of `GraphService`. It has no external API surface — `AcquireGraphTokenAsync` is `private`, not part of `IGraphService`, and not reflected in any DTO or OpenAPI definition. The fix aligns cleanly with the existing Clean Architecture / Vertical Slice organisation: all affected code lives inside one service file under `Features/UserManagement/Services/`. No module boundaries are crossed, no contracts change, and the behavioral output of the service is identical after the change.

Verification against the file confirms:
- `AcquireGraphTokenAsync` is declared on line 86 as `private async Task<string> AcquireGraphTokenAsync(string groupId, CancellationToken cancellationToken)`.
- `groupId` does not appear anywhere inside the method body (lines 87–101).
- The single call site is on line 119: `var graphToken = await AcquireGraphTokenAsync(groupId, cancellationToken);`.
- `SearchUsersAsync` (line 188) and `GetAppRoleMembersAsync` (line 264) each acquire their tokens by calling `_tokenAcquisition.GetAccessTokenForAppAsync` directly — neither calls `AcquireGraphTokenAsync`. There are therefore exactly zero other call sites to update.

## Proposed Architecture

### Component Overview

```
GraphService (single file)
│
├── AcquireGraphTokenAsync(CancellationToken)   ← AFTER: parameter removed
│
└── GetGroupMembersAsync(string groupId, ...)
        └── calls AcquireGraphTokenAsync(cancellationToken)  ← AFTER: groupId arg dropped
```

No new components. No new dependencies.

### Key Design Decisions

#### Decision 1: Remove the parameter entirely vs. wire it up for logging

**Options considered:**
1. Remove `string groupId` from the signature entirely.
2. Keep the parameter and add a `_logger.LogInformation` call inside `AcquireGraphTokenAsync` so the parameter is actually used.

**Chosen approach:** Remove the parameter entirely (option 1).

**Rationale:** `AcquireGraphTokenAsync` acquires an application-scoped token using `https://graph.microsoft.com/.default`. The token is not group-scoped; passing `groupId` into the method implies a relationship that does not exist. The caller (`GetGroupMembersAsync`) already logs the group ID before and after the token acquisition call — there is no logging gap that justifies retaining the parameter. Option 2 would preserve dead weight and perpetuate the misleading signature merely to satisfy a linter, which is the opposite of what this clean-up intends.

## Implementation Guidance

### Directory / Module Structure

Only one file requires changes:

```
backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs
```

No new files. No directory changes.

### Interfaces and Contracts

`AcquireGraphTokenAsync` is `private` and does not appear in `IGraphService`. No interface files require changes. No DTOs, no OpenAPI definitions, no TypeScript client regeneration needed.

### Data Flow

Before:
```
GetGroupMembersAsync(groupId)
  → AcquireGraphTokenAsync(groupId, cancellationToken)   // groupId silently ignored
      → _tokenAcquisition.GetAccessTokenForAppAsync(scope)
      → return graphToken
```

After:
```
GetGroupMembersAsync(groupId)
  → AcquireGraphTokenAsync(cancellationToken)
      → _tokenAcquisition.GetAccessTokenForAppAsync(scope)
      → return graphToken
```

The token acquisition logic, scope, caching, and all downstream HTTP calls are unchanged.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| A hidden call site exists that was not found by inspection | Low | Run `dotnet build` after the change; any remaining call sites with a `groupId` argument will produce a compile error. |
| The change is accidentally applied to `SearchUsersAsync` or `GetAppRoleMembersAsync`, which inline their own token acquisition | Low | Those methods call `_tokenAcquisition.GetAccessTokenForAppAsync` directly and do not use `AcquireGraphTokenAsync`; they are not touched. |

## Specification Amendments

None. The spec accurately describes the two-line change required and the scope is correctly bounded. The finding that `SearchUsersAsync` and `GetAppRoleMembersAsync` do not call `AcquireGraphTokenAsync` is a confirming detail, not an amendment.

## Prerequisites

None. The change requires no migrations, configuration updates, infrastructure changes, or prerequisite code. It can be implemented and validated with a `dotnet build` immediately.
