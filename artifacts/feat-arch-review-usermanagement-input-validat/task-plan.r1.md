Plan saved to `artifacts/feat-arch-review-usermanagement-input-validat/plan.r1.md`.

It decomposes the work into 6 tasks executed TDD-first:

1. **Add the validator** (with a `.Must(!IsNullOrWhiteSpace)` rule — plain `NotEmpty()` would let `"   "` through, contradicting the spec's stated acceptance criterion) plus four `[Fact]`/`[Theory]` unit tests.
2. **Wire DI + pipeline integration test** that builds a real `ServiceCollection`, resolves `IMediator`, and asserts `ValidationException` for empty `GroupId` before the handler runs — the safety net for the arch review's HIGH-severity "forgot one of two registrations" risk.
3. **Translate the failure at the MCP boundary** — catch `FluentValidation.ValidationException` in `UserManagementMcpTools.GetGroupMembers` and rethrow as `McpException("[ValidationError] ...")`, mirroring the existing message format.
4. **Delete the redundant null guard** in `GraphService.GetGroupMembersAsync` (lines 119–126 in the current file).
5. **Remove the obsolete handler test** `Handle_WithEmptyGroupId_CallsGraphService` per arch-review amendment 4.
6. **Final sweep** — build, format-verify, run UserManagement slice, run full suite, confirm acceptance criteria.

Each step shows full code, exact `dotnet`/`git` commands, expected output, and a focused commit. Out-of-scope items (`GetAppRoleMembersAsync` guard, `IGraphService` signature, GUID-format checks) are explicitly called out in the File Structure section so the implementer doesn't drift.