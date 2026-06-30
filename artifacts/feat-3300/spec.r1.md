# Specification: Eliminate N+1 Graph API Calls in GetAppRoleMembersAsync

## Summary

`GraphService.GetAppRoleMembersAsync` (UserManagement module) resolves user display names and email addresses using one sequential HTTP call per directly-assigned user, producing O(N) sequential Microsoft Graph round-trips. Replacing this loop with Graph `$batch` requests reduces the call count to ⌈N/20⌉ and eliminates serial latency stacking. No public interfaces or API contracts change; this is an internal performance improvement to a single private implementation step.

## Background

`GetAppRoleMembersAsync` is called to enumerate members of an Azure AD app role by value (e.g. `"Admin"`, `"ProductManager"`). It proceeds in five steps: resolve the service principal, find the target role ID, page through `appRoleAssignedTo`, expand group assignments, and finally resolve display name + email for each directly-assigned user ID.

Step 5 (lines 386–404 of `GraphService.cs`) iterates over `directUserIds` with a `foreach` / `await` pattern. Each iteration fires a synchronous HTTP round-trip to `GET /v1.0/users/{id}`. Because Graph is a network call, latency accumulates linearly: 10 users ≈ 10× a single call's latency (typically 100–300 ms each), for a worst-case Step 5 cost of several seconds before the cache is populated.

Microsoft Graph's `$batch` endpoint (`POST /v1.0/$batch`) accepts up to 20 sub-requests in one HTTP call and processes them in parallel server-side. Switching to batch reduces N sequential calls to ⌈N/20⌉ parallel-on-the-server batch calls, cutting latency by up to 20×.

The result set is cached for 20 minutes (`_cacheExpiration`), so the expensive path is hit only once per cache period per role — but when the cache is cold (first request, cache eviction, service restart) the current behaviour blocks the caller for the full N-call duration.

## Functional Requirements

### FR-1: Replace per-user serial HTTP calls with Graph batch requests

Step 5 of `GetAppRoleMembersAsync` must be rewritten. Instead of issuing one `GET /users/{id}` per user, the implementation must partition `directUserIds` into chunks of at most 20 IDs and issue one `POST /v1.0/$batch` call per chunk. Each batch call carries up to 20 sub-requests, each a `GET /users/{id}?$select=id,displayName,mail,userPrincipalName`.

**Acceptance criteria:**
- Given N directly-assigned user IDs, Step 5 issues exactly ⌈N/20⌉ HTTP calls (batch calls) rather than N calls.
- Each batch call is a `POST` to `https://graph.microsoft.com/v1.0/$batch`.
- The request body is a JSON object `{ "requests": [ ... ] }` where each element has `"id"`, `"method": "GET"`, and `"url": "/users/{id}?$select=id,displayName,mail,userPrincipalName"`.
- The `Authorization: Bearer {token}` header is present on the outer batch HTTP call; sub-requests do not carry individual auth headers (Graph $batch inherits the outer token).
- The `Content-Type: application/json` header is present on the batch POST.

### FR-2: Parse batch response and map users correctly

The `POST /v1.0/$batch` response body contains a `responses` array. Each response object has `"id"`, `"status"` (HTTP status code as integer), and `"body"` (the individual user object or error body).

**Acceptance criteria:**
- For each sub-response with `"status": 200`, parse `id`, `displayName`, `mail`, `userPrincipalName` from the `body` field and produce a `UserDto` using the same logic as the replaced loop (`Email = mail ?? userPrincipalName ?? ""`).
- For each sub-response with a non-200 status, log a warning at `LogWarning` level identifying the user ID and status code, and skip that entry (same behaviour as the replaced `continue` branch).
- The overall `List<UserDto>` result is assembled from all successful sub-responses across all batch calls and then cached as before.

### FR-3: Preserve all existing error-handling and caching behaviour

The surrounding logic (cache lookup at entry, `_cache.Set` after resolution, `LogInformation` on success, `LogError` on token failure or unrecoverable error, `return new List<UserDto>()` on non-fatal Graph failures) must remain unchanged. Only Step 5's user-resolution loop is modified.

**Acceptance criteria:**
- A cold cache miss still populates the cache after Step 5 completes.
- A cache hit at method entry still short-circuits before any Graph call (including batch calls).
- Token acquisition failure still returns an empty list with a logged error.
- If a batch HTTP call itself returns a non-2xx status (transport/network error at the batch level), log an error and return an empty list (consistent with existing behaviour for individual call failures).

### FR-4: Chunk size is configurable via constant, defaults to 20

The maximum sub-requests per batch call must be expressed as a named constant (not a magic number), set to 20 (the Graph API limit). This makes it easy to find and adjust if Microsoft changes the limit.

