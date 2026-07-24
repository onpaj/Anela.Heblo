# Review: Document `UnauthorizedAccessException` on `IGraphService.GetGroupMembersAsync`

## Verdict: done

## What I checked

1. **Diff against the pre-task baseline** (`git diff 59c93f0b HEAD -- backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs`): exactly a 3-line addition, nothing else touched in the file or repo. `git status` is clean.

2. **Current file content** (`IGraphService.cs:1-19`): the new block

   ```csharp
   /// <exception cref="UnauthorizedAccessException">
   /// Thrown when the caller lacks permission to read the specified group.
   /// </exception>
   ```

   is inserted between the existing `GraphServiceException` tag and the `GetGroupMembersAsync` signature, matching `design-01.md` verbatim — correct placement (same tier as `GraphServiceException`, both firing at the live-call stage vs. `GraphServiceAuthException` at token-acquisition stage), correct indentation, well-formed XML.

3. **Conformance to spec (FR-1 in `plan-01.md`)**: the doc comment now lists all three exceptions the adapter can propagate, wording explains *when* it fires, no other `IGraphService` members touched. Met.

4. **Adherence to architecture**: no component/contract/DTO changes, Application/Adapters seam untouched, consistent with the existing `<exception cref="...">` documentation convention already used on this method. Matches `architecture-01.md`.

5. **Correctness against the live implementation** — re-verified independently (not just trusting the prior steps' claims):
   - `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs:180` still has `catch (UnauthorizedAccessException authEx)` that rethrows.
   - `backend/src/Anela.Heblo.Application/Features/UserManagement/UseCases/GetGroupMembers/GetGroupMembersHandler.cs:56` still catches `UnauthorizedAccessException` and maps it.
   - `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/GraphArticleUserResolver.cs:37` still comments on `UnauthorizedAccessException` propagating as-is.
   - Note: commit `b0d8f7d3` ("decouple GetGroupMembersHandler from SDK exception types") landed on `main` between the finding being filed and this fix, and touched the handler — but it did not remove the `UnauthorizedAccessException` catch or change the adapter's rethrow, so the doc addition is still accurate against current `main`.

6. **Scope discipline**: no unrelated files changed; `SearchUsersAsync`/`GetAppRoleMembersAsync` correctly left undocumented (they swallow exceptions internally, out of scope per the finding).

7. **Build/format verification**: `dotnet` and `podman`/`docker` are unavailable in this sandbox, so `dotnet build`/`dotnet format` could not be executed here either — same limitation development-01.md already flagged. The change is a syntactically well-formed addition mirroring two pre-existing, already-compiling sibling `<exception>` tags in the same block, so the residual build risk is negligible. A maintainer/CI should still run `dotnet build && dotnet format --verify-no-changes` before merge, per repo policy, but this is not a blocking correctness concern for the review.

## Conclusion

Implementation matches the plan, design, and architecture exactly; verified independently against current `main` state (post `b0d8f7d3`), not just re-stated from prior artifacts. No functional requirement is unmet, no architecture conflict, no missing required tests (none were required — doc-only change), no logic/correctness bug. Approved.
