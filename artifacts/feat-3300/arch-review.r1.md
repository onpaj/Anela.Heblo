# Architecture Review: Eliminate N+1 Graph API Calls in GetAppRoleMembersAsync

## Skip Design: true

## Architectural Fit Assessment

This change is a pure internal performance improvement to a single private implementation block (Step 5, lines 385–404 of `GraphService.cs`). It fits naturally within the existing pattern: `GraphService` already wraps all Graph HTTP calls in `using var request = new HttpRequestMessage(...)` blocks against the named `"MicrosoftGraph"` `HttpClient`, parses responses with `System.Text.Json.JsonDocument`, and returns `UserDto` records. The `$batch` endpoint is a different Graph URL and HTTP verb (POST), but no new dependencies or infrastructure are required. `IGraphService` and `UserDto` are unchanged.

The 20-minute `IMemoryCache` layer above Step 5 means this code path is exercised once per cache period per role — but on a cold cache (service restart, first request, eviction) the serial latency is user-visible. The fix is scoped entirely below the cache check, so the caching contract is untouched.

## Proposed Architecture

### Component Overview

```
GetAppRoleMembersAsync (GraphService)
│
├── Step 1–4: unchanged (SP lookup, role ID, assignments pagination, group expansion)
│
└── Step 5 (REPLACED): batch user resolution
    │
    ├── Chunk directUserIds into groups of ≤ GraphBatchSize (20)
    │
    ├── For each chunk:
    │   ├── Build JSON batch payload:
    │   │     { "requests": [ { "id": "0", "method": "GET", "url": "/users/{id}?$select=..." }, ... ] }
    │   ├── POST https://graph.microsoft.com/v1.0/$batch
    │   │     Authorization: Bearer {graphToken}
    │   │     Content-Type: application/json
    │   └── Parse { "responses": [ { "id": "0", "status": 200, "body": {...} }, ... ] }
    │         200 → parse UserDto
    │         non-200 → LogWarning, skip
    │         batch-level failure → LogError, throw (caught by outer try/catch)
    │
    └── Accumulate users → cache → return
```

The outer `try/catch (Exception ex)` already swallows unexpected errors and returns an empty list; batch-level HTTP failures fall into the same handler. No changes are needed to error propagation.

### Key Design Decisions

#### Decision 1: Batch over Task.WhenAll
**Options considered:**
- `Task.WhenAll` — fire N individual GET requests in parallel using the pooled `HttpClient`
- Graph `$batch` — bundle up to 20 sub-requests in one HTTP POST

**Chosen approach:** Graph `$batch`.

**Rationale:** `Task.WhenAll` would reduce wall-clock time for large N by overlapping network round-trips, but it multiplies connection pressure on the `"MicrosoftGraph"` named `HttpClient` and does not reduce the number of outbound HTTP calls charged against Graph throttling limits. The spec explicitly singles out `$batch` and the brief calls it out as the intended fix. With typical small role populations (≤ 20 users) a single batch replaces N calls with 1.

#### Decision 2: Chunk size as a named constant, not injected configuration
**Options considered:**
- `private const int GraphBatchSize = 20;` on `GraphService`
- App setting / `IConfiguration` injection

**Chosen approach:** `private const int GraphBatchSize = 20;` on `GraphService`.

**Rationale:** 20 is the hard Graph API limit per batch call — this is not a tunable parameter. A constant communicates the constraint directly in the code without adding configuration surface that has no valid runtime values other than ≤ 20. Matches `SearchResultLimit = 25` already declared in the same class.

#### Decision 3: Sequential batch calls (one chunk at a time), not parallel
**Options considered:**
- `Task.WhenAll` over the chunk list (parallel batch calls)
- Sequential `foreach` over chunks

**Chosen approach:** Sequential `foreach` over chunks (matching the surrounding code style).

**Rationale:** With expected role populations in the tens, not hundreds, there will almost never be more than one chunk. Sequential processing is simpler, avoids concurrent writes to `users`, and aligns with the existing `foreach` / `await` style used in Steps 3 and 4. If future scale demands it, parallelising batch calls is a follow-on change.

#### Decision 4: JSON serialisation approach
**Options considered:**
- `System.Text.Json.JsonSerializer` with typed DTOs for the batch envelope
- Raw `System.Text.Json.JsonDocument` manipulation (same as used elsewhere in the file)

**Chosen approach:** Use `System.Text.Json.JsonSerializer` to *build* the request payload (serialise a batch envelope object) and `JsonDocument` to *parse* the response — or alternatively construct the JSON string directly with `JsonSerializer.Serialize`.

**Rationale:** The batch request body is a known-shape object that is easiest to build via `JsonSerializer`. Parsing the response with `JsonDocument` is consistent with the existing `ParseMembersFromJson` pattern in the file. No new NuGet package is needed; `System.Text.Json` is already used. Internal helper record/class for the batch envelope is acceptable since it is an internal implementation detail, not a public DTO.

## Implementation Guidance

### Directory / Module Structure

No new files are required. All changes are confined to:

```
backend/src/Anela.Heblo.Application/Features/UserManagement/Services/
└── GraphService.cs          ← only file changed in production code

backend/test/Anela.Heblo.Tests/Features/UserManagement/
└── GraphServiceTests.cs     ← existing file; add new [Fact] methods for batch behaviour
```

