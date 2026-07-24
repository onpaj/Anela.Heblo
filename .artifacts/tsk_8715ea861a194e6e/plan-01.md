# Plan: Document `UnauthorizedAccessException` on `IGraphService.GetGroupMembersAsync`

## Summary
`IGraphService.GetGroupMembersAsync`'s XML doc lists two exceptions (`GraphServiceAuthException`, `GraphServiceException`), but the concrete `GraphService` implementation also re-throws `UnauthorizedAccessException` verbatim, and two callers already handle it based on reading the adapter, not the interface. This is a documentation-only fix: add the missing `<exception>` tag so the interface contract matches the real behavior and existing caller assumptions.

## Context
Found by the daily arch-review routine (UserManagement module). The interface is meant to be the contract callers code against. Today a caller (`GetGroupMembersHandler`) and a resolver (`GraphArticleUserResolver`) both handle `UnauthorizedAccessException` propagating out of `GetGroupMembersAsync`, but nothing in the interface signals that this is expected/supported behavior. A future alternate implementation of `IGraphService` (test double, different directory provider) written strictly against the documented contract would omit this behavior and silently break both callers (dead `Forbidden` mapping in the handler; a broken propagation assumption in the resolver). No runtime behavior changes — this is purely closing a documentation gap.

## Functional requirements

**FR-1: Document `UnauthorizedAccessException` on the interface method**
- Add an `<exception cref="UnauthorizedAccessException">` XML doc entry to `IGraphService.GetGroupMembersAsync` in `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs`, alongside the existing `GraphServiceAuthException` and `GraphServiceException` entries.
- Acceptance criteria:
  - The XML doc comment on `GetGroupMembersAsync` lists all three exceptions the concrete adapter is known to throw/propagate: `GraphServiceAuthException`, `GraphServiceException`, `UnauthorizedAccessException`.
  - Wording explains *when* it's thrown (caller lacks permission to read the specified group), consistent with the suggested fix in the finding.
  - No other members of `IGraphService` are touched (this finding is scoped to `GetGroupMembersAsync` only).

## Non-functional requirements
- None beyond correctness of the doc comment — this is not a runtime-behavior change, no performance/security implications.

## Data model
- N/A — no data model changes.

## Interfaces
- `IGraphService.GetGroupMembersAsync` (XML doc only) — no signature change, no new exception type introduced, no behavior change in `GraphService`, `GetGroupMembersHandler`, or `GraphArticleUserResolver`.

## Dependencies and scope
- In scope: `IGraphService.cs` XML doc comment edit only.
- Out of scope:
  - Changing `GraphService.GetGroupMembersAsync`'s actual exception handling/re-throw behavior.
  - Changing `GetGroupMembersHandler` or `GraphArticleUserResolver`.
  - Auditing/documenting exceptions on the other two `IGraphService` methods (`SearchUsersAsync`, `GetAppRoleMembersAsync`) — those swallow exceptions internally and return empty lists rather than propagating, so they're not affected by this finding.
  - Introducing a custom `GraphServiceUnauthorizedException` type or otherwise changing the exception's type — the finding's suggested fix keeps using the standard `UnauthorizedAccessException`, which is what both callers already catch.

## Rough plan
1. Edit `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs`: insert the `<exception cref="UnauthorizedAccessException">` doc block for `GetGroupMembersAsync`, per the suggested fix in the finding.
2. Build the backend (`dotnet build`) and run `dotnet format` to confirm the doc comment doesn't break XML doc generation and formatting stays clean.
3. Run the UserManagement-related backend test suite (or full BE test suite if fast enough) to confirm zero behavior change — expected: no test changes needed since this is doc-only.

## Open questions
- None — the finding is unambiguous and self-contained (single XML doc addition, no design decision required).
