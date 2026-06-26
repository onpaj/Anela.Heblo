All tasks complete. Here is the output summary:

---

# Implementation: GraphService.GetGroupMembersAsync Refactor

## What was implemented

Two private helper methods extracted from `GraphService.GetGroupMembersAsync`, reducing the orchestrator from ~160 lines to 93 lines and making the parsing logic directly unit-testable. The refactor is strictly behaviour-preserving — all log templates, exception flows, cache semantics, and the public interface are unchanged.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs` — two helpers extracted: `internal static (List<UserDto> Users, int TotalCount) ParseMembersFromJson(string json)` and `private async Task<string> AcquireGraphTokenAsync(string groupId, CancellationToken cancellationToken)`. Orchestrator now reads as a clean sequential pipeline.
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/ParseMembersFromJsonTests.cs` — 7 unit tests for the parser (all-users, mixed users/groups, empty value, missing optional fields, mail/UPN fallback, no @odata.type but has UPN, empty JSON).

`InternalsVisibleTo("Anela.Heblo.Tests")` was already present in the `.csproj` — no change needed.

## Tests

- **`ParseMembersFromJsonTests`** — 7 tests covering all NFR-3 scenarios; parser callable directly without HTTP plumbing.
- **`GraphServiceTests`** — 12 pre-existing tests; all pass without modification (behaviour preserved).

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GraphServiceTests|FullyQualifiedName~ParseMembersFromJsonTests"
# Expected: 19 passed, 0 failed
```

## Notes

- **FR-3 line-count target (≤50 lines):** Not met — method is 93 lines. The excess is from pre-existing verbose structured logging (12 statements) and 4 exception handlers that were in the original method and are not touched by the two scoped extractions. The HTTP send block (25 lines) is explicitly out-of-scope for extraction per the spec. A follow-up task extracting `SendGraphRequestAsync` would bring the count under 50.
- **`AcquireGraphTokenAsync` unused parameters:** `groupId` and `cancellationToken` are accepted but not forwarded to `GetAccessTokenForAppAsync` — the Microsoft.Identity.Web 3.14.1 API does not expose a `CancellationToken` overload; all other callers in the codebase do the same.
- **`graphToken?.Length ?? 0` defensive null-check:** Pre-existing code carried over verbatim; harmless but technically redundant since `Task<string>` is non-nullable.

## PR Summary

Extracted two focused private helpers from the 160-line `GraphService.GetGroupMembersAsync` to separate token acquisition and JSON response parsing from the orchestration flow. The parser is now directly unit-testable without HTTP plumbing (7 new tests). All 12 existing `GraphServiceTests` pass without modification.

### Changes
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs` — extracted `ParseMembersFromJson` (internal static, returns tuple for total count logging) and `AcquireGraphTokenAsync` (private instance); orchestrator reduced to a readable sequential pipeline
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/ParseMembersFromJsonTests.cs` — 7 tests covering all specified parser scenarios

## Status
DONE_WITH_CONCERNS

> Concern: `GetGroupMembersAsync` is 93 lines vs the ≤50-line acceptance criterion. The remaining excess is pre-existing code (logging, exception handlers, HTTP block that is spec-exempt from extraction). Follow-up: extract `SendGraphRequestAsync` to close the gap.