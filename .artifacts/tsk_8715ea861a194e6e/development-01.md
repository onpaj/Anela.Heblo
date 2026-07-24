# Development: Document `UnauthorizedAccessException` on `IGraphService.GetGroupMembersAsync`

## Summary

Implemented the scoped, doc-only fix approved in `plan-01.md` / `design-01.md` / `architecture-01.md`: added the missing `<exception cref="UnauthorizedAccessException">` XML doc tag to `IGraphService.GetGroupMembersAsync`, matching the existing implementation (`GraphService.cs`) and both callers (`GetGroupMembersHandler`, `GraphArticleUserResolver`) that already handle it.

## Files changed

- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs`
  - Inserted one `<exception cref="UnauthorizedAccessException">Thrown when the caller lacks permission to read the specified group.</exception>` block, placed after the existing `GraphServiceException` doc tag and before the `GetGroupMembersAsync` method signature, per the exact placement specified in `design-01.md`.
  - No other lines touched. `git diff` confirms a 3-line-added, 0-line-removed change scoped to this single method's doc comment.

No other files were changed (`GraphService.cs`, `GetGroupMembersHandler.cs`, `GraphArticleUserResolver.cs`, `SearchUsersAsync`/`GetAppRoleMembersAsync` docs — all left untouched, as specified as out of scope).

## Verification

- Manual inspection: the new tag is syntactically valid XML doc (well-formed `<exception cref="...">...</exception>`), matches the pre-existing style/indentation of the two sibling `<exception>` tags on the same method, and doesn't alter the method signature or any runtime code path.
- No new/changed tests are needed — this is a documentation-only change with zero behavior change, consistent with the plan's expectation ("no test changes needed since this is doc-only").
- `dotnet build` / `dotnet format` could not be run in this sandbox — no .NET SDK or Docker/Podman available in the environment (`dotnet: command not found`, `podman not found`). The change is a comment-only addition inside an existing, already-compiling doc-comment block, so it carries negligible build risk; a maintainer or CI should still run `dotnet build && dotnet format` to confirm, per the repo's stated validation-before-completion policy.

## How to verify

1. `git diff backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs` — confirm only the 3-line addition shown above.
2. `cd backend && dotnet build` — should succeed with no new warnings/errors (XML doc comments are only validated when `GenerateDocumentationFile` is enabled; even so, this tag's syntax mirrors the existing valid tags).
3. `cd backend && dotnet format --verify-no-changes` — should report no diff.
4. Run the UserManagement test suite (e.g. `dotnet test --filter FullyQualifiedName~UserManagement`) — expected: no failures, no test changes required.
