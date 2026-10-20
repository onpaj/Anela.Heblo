# Code Review: relocate-exception-files

## Summary
The task required moving `GraphServiceAuthException.cs` and `GraphServiceException.cs` from `Contracts/` to `Infrastructure/Exceptions/` via `git mv`, updating their namespace, and committing — with the build expected to fail at this point since consumers are updated in a later task. All steps were followed exactly as specified.

## Review Result: PASS

### task: relocate-exception-files
**Status:** PASS

Verified:
- Both files moved via `git mv` from `Contracts/` to `Infrastructure/Exceptions/`; `git log --follow` history is preserved (rename detected by git, shown as `R` in status).
- Namespace updated to `Anela.Heblo.Application.Features.UserManagement.Infrastructure.Exceptions` in both files, matching the task context exactly.
- The `<see cref="IGraphService"/>` doc comments were fully qualified as instructed, avoiding an unresolved cref warning.
- Class bodies, constructors, and summary text otherwise unchanged — no unintended logic changes.
- Commit made with the exact scope specified (the two old paths + two new paths).
- `dotnet build` on the Application project confirms the expected failure: `CS0246` errors for `GraphServiceAuthException`/`GraphServiceException` in `GetGroupMembersHandler.cs`, `GraphArticleUserResolver.cs`, and `EntraAccessUserSourceAdapter.cs` — exactly the four consumer files named in the issue, and exactly the failure the task context predicted as expected/deferred to the next task.

No issues found for this task's scope.

## Docs to Update
(none — this is an internal refactor with no public behavior, CLI, or documented-layout change beyond what the arch-review issue itself already describes)

## Overall Notes
Nothing further to add. The remaining tasks (`update-production-usings`, `update-test-usings-and-comments`, `final-verification`) are needed before the branch builds cleanly again.
