# Architecture Assessment: Document `UnauthorizedAccessException` on `IGraphService.GetGroupMembersAsync`

## Verdict

Approved as scoped. This is a documentation-only change to an XML doc comment on one interface method. No component boundaries, contracts, DTOs, or runtime behavior are touched. The plan and design produced in the previous steps are correct and require no revision — this assessment confirms the architecture is sound and adds the guardrails an implementer should hold to.

## Alignment with existing patterns

- `IGraphService` (`backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs`) already uses `<exception cref="...">` XML doc tags as its documented-contract mechanism for `GraphServiceAuthException` and `GraphServiceException`. Adding a third `<exception>` tag for `UnauthorizedAccessException` is consistent with — not a deviation from — this existing convention. No new documentation pattern is introduced.
- The interface lives in the Application layer (`Anela.Heblo.Application.Features.UserManagement.Services`); the concrete adapter (`GraphService`) lives in `Anela.Heblo.Adapters.Microsoft365`. This is the standard Clean Architecture seam used throughout the codebase (interface in Application, implementation in Adapters) — the fix does not cross or blur that seam, it just makes the Application-side contract accurately describe what the Adapters-side implementation actually does.
- I independently verified the three catch blocks in `GraphService.GetGroupMembersAsync` (lines ~167–189): `MsalException` → `GraphServiceAuthException`, `ODataError` → `GraphServiceException`, `UnauthorizedAccessException` → re-thrown verbatim (plus a catch-all `Exception` → re-thrown verbatim, undocumented and intentionally out of scope — see below). This matches the finding exactly.
- Confirmed both callers: `GetGroupMembersHandler.cs` catches `UnauthorizedAccessException` and maps it to `ErrorCodes.Forbidden`; `GraphArticleUserResolver.cs` carries a comment stating the exception is expected to propagate. Both already code against the *implementation's* behavior, not the interface's stated contract — exactly the gap this task closes.

## Proposed architecture

No new components, no new abstractions. The only architectural artifact is the XML doc block itself, which is part of the interface's public contract surface (it flows into IntelliSense and any generated API documentation). Decision: place the new `<exception cref="UnauthorizedAccessException">` entry **after** the two existing entries (auth token failure, then OData response error), since it fires at the same tier as `GraphServiceException` — both originate from the live Graph API call — while `GraphServiceAuthException` fires earlier, during token acquisition. This ordering was already correctly chosen in `design-01.md`; no change needed.

### Option not taken: introduce a typed exception

One could argue `UnauthorizedAccessException` (a BCL type also thrown by unrelated file-system/reflection code) is a poor fit for a Graph-specific contract, and that a `GraphServiceForbiddenException` sibling to `GraphServiceAuthException`/`GraphServiceException` would be more consistent. Rejected for this task: both existing callers already catch the BCL type by name, so introducing a wrapper type would be a breaking behavior change disguised as a docs fix, and is explicitly out of scope per the finding and the plan. Worth a `memory/gotchas` or backlog note if the module gets touched again, but not part of this fix.

## Implementation guidance

- **File to change:** `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs`, lines 7–13.
- **Exact edit:** insert one `<exception cref="UnauthorizedAccessException">...</exception>` block between the existing `GraphServiceException` block (ends line 12) and the `GetGroupMembersAsync` method signature (line 13), per the text already drafted in `design-01.md`.
- **Nothing else moves.** Do not touch `GraphService.cs`, `GetGroupMembersHandler.cs`, or `GraphArticleUserResolver.cs`. Do not add `<exception>` tags to `SearchUsersAsync` or `GetAppRoleMembersAsync` — confirmed these swallow exceptions internally and return empty lists, so they have no undocumented propagation to fix, and doing so would expand scope beyond the finding.
- **Data flow:** unchanged. This is a compile-time-only artifact (XML doc comment); it has zero effect on the request/response path.

## Risks and mitigations

- **Risk:** none of substance — this is additive documentation with no code path change. The only theoretical risk is a malformed XML doc comment breaking doc-file generation if the project has `<GenerateDocumentationFile>true</GenerateDocumentationFile>` enabled.
  - **Mitigation:** `dotnet build` will surface any XML doc syntax error immediately; the plan already calls for this as a verification step.
- **Risk:** scope creep — an implementer might be tempted to also "fix" the undocumented catch-all `Exception` re-throw at the bottom of `GraphService.GetGroupMembersAsync`, or to normalize exception docs across the other two `IGraphService` methods.
  - **Mitigation:** explicitly out of scope, as already stated in `plan-01.md`. Flag as a follow-up finding if desired, do not fold into this change.

## Prerequisites before implementation

None. The finding, plan, and design are all consistent, scoped correctly, and verified against current source. Implementation can proceed directly to the single-line edit described above.
