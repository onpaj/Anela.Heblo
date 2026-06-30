# Specification: Remove Unused `groupId` Parameter from `AcquireGraphTokenAsync`

## Summary
`GraphService.AcquireGraphTokenAsync` carries a `string groupId` parameter that is never referenced in the method body, misleading readers into believing group identity influences token acquisition. This change removes the dead parameter and updates the single call site to eliminate the signature noise.

## Background
During a routine architecture review on 2026-06-22, `GraphService.AcquireGraphTokenAsync` (located at `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs`, line 86) was found to accept a `groupId` argument that is passed into the method but never used — not in the token acquisition call, not in any log statement, and not in any conditional logic. The parameter was presumably added for future logging context but was never wired up. Leaving it in place inflates the call-site signature and creates a false impression that token acquisition is group-scoped, which it is not.

## Functional Requirements

### FR-1: Remove `groupId` parameter from `AcquireGraphTokenAsync`
Remove `string groupId` from the private method signature so the method accepts only `CancellationToken cancellationToken`.

**Acceptance criteria:**
- The method signature reads `private async Task<string> AcquireGraphTokenAsync(CancellationToken cancellationToken)`.
- No reference to `groupId` remains inside the method body.
- The project compiles without errors or warnings introduced by this change.

### FR-2: Update the call site
Update the single internal call site (line 119 at time of filing) to omit the `groupId` argument.

**Acceptance criteria:**
- The call site reads `var graphToken = await AcquireGraphTokenAsync(cancellationToken);`.
- No other call sites exist (confirmed by search); if additional call sites are discovered they must also be updated.
- The project compiles without errors or warnings introduced by this change.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected. This is a pure signature clean-up with no behavioral change.

### NFR-2: Security
No security impact. Token acquisition scope (`https://graph.microsoft.com/.default`) and acquisition logic are unchanged. The `groupId` value was never used in the token request, so removing it does not alter what token is returned or how it is used.

### NFR-3: Maintainability
After the change, `AcquireGraphTokenAsync` should have a minimal, accurate signature that reflects only the inputs it actually uses, reducing cognitive overhead for future readers.

## Data Model
Not applicable. No data model changes.

## API / Interface Design
`AcquireGraphTokenAsync` is a private method; it has no external API surface. No public contracts, DTOs, or OpenAPI definitions are affected.

## Dependencies
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs` — the file containing both the method definition (line 86) and the call site (line 119 at time of filing).
- No external services, libraries, or other features are affected.

## Out of Scope
- Adding `groupId`-based logging or logic to `AcquireGraphTokenAsync`. If group-aware logging is desired, that is a separate feature.
- Any refactoring of `GraphService` beyond the two-line signature change described here.
- Changes to token acquisition scope or caching behavior.

## Open Questions
None.

## Status: COMPLETE