Do not create a separate test file for the batch tests. The pattern in this project is to add test methods to the existing per-class test file (`GraphServiceTests.cs`).

### Interfaces and Contracts

None change. Specifically:

- `IGraphService.GetAppRoleMembersAsync(string appRoleValue, CancellationToken)` — signature unchanged
- `UserDto { Id, DisplayName, Email }` — unchanged
- `IMemoryCache` usage and cache key `$"app_role_members_{appRoleValue}"` — unchanged
- `"MicrosoftGraph"` named `HttpClient` — same factory, same client name, same auth header pattern

The batch request uses:
- URL: `https://graph.microsoft.com/v1.0/$batch`
- Method: `POST`
- Header: `Authorization: Bearer {graphToken}` (same token already in scope)
- Content-Type: `application/json`
- Body shape:
  ```json
  {
    "requests": [
      { "id": "0", "method": "GET", "url": "/users/{id}?$select=id,displayName,mail,userPrincipalName" }
    ]
  }
  ```
- Response shape:
  ```json
  {
    "responses": [
      { "id": "0", "status": 200, "body": { "id": "...", "displayName": "...", "mail": "...", "userPrincipalName": "..." } }
    ]
  }
  ```

Note that sub-request URLs in the batch body are **relative** (no `https://graph.microsoft.com/v1.0` prefix). The `id` field in each sub-request is arbitrary; using the array index as a string is sufficient.

### Data Flow

**Cold cache, N directly-assigned users (N ≤ 20):**
```
caller
  → GetAppRoleMembersAsync
    → cache miss
    → Step 1–4 (unchanged)
    → Step 5:
        chunk [0..N-1] into single batch
        POST /v1.0/$batch  (1 HTTP call)
        parse responses array
        200 entries → UserDto list
        non-200 entries → LogWarning, skip
    → _cache.Set(cacheKey, users, _cacheExpiration)
    → return users
```

**Cold cache, N > 20 users:**
```
Step 5:
    chunk [0..19]   → POST /v1.0/$batch  (call 1)
    chunk [20..39]  → POST /v1.0/$batch  (call 2)
    ...
    ⌈N/20⌉ sequential batch calls
    accumulate all 200 results into users list
```

**Warm cache:**
```
caller → cache hit → return cached List<UserDto>  (Step 5 never reached)
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Graph `$batch` throttling (429 on batch endpoint) | Low | Outer `catch (Exception ex)` already returns empty list and logs error; same behaviour as current individual-call throttling. Add `LogError` with status code and retry-after header value. |
| Partial batch failure (some sub-requests 404/403, batch itself 200) | Low | FR-2 already specifies: log warning per non-200 sub-response, skip. Implement exactly that. |
| Batch-level failure (HTTP non-200 on the POST itself) | Low | Fall through to existing `catch (Exception ex)` handler; log error. Consistent with how non-200 responses from Steps 1–3 are handled. |
| Sub-request `id` collision if index-based IDs are reused across chunks | None | Each batch call is independent; IDs only need to be unique within a single batch payload. |
| `FakeHttpMessageHandler` captures only one request URI | Medium (test) | Tests for multiple chunks must use a multi-response handler or separate calls. Implement a `SequentialFakeHttpMessageHandler` (returns canned responses in order) within the test file, following the existing `ThrowingHttpMessageHandler` / `DisposalTrackingHandler` private-class pattern in `GraphServiceTests.cs`. |
| Batch URL is relative, not absolute | Low | Sub-request `url` field must NOT include the Graph host. Verify in unit test by asserting the serialised request body contains `/users/{id}` not `https://graph.microsoft.com/v1.0/users/{id}`. |

## Specification Amendments

**Amendment 1 — Test handler pattern**

FR-5 lists test cases (N=1, N=21, non-200 sub-response, batch-level failure) but does not specify how multi-call scenarios are handled given that `FakeHttpMessageHandler` captures only one request at a time. The implementation must add a private `SequentialFakeHttpMessageHandler` to `GraphServiceTests.cs` that returns pre-configured responses in order. This is the established pattern for inline test helpers in this file.

**Amendment 2 — Logging on batch-level non-200**

NFR-4 specifies `LogError on batch failure`. The current Step 5 has no equivalent — if `httpClient.SendAsync` succeeds but `!userResponse.IsSuccessStatusCode`, the original code continues (it would just miss the user). For the batch endpoint, a non-2xx HTTP status on the `POST /v1.0/$batch` call itself should be logged as `LogError` with the status code, and the method should early-return the empty list (consistent with how Steps 1 and 3 handle Graph failures). This is implied by NFR-4 but worth making explicit.

**Amendment 3 — No `using` on `HttpRequestMessage` for batch POST**

The existing code uses `using var userRequest = new HttpRequestMessage(...)`. The new batch POST request must follow the same pattern. Additionally, the `StringContent` body object should be disposed. This is already implicit in the existing style but call it out explicitly to avoid leaking the content stream in the batch path.

## Prerequisites

None. All prerequisites are already satisfied:

- `"MicrosoftGraph"` named `HttpClient` is registered in `UserManagementModule`
- `ITokenAcquisition` and the Graph token acquisition pattern exist and work
- `System.Text.Json` is already used in this file
- `IMemoryCache` registration is unchanged
- The Graph application permission (`User.Read.All` or equivalent) that allows per-user resolution also covers the `$batch` endpoint — no permission changes required
- No database migrations, no infrastructure changes, no frontend changes
