# Specification: Extract Helper Methods from `GraphService.GetAppRoleMembersAsync`

## Summary
`GraphService.GetAppRoleMembersAsync` (`backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs`, lines 193–384) is ~190 lines that inline five sequential Microsoft Graph API steps in a single method body. This spec extracts each step into a private helper method so the public method becomes a short orchestrating sequence of calls, with zero behavior change. This is a pure internal refactor: no public interface (`IGraphService`), request/response contract, endpoint, or caller changes.

## Background
The previous arch-review finding #2630 refactored `GetGroupMembersAsync` (now ~88 lines) but did not cover `GetAppRoleMembersAsync`, which grew independently to ~190 lines with six sequential concerns (token acquisition — already extracted — service-principal resolution, app-role-id lookup, paginated assignment collection, group expansion, and batch user resolution). This exceeds the project's informal 50-line method guideline (used consistently across `docs/superpowers/plans/*` and enforced ad hoc by the arch-review routine) and makes the method hard to unit-test as a unit and hard to reason about locally.

An existing test suite (`backend/test/Anela.Heblo.Tests/Features/UserManagement/GetAppRoleMembersTests.cs`) exercises `GetAppRoleMembersAsync` end-to-end through a queued fake `HttpMessageHandler`, covering: cache hit (no HTTP calls), missing `AzureAd:ClientId` config (empty list, no HTTP calls), and the happy path (service principal → assignment → batch user resolution, 1 user returned). These three tests assert only on the public method's return value and call count to `IHttpClientFactory.CreateClient`, not on any internal structure, so they serve as the primary regression guard for this refactor and must continue to pass unmodified.

## Functional Requirements

### FR-1: Extract `ResolveServicePrincipalAsync`
Extract the service-principal resolution and app-roles collection currently inline at lines 229–247 (the `spUrl` GET request, JSON parse, and `spId` extraction) into a private helper.

**Acceptance criteria:**
- New private method with signature equivalent to `Task<(string? SpId, JsonElement? AppRoles)> ResolveServicePrincipalAsync(string clientId, string graphToken, HttpClient httpClient, CancellationToken cancellationToken)` (exact return shape is an implementation choice — see Non-Functional Requirements — as long as it lets the caller obtain both the resolved `spId` and the `appRoles` array needed by FR-2).
- Preserves existing behavior exactly: on non-success status code or missing/empty `id`, the original method logs the same `_logger.LogError(...)` calls (verbatim message templates and arguments) and returns an empty `List<UserDto>` — this early-return-on-failure behavior stays in the orchestrating public method (i.e., the helper must let the caller detect failure and return early), not inside the helper via a thrown exception, so log messages and call sequence are unchanged.
- No change to the Graph request URL, headers, or the `$select=id,appRoles` query.

### FR-2: Extract `FindAppRoleIdAsync` (or synchronous `FindAppRoleId`)
Extract the loop at lines 249–261 that scans the service principal's `appRoles` array for the entry matching the requested `appRoleValue` into a private helper. Because this step does no I/O, it may be synchronous (`private static string? FindAppRoleId(...)`) rather than `async Task<T>` — the issue's suggested method names are illustrative, not mandatory; use whichever signature (sync vs. async) matches what the step actually does.

**Acceptance criteria:**
- Takes the `appRoles` JSON data (as produced by FR-1) and the requested `appRoleValue`, returns the matching `appRoleId` or `null`.
- Preserves the existing `_logger.LogWarning("App role '{RoleValue}' not found on service principal {SpId}", appRoleValue, spId)` call and the empty-list early return when no match is found — this may stay in the caller (orchestrating method) since it needs both `appRoleId` and `spId`.
- No behavior change to the matching logic (exact string equality on `role.value`, first-match-wins via `break`).

### FR-3: Extract `CollectRoleAssigneesAsync`
Extract the paginated `while (nextLink != null)` loop at lines 268–306 that walks `servicePrincipals/{spId}/appRoleAssignedTo` and buckets principals into direct users vs. groups-to-expand, into a private helper.

