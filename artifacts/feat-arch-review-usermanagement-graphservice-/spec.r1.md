# Specification: GraphService HttpClientFactory Migration

## Summary
Replace the raw `new HttpClient()` instantiation inside `GraphService.GetGroupMembersAsync` with an `IHttpClientFactory`-provided client to eliminate socket exhaustion and DNS-staleness risks against `graph.microsoft.com`. The change is a localized refactor of one service plus the `UserManagementModule` DI registration; public behavior, response shape, and caching semantics must remain identical.

## Background
`GraphService` is the production implementation of `IGraphService`, registered in `UserManagementModule.AddUserManagement` when neither `USE_MOCK_AUTH` nor `BYPASS_JWT_VALIDATION` is enabled. It is consumed by `GetGroupMembersHandler` to resolve Microsoft Entra group memberships used for user-management flows.

Today, every cache miss (cold start or 20-minute expiry per group) executes `using var httpClient = new HttpClient()` at `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs:84`. Per Microsoft's guidance on `HttpClient` lifetime, this pattern:

- Bypasses connection pooling: each call opens and closes a fresh TCP socket against `graph.microsoft.com`.
- Holds sockets in `TIME_WAIT` after disposal, contributing to port/socket exhaustion under concurrent cache misses.
- Bypasses DNS TTL refresh handled by `SocketsHttpHandler` when reused through `IHttpClientFactory`.
- Has previously been linked to silent failures (HTTP errors surfacing as empty member lists) when sockets exhaust or Graph throttles.

The 20-minute cache lowers but does not remove the risk: concurrent first-hits, staggered group lookups, and process restarts all bypass the cache simultaneously. Migrating to `IHttpClientFactory` is the standard, low-risk remediation and aligns the module with the rest of the platform.

## Functional Requirements

### FR-1: Inject IHttpClientFactory into GraphService
`GraphService` must obtain its `HttpClient` from `IHttpClientFactory` instead of instantiating one directly. The factory is added to the constructor as a required dependency; the existing `ITokenAcquisition`, `IMemoryCache`, and `ILogger<GraphService>` parameters remain unchanged.

**Acceptance criteria:**
- `GraphService`'s constructor accepts `IHttpClientFactory httpClientFactory` and stores it in a `private readonly` field.
- No code path inside `GraphService` calls `new HttpClient(...)`; the only client construction is `_httpClientFactory.CreateClient("MicrosoftGraph")`.
- `MockGraphService` is **not** changed (no behavioral or signature changes required).
- `IGraphService` interface is unchanged.

### FR-2: Register named HttpClient `"MicrosoftGraph"`
A named `HttpClient` registration with the logical name `"MicrosoftGraph"` must be added in `UserManagementModule.AddUserManagement`. The registration must apply only on the real-service branch (i.e., when `useMockAuth == false` and `bypassJwtValidation == false`); when mocks are used, the named client need not be registered, since `MockGraphService` does not perform HTTP calls.

**Acceptance criteria:**
- `services.AddHttpClient("MicrosoftGraph")` is invoked alongside `services.AddScoped<IGraphService, GraphService>()` in the production branch of `UserManagementModule.AddUserManagement`.
- The registration uses the default `HttpMessageHandler` lifetime supplied by `IHttpClientFactory` (no custom handler lifetime is configured in this change).
- No `BaseAddress`, `DefaultRequestHeaders`, or handler is configured on the named client in this change (the absolute Graph URL and per-request Authorization header continue to be set inside `GetGroupMembersAsync`, preserving current behavior).
- If the production branch is selected but the named client somehow is not registered, the service must still resolve a default client (i.e., `CreateClient("MicrosoftGraph")` returns a usable client; the factory creates a default-configured client when the name is unknown — this is the standard .NET behavior and is acceptable as a safety fallback, not a substitute for FR-2).

### FR-3: Preserve existing GetGroupMembersAsync behavior
The refactor is non-functional from the caller's perspective. All current behaviors of `GetGroupMembersAsync` must be preserved byte-for-byte where the change does not directly require modification.