**Acceptance criteria:**
- A `private const int GraphBatchSize = 20;` (or equivalent named constant) is defined in `GraphService`.
- The chunk partitioning uses this constant.

### FR-5: Unit tests cover batch behaviour

The existing `GraphServiceTests` suite must be extended to cover the new batch path.

**Acceptance criteria:**
- A test verifies that for N=1 user, exactly 1 batch POST is made (not a GET to `/users/{id}`).
- A test verifies that for N=21 users, exactly 2 batch POSTs are made.
- A test verifies that a non-200 sub-response status causes that user to be skipped and the rest to be returned normally.
- A test verifies that a batch-level non-2xx HTTP response returns an empty list.
- Existing tests continue to pass without modification.

## Non-Functional Requirements

### NFR-1: Performance

The purpose of this change is latency reduction on the cold-cache path of `GetAppRoleMembersAsync`. After the change, resolving up to 20 directly-assigned users must require exactly 1 Graph network round-trip in Step 5 (down from up to 20). For N users the maximum Step 5 latency must be proportional to ⌈N/20⌉, not N.

### NFR-2: Security

No changes to authentication, authorization, or secret handling. The same `Bearer` token acquired by `GetAccessTokenForAppAsync("https://graph.microsoft.com/.default")` is used on the outer batch request. Sub-requests within the batch do not include individual `Authorization` headers — this is the standard Graph $batch pattern.

### NFR-3: Correctness under concurrent cache access

The existing `IMemoryCache`-based cache (keyed `app_role_members_{appRoleValue}`, 20-minute TTL) is preserved. No new concurrency primitives are needed; cache behaviour is unchanged.

### NFR-4: Observability

Log verbosity must not regress. The replacement code must emit at minimum:
- `LogInformation` when all batch calls complete, reporting total user count (replacing the current success log at line 407).
- `LogWarning` per skipped sub-response (non-200 status within a batch).
- `LogError` if a batch-level HTTP call fails.

## Data Model

No data model changes. The feature operates entirely within `GraphService` and produces `List<UserDto>` (unchanged contract):

```
UserDto
  Id:          string   — Azure AD object ID
  DisplayName: string   — Graph displayName
  Email:       string   — Graph mail ?? userPrincipalName ?? ""
```

## API / Interface Design

### Internal Graph $batch request structure

```
POST https://graph.microsoft.com/v1.0/$batch
Authorization: Bearer {token}
Content-Type: application/json

{
  "requests": [
    {
      "id": "1",
      "method": "GET",
      "url": "/users/{userId1}?$select=id,displayName,mail,userPrincipalName"
    },
    {
      "id": "2",
      "method": "GET",
      "url": "/users/{userId2}?$select=id,displayName,mail,userPrincipalName"
    }
    // ... up to 20 entries per call
  ]
}
```

### Internal Graph $batch response structure

```json
{
  "responses": [
    {
      "id": "1",
      "status": 200,
      "body": {
        "id": "...",
        "displayName": "...",
        "mail": "...",
        "userPrincipalName": "..."
      }
    },
    {
      "id": "2",
      "status": 404,
      "body": { "error": { "code": "Request_ResourceNotFound" } }
    }
  ]
}
```

### No changes to external-facing interfaces

- `IGraphService.GetAppRoleMembersAsync(string, CancellationToken)` — signature unchanged.
- `UserDto` — unchanged.
- No controller, MediatR handler, or OpenAPI schema changes.

### Named HTTP client

The existing `"MicrosoftGraph"` named `HttpClient` (registered in `UserManagementModule`) is reused for the batch POST. No new client registrations are needed.

## Dependencies

- **Microsoft Graph $batch API** — `POST https://graph.microsoft.com/v1.0/$batch`. Limit: 20 sub-requests per batch. The application already holds the `User.Read.All` application permission required; batch does not require additional permissions.
- **`System.Text.Json`** — already used throughout `GraphService` for JSON parsing.
- **`IHttpClientFactory` / `"MicrosoftGraph"` named client** — already injected and used in `GraphService`.
- **`IMemoryCache`** — no changes; already injected.

## Out of Scope

- Parallelising `Task.WhenAll` as an interim measure. The brief proposes this as an alternative; this spec targets the batch approach directly as it is the correct long-term solution.
- Changing batch behaviour for `GetGroupMembersAsync` or `SearchUsersAsync`. Those methods do not have the same N+1 pattern.
- Expanding group membership (Step 4). Group expansion calls `GetGroupMembersAsync` which uses a single paginated Graph call per group — this is not an N+1 problem and is out of scope.
- Persistent caching (Redis, distributed cache). The 20-minute in-memory cache is unchanged.
- Retrying failed batch sub-requests. A warning log and skip is sufficient, consistent with existing per-user failure handling.
- Changing the cache TTL or cache key scheme.

## Open Questions

None.

## Status: COMPLETE