**Acceptance criteria:**
- New private method with signature equivalent to `Task<(HashSet<string> DirectUserIds, List<string> GroupIdsToExpand)> CollectRoleAssigneesAsync(string spId, string appRoleId, string graphToken, HttpClient httpClient, CancellationToken cancellationToken)`, returning `null`/a failure signal (or throwing, per the Non-Functional Requirements decision) when any page request fails.
- Preserves exact pagination behavior: starts at `servicePrincipals/{spId}/appRoleAssignedTo?$top=100`, follows `@odata.nextLink` until null, filters assignments by `appRoleId` equality, buckets `principalType == "User"` into the direct-user set and `principalType == "Group"` into the group-expansion list, skips entries with empty `principalId`.
- Preserves the existing `_logger.LogError("Failed to get app role assignments. Status: {Status}, Body: {Body}", ...)` call and empty-list-return-on-failure behavior for any page in the loop.

### FR-4: Keep group expansion (step 5) as a direct, un-extracted call
Per the issue's explicit instruction, the recursive `GetGroupMembersAsync` call in the `foreach (var groupId in groupIdsToExpand)` loop (lines 308–313) stays inline in the orchestrating method — it is not wrapped in a new private helper. This loop is 6 lines and calls an already-extracted public method; no further extraction adds test value.

**Acceptance criteria:**
- The `foreach (var groupId in groupIdsToExpand) { var members = await GetGroupMembersAsync(...); ... }` loop remains directly in `GetAppRoleMembersAsync`, unchanged in behavior (adds each returned member's `Id` into the same `directUserIds` set collected by FR-3).

### FR-5: Extract `BatchResolveUserDtosAsync`
Extract the batching loop at lines 317–369 (chunking `directUserIds` into groups of `GraphBatchSize`, building and sending each Graph `$batch` request, and parsing each batch sub-response into `UserDto`) into a private helper.

**Acceptance criteria:**
- New private method with signature equivalent to `Task<List<UserDto>?> BatchResolveUserDtosAsync(IReadOnlyCollection<string> userIds, string graphToken, HttpClient httpClient, CancellationToken cancellationToken)`, returning `null` (or a failure signal, per the chosen error-handling shape) when any batch request itself fails (non-success HTTP status on the `$batch` POST) — matching the existing early-return-with-empty-list-at-the-call-site behavior.
- Preserves exact batching, request, and parsing behavior: `GraphBatchSize` (20) items per chunk, `$batch` POST body shape (`{ requests: [{ id, method: "GET", url }] }`), per-sub-response `status != 200` handling (`_logger.LogWarning("Could not resolve user {UserId} — batch sub-response status {Status}", ...)` and `continue`, not abort), and `UserDto` field mapping (`Id`, `DisplayName`, `Email = mail ?? upn ?? ""`).
- Preserves the existing `_logger.LogError("Graph $batch request failed. Status: {Status}, Body: {Body}", ...)` call on a failed batch POST.

### FR-6: `GetAppRoleMembersAsync` becomes an orchestrating sequence
After FR-1, FR-2, FR-3, FR-5 land, the public method's body (excluding the existing early-return guard clauses for null/empty `appRoleValue`, cache hit, and missing `AzureAd:ClientId`, and excluding the existing outer `try`/`catch` block) consists of: acquire token → call `ResolveServicePrincipalAsync` (return `[]` on failure) → call `FindAppRoleIdAsync`/`FindAppRoleId` (return `[]` on failure) → call `CollectRoleAssigneesAsync` (return `[]` on failure) → the un-extracted group-expansion `foreach` loop (FR-4) → call `BatchResolveUserDtosAsync` (return `[]` on failure) → cache and return the result.

**Acceptance criteria:**
- `GetAppRoleMembersAsync` method body (measured the same way as the issue measured the original ~190 lines — from opening `{` to closing `}`) is reduced to roughly 60–90 lines (guard clauses + try/catch scaffolding + the 5-step orchestration + the FR-4 loop + caching/logging), i.e., no single method in the file exceeds the project's ~50-line-per-*logical-unit* guideline once helpers are counted separately.
- Each of `ResolveServicePrincipalAsync`, `FindAppRoleIdAsync`/`FindAppRoleId`, `CollectRoleAssigneesAsync`, `BatchResolveUserDtosAsync` is a `private` method on `GraphService` (not `public`/`internal` — no new surface on `IGraphService`).
- `IGraphService` interface is unchanged (no new members).
- No behavior change: `dotnet test --filter "FullyQualifiedName~GetAppRoleMembersTests"` passes unmodified (all 3 existing tests), and `dotnet test --filter "FullyQualifiedName~GraphServiceTests"` / `ParseMembersFromJsonTests` / `MockGraphServiceTests` continue to pass unmodified.

## Non-Functional Requirements

### NFR-1: No behavior change
This is a pure refactor. Response shapes, HTTP request URLs/headers/bodies, cache key (`app_role_members_{appRoleValue}`) and expiration (`_cacheExpiration`, 20 minutes), log message templates and log levels, and exception types thrown (`GraphServiceAuthException` on token-acquisition `MsalException`, unhandled exceptions rethrown from the outer `catch (Exception ex)`) must all remain identical to the current implementation. Callers of `IGraphService.GetAppRoleMembersAsync` (application-layer handlers, if any exist — verify via a repo-wide usage search before implementation) require no changes.

### NFR-2: Failure-signaling shape for extracted helpers
Each extracted step currently returns early from the *public* method with `return new List<UserDto>();` on failure (after logging an error). The implementer must choose one consistent failure-signaling shape for the new helpers — either (a) a nullable/`Try`-style return (e.g. `(bool Success, T Value)` tuple or nullable result) that the orchestrating method checks and turns into the existing early return, or (b) the helper itself performs the logging and the orchestrating method checks a null/empty sentinel. Either is acceptable as long as: the exact existing log calls fire in the exact existing order, and the public method's return-empty-list-on-failure behavior at each of the four failure points (SP resolution, app-role-not-found, assignment-page failure, batch failure) is preserved byte-for-byte in test-observable terms (log output + return value). This spec does not mandate exceptions for expected-failure paths (SP not found, batch HTTP failure, etc.) because the current code treats these as expected/loggable-and-return, not exceptional — introducing exceptions here would be a behavior change requiring updated tests, which is out of scope.

### NFR-3: Testability improvement (the stated goal of the issue)
Extracted helpers should be structured so that, in principle, each could be unit-tested independently in a follow-up (not required by this spec — see Out of Scope). This mainly constrains helper signatures to take their dependencies (`HttpClient`, `graphToken`, ids) as parameters rather than closing over ambient method-local state in a way that would prevent isolated testing later.

### NFR-4: Code style
Follow the codebase's existing C# conventions in this file: `private async Task<T>` for I/O-bound helpers (per the issue's suggested naming), XML doc comments are not required for `private` helpers (none exist on the current private `AcquireGraphTokenAsync`), and keep using the same `System.Text.Json.JsonDocument`/`JsonElement` parsing style already used throughout the file (no new JSON library).