**Acceptance criteria:**
- The cache key (`group_members_{groupId}`), 20-minute TTL, and cache hit/miss branches behave identically.
- The token-acquisition flow (`GetAccessTokenForAppAsync` with `https://graph.microsoft.com/.default`) is unchanged.
- The request URL, query string (`$select=id,displayName,mail,userPrincipalName`), and `Bearer` header construction remain identical.
- The same response parsing (`@odata.type` / `userPrincipalName` detection, `id` / `displayName` / `mail` / `userPrincipalName` extraction) is preserved.
- All existing `try`/`catch` branches (`MsalException`, generic token exception, `ODataError`, `UnauthorizedAccessException`, generic `Exception`) continue to return `new List<UserDto>()` and log via the same templates and structured properties.
- The `Authorization` header continues to be set per request (necessary because the token is per-call and `HttpClient` instances returned by `IHttpClientFactory` are shared — setting it on `DefaultRequestHeaders` is acceptable for the lifetime of the returned instance, but the team should not rely on it persisting; per-request `HttpRequestMessage` is preferred. See NFR-3.).
- `CancellationToken` continues to flow into `GetAsync` and `ReadAsStringAsync`.

### FR-4: Lifecycle correctness
The injected `IHttpClientFactory` is a singleton; `GraphService` is registered as scoped. The change must not introduce captive-dependency or lifetime mismatch warnings.

**Acceptance criteria:**
- `GraphService` remains registered as `Scoped` (unchanged from current).
- No `using` block disposes the `HttpClient` returned from `IHttpClientFactory.CreateClient(...)` — disposal is the factory's responsibility. (Disposing a factory-provided client is non-fatal but defeats pooling; the change must remove the `using` keyword on the client.)
- `dotnet build` produces no new analyzer warnings (`CA2000`, `CA1816`, etc.) for `GraphService`.

## Non-Functional Requirements

### NFR-1: Performance
- The change must reduce the number of TCP connections opened to `graph.microsoft.com` under load. The expected steady-state behavior is that all `GraphService` calls within a `SocketsHttpHandler` rotation window (default 2 minutes per `IHttpClientFactory`) share a pooled connection.
- No regression in latency for cached hits (still served from `IMemoryCache` without touching the factory).
- No regression in latency on cache miss beyond normal variance; the factory's handler reuse should reduce, not increase, per-call cost.

### NFR-2: Security
- No new secrets, configuration values, or credentials introduced.
- The Graph token is still acquired through `ITokenAcquisition.GetAccessTokenForAppAsync` and attached per call; the token must never be logged.
- No change to existing OAuth scopes (`https://graph.microsoft.com/.default`) or application-permission posture.

### NFR-3: Maintainability and correctness
- The named-client identifier `"MicrosoftGraph"` should be defined as a `private const string` (e.g., `GraphHttpClientName`) on `GraphService` to avoid string drift between registration and consumption. The same constant must be referenced from `UserManagementModule` (either by exposing it as `public const` or by duplicating the literal with a code comment cross-referencing the class — preferred is `public const string` on `GraphService` for single source of truth).
- The previous `using` declaration on the HTTP client must be removed; do not dispose factory-provided clients.
- Setting `DefaultRequestHeaders.Authorization` on a factory-provided client is acceptable for this single-call usage and matches the suggested fix in the brief. The implementer may choose to switch to a per-request `HttpRequestMessage` with `Headers.Authorization` set on the request if they wish to be defensive against future code changes that reuse the client across calls; either approach is acceptable as long as behavior is preserved (see Open Questions OQ-1).

### NFR-4: Observability
- All existing log statements, structured properties, and log levels must be preserved verbatim.
- No new log statements are required by this change; if added, they must be at `Debug` or `Information` and must not log token contents or full response bodies above what exists today.

### NFR-5: Backward compatibility
- The change is purely internal to the `Anela.Heblo.Application` assembly. No public API, DTO, MediatR contract, or controller signature changes.
- No database migrations, configuration keys, or environment variable changes are introduced.

## Data Model
No data-model changes. `UserDto` (returned shape) is unchanged. `IMemoryCache` entries remain `List<UserDto>` keyed by `group_members_{groupId}`.

## API / Interface Design

### Service interface (unchanged)
```csharp
public interface IGraphService
{
    Task<List<UserDto>> GetGroupMembersAsync(string groupId, CancellationToken cancellationToken = default);
}
```

