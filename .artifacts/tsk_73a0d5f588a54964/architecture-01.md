# Architecture Assessment: Remove dead `IGraphService.SearchUsersAsync`

## Verdict

Approved as scoped. This is a pure subtraction with no architectural risk — proceed with Option A (deletion) exactly as the plan and design specify. No further design work is needed; what follows validates the diff surface against the live repo and calls out the two things an implementer must get right.

## Alignment with existing patterns

Verified live against `backend/src`, not just the plan's assumptions:

- **Interface contract** (`IGraphService.cs`) declares three methods. Grep of the whole codebase for `IGraphService|GraphService>` (12 files) shows the two surviving methods each have a real consumer chain: `GetGroupMembersAsync` → `GetGroupMembersHandler` and `GraphArticleUserResolver`; `GetAppRoleMembersAsync` → `EntraAccessUserSourceAdapter`. `SearchUsersAsync` has no entry anywhere in that chain — no handler, no MCP tool registration, no adapter. This matches the finding exactly and confirms the interface itself is sound; only this one member is dead.
- **DI wiring** lives in `Microsoft365AdapterServiceCollectionExtensions.cs`, registering `GraphService`/`MockGraphService` against `IGraphService` as a whole — removing one interface member requires no change there, since C# interface implementation is structural, not registered per-method.
- **Vertical Slice convention** (per `docs/architecture/development_guidelines.md`): every live capability in this codebase is expected to run end-to-end (handler → endpoint/MCP tool → optionally frontend). `SearchUsersAsync` never got that treatment — it's an orphaned adapter method with test coverage but no slice around it. Deleting it, rather than building the missing slice speculatively, is the correct default per the repo's own YAGNI guidance ("speculative configuration/feature flags with no consumer" is called out as a smell in that doc).
- **DTOs are classes** rule doesn't apply here — no DTO is being added or changed. `UserDto` is untouched and remains exercised by the two surviving methods.

## Proposed architecture

None — there is no new component, boundary, or contract to design. The design doc already reduced this to four verified diffs:

1. `IGraphService.cs:14` — delete the one-line declaration.
2. `GraphService.cs:192–266` — delete the method body; `GraphService.cs:25` — delete `SearchResultLimit`, confirmed (via `sed`/grep in this pass) to have exactly one other reference, at line 218 inside the method being deleted, so it becomes fully dead once the method is gone.
3. `MockGraphService.cs:22–26` — delete the mock override.
4. `GraphServiceSearchTests.cs` — delete the file in full (120 lines, 5 facts, all exclusively covering the removed method).

I re-ran the repo-wide grep for `SearchUsersAsync` in this pass and it returns matches in exactly these same 4 files (plus the test file's 5 call sites) — no fifth location was missed. **Options considered:** the finding's Option B (build the missing vertical slice — handler, endpoint/MCP tool, frontend) was considered and rejected for this task: it's out of scope for an arch-review cleanup, no product requirement calls for user directory search today, and building speculative surface contradicts the same YAGNI principle motivating the cleanup. If directory search becomes a real requirement later, it re-enters through a normal brainstorm/spec cycle, not by resurrecting an orphaned method.

## Implementation guidance

- **Order of edits doesn't matter functionally** (interface, both implementations, and tests can be edited in either order since C# doesn't require single-file consistency mid-edit), but for reviewability: interface first, then the two implementations, then delete the test file, matching the design doc's ordering.
- **`SearchResultLimit` deletion is conditional, not automatic** — the implementer must re-grep `SearchResultLimit` in `GraphService.cs` at edit time (not trust this doc's snapshot) before removing it, per the repo's surgical-changes rule. This pass confirms it today, but confirm again immediately before deleting.
- **No `using` directive cleanup expected** — the removed method's types (`System.Text.Json`, `System.Net.Http.Headers`) are already used elsewhere in `GraphService.cs` (e.g., `GetGroupMembersAsync`/`GetAppRoleMembersAsync` likely share the same HTTP/JSON patterns); verify with `dotnet build` warnings rather than removing usings preemptively.
- **Test suite scope**: only `GraphServiceSearchTests.cs` is implicated. `MockGraphServiceTests.cs` and `GraphServiceTests.cs` were grepped in this pass and confirmed to have zero `SearchUsersAsync` references — no edits needed there, but the implementer should re-grep at execution time since test files can drift between planning and execution.
- **Data flow**: none — there was never a request/response path into this method from outside the adapter layer, so there is no caller-side cleanup, no MCP tool deregistration, and no frontend hook to touch.

## Risks and mitigations

- **Risk: hidden reflection-based or DI-container consumer.** Mitigated — `IGraphService` is registered and consumed by constructor injection only (standard pattern throughout this codebase, confirmed via the 12-file grep); no reflection-based dispatch pattern exists in this codebase for services. `dotnet build` will fail loudly if anything still references the deleted method, since C# is statically typed.
- **Risk: `SearchResultLimit` reused elsewhere in `GraphService.cs`.** Mitigated by the plan's own open question and this assessment's live re-verification (one reference, inside the deleted method). Re-verify once more immediately before the edit, since this is a live codebase and the plan/design snapshots are already a step removed from "now."
- **Risk: silent loss of a needed capability.** Low — no endpoint, MCP tool, or frontend hook depends on this method today (verified), so nothing observable breaks. If directory search is actually wanted, it was never functional in production anyway (nothing called it), so removal changes nothing for end users.

## Prerequisites before implementation

None outstanding. Both prior steps (plan, design) already did the verification work this assessment would otherwise demand (live grep, DI registration check, constant-usage check). Implementation can proceed directly to the four deletions listed above, followed by `dotnet build`, `dotnet format`, and the full `Anela.Heblo.Tests` run per the repo's validation rules.