## Data Model
No data model changes. `UserDto` (`Id`, `DisplayName`, `Email`) is unchanged. No new DTOs are introduced by this refactor; any intermediate value extracted-method signatures pass between helpers (e.g., a service-principal-id + app-roles pair) may use a `ValueTuple` or a small `private` local record/struct scoped to `GraphService` if it improves readability — this is an implementation detail, not a public contract.

## API / Interface Design
No HTTP endpoint, controller, MCP tool, or `IGraphService` interface changes. The four new methods are `private` members of `GraphService` only.

## Dependencies
No new dependencies. Uses only what `GraphService.cs` already references: `ITokenAcquisition`, `IMemoryCache`, `ILogger<GraphService>`, `IHttpClientFactory`, `IConfiguration`, `System.Text.Json`, `Microsoft.Identity.Client` (`MsalException`), `Microsoft.Graph.Models.ODataErrors.ODataError`.

## Out of Scope
- Writing new unit tests for the individual extracted helper methods (NFR-3 only requires them to be *structured* for future testability, not tested now — the issue's stated goal is readability/maintainability via extraction; the existing 3 end-to-end tests in `GetAppRoleMembersTests.cs` remain the regression guard).
- Any change to `GetGroupMembersAsync` (already refactored under #2630) or to the group-expansion call site beyond what FR-4 requires (i.e., it must NOT be extracted into a new helper).
- Any change to `ParseMembersFromJson`, `AcquireGraphTokenAsync`, caching strategy/expiration, or the outer `try`/`catch` exception-handling structure of `GetAppRoleMembersAsync`.
- Any change to `MockGraphService` (the test/dev double) — verify it is unaffected since it does not share implementation with `GraphService`.
- Renaming `GetAppRoleMembersAsync` itself or changing its public signature.
- Performance changes (this is not a performance fix; sequential Graph calls remain sequential).
- Any change to `docs/integrations/mcp-server.md` or other documentation — this method is not directly exposed as an MCP tool per the existing doc inventory (verify during implementation; out of scope to update docs unless the verification finds otherwise).

## Open Questions
None.

## Status: COMPLETE