### Constructor (changed)
```csharp
public GraphService(
    ITokenAcquisition tokenAcquisition,
    IMemoryCache cache,
    ILogger<GraphService> logger,
    IHttpClientFactory httpClientFactory)
{
    _tokenAcquisition = tokenAcquisition;
    _cache = cache;
    _logger = logger;
    _httpClientFactory = httpClientFactory;
}
```

### Client acquisition inside `GetGroupMembersAsync` (changed)
Replace lines 84–85 of `GraphService.cs`:

```csharp
// Before
using var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", graphToken);

// After
var httpClient = _httpClientFactory.CreateClient(GraphHttpClientName);
httpClient.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", graphToken);
```

with `private const string GraphHttpClientName = "MicrosoftGraph";` (or equivalent shared constant — see NFR-3).

### DI registration in `UserManagementModule.AddUserManagement` (changed)
Inside the existing `else` branch (production path):

```csharp
services.AddHttpClient(GraphService.GraphHttpClientName);
services.AddScoped<IGraphService, GraphService>();
```

No changes to the mock branch.

## Dependencies
- `Microsoft.Extensions.Http` — already transitively available in the API layer via ASP.NET Core; verify it is present in `Anela.Heblo.Application.csproj`. If not, add the package reference. (`IHttpClientFactory` and `services.AddHttpClient` live in this package.)
- `Microsoft.Identity.Web` (unchanged) — still provides `ITokenAcquisition`.
- `Microsoft.Extensions.Caching.Memory` (unchanged).
- No new runtime services. The host already registers `IHttpClientFactory` whenever any module calls `services.AddHttpClient(...)`; the API layer already calls `AddHttpClient` for other clients (verify during implementation; if absent, `AddHttpClient` calls in this module are sufficient to bootstrap the factory).

## Testing Requirements

### Unit tests (new)
Add a `GraphServiceTests` class under `backend/test/...UserManagement/Services/` (mirror existing test project layout):

- **Constructor wiring**: Resolving `GraphService` from a `ServiceCollection` that registered `AddHttpClient("MicrosoftGraph")` succeeds without throwing.
- **Cache hit**: When `_cache.TryGetValue` returns a populated list, `IHttpClientFactory.CreateClient` is **not** invoked.
- **Cache miss invokes factory**: When the cache is empty and token acquisition succeeds, `IHttpClientFactory.CreateClient("MicrosoftGraph")` is invoked exactly once per call. Use a mocked `IHttpClientFactory` returning an `HttpClient` backed by a stub `HttpMessageHandler` that returns a canned Graph payload; assert the resulting `List<UserDto>` matches the parsed payload and is cached.
- **Failure modes preserved**: Stub handler returns non-2xx, token acquisition throws `MsalException`, handler throws `HttpRequestException` — each path returns `new List<UserDto>()` (preserved behavior).
- **No `using` on factory client**: Assert (by mock verification) that the factory's `HttpClient` is not disposed by `GraphService` (either by asserting the underlying `HttpMessageHandler` is not disposed by the SUT, or by relying on the mock to throw if `Dispose` is called).

Coverage target: cover all new branches; overall service coverage must meet or exceed the project's 80% threshold for the file.

### Integration / smoke
- No integration test against the live Graph API is in scope. Manual smoke verification: start the API locally with production auth wiring, invoke an endpoint that calls `GetGroupMembers`, confirm a successful member list is returned and no `SocketException` is observed across two consecutive cache-miss windows (>20 minutes apart, or by clearing cache).

### Build/format gates
- `dotnet build` must succeed with no new warnings.
- `dotnet format` must produce no diff after the change.
- All existing tests in the `UserManagement` namespace must continue to pass.

## Out of Scope
- Switching from raw HTTP calls to the `GraphServiceClient` SDK (Microsoft.Graph). The comment at `UserManagementModule.cs:28–29` mentions this as a future direction, but this change is limited to fixing the lifetime issue with the existing raw-HTTP approach.
- Replacing the in-memory cache with a distributed cache.
- Adding Polly resilience policies (retry/circuit breaker) on the named client. These are sensible follow-ups but are not part of this fix.
- Changing the cache TTL, cache key strategy, or eager refresh behavior.
- Modifying `MockGraphService` or the mock-auth code path.
- Adding paging support to handle Graph's `@odata.nextLink` (existing behavior is preserved as-is, even though it does not page).
- Changing logging templates, levels, or structured property names beyond what is required.

## Open Questions
None.

## Status: COMPLETE

