Now I have a complete picture. Let me write the architecture review.

# Architecture Review: GraphService HttpClientFactory Migration

## Skip Design: true

## Architectural Fit Assessment

This change is a **textbook fit** for existing conventions. The codebase already has five modules that register and consume a `"MicrosoftGraph"` named client through `IHttpClientFactory`:

- `MarketingModule` → `OutlookCalendarSyncService`
- `MeetingTasksModule` → `GraphPlannerService`
- `CatalogDocumentsModule` → `GraphCatalogDocumentsStorage`
- `KnowledgeBaseModule` → `GraphOneDriveService`
- `PhotobankModule` → `PhotobankGraphService`

`UserManagementModule.GraphService` is the **only Graph caller still on raw `new HttpClient()`**. Bringing it onto the existing pattern closes a known gap rather than introducing new architecture. The integration points are surgical: one constructor change, one DI registration, one client-acquisition line. Test patterns (`Mock<IHttpClientFactory>` + `FakeHttpMessageHandler`) are well-established in `backend/test/Anela.Heblo.Tests/Helpers/` and mirrored across the five sister services.

One **latent issue** surfaced during exploration that the spec does not address (see Risks): multiple modules independently register `services.AddHttpClient("MicrosoftGraph")`, and `PhotobankModule` adds a custom `HttpClientHandler`. With named clients, last registration wins for handler config — registration order is not deterministic across modules. The UserManagement change does not worsen this, but the team should know.

## Proposed Architecture

### Component Overview

```
GetGroupMembersHandler (MediatR)
        │ (unchanged)
        ▼
IGraphService  ── implemented by ──▶ GraphService (scoped)
                                          │
                                          ├─▶ ITokenAcquisition  (unchanged)
                                          ├─▶ IMemoryCache       (unchanged)
                                          ├─▶ ILogger<…>         (unchanged)
                                          └─▶ IHttpClientFactory ◀── NEW
                                                  │
                                                  └─ CreateClient("MicrosoftGraph")
                                                       │
                                                       ▼
                                              SocketsHttpHandler (pooled, default 2-min rotation)
                                                       │
                                                       ▼
                                              graph.microsoft.com (per-request Bearer)
```

DI wiring (production branch of `UserManagementModule.AddUserManagement`):

```
services.AddHttpClient("MicrosoftGraph");      // NEW — match sister modules
services.AddScoped<IGraphService, GraphService>();  // existing
```

### Key Design Decisions

#### Decision 1: Use the literal `"MicrosoftGraph"` — do **not** introduce a `GraphHttpClientName` constant on `GraphService`

**Options considered:**
- (A) Add `public const string GraphHttpClientName = "MicrosoftGraph"` on `GraphService` and reference it from `UserManagementModule` (the spec's NFR-3 proposal).
- (B) Add a shared constant in a neutral location (e.g., `Anela.Heblo.Application.Common.HttpClientNames`).
- (C) Duplicate the literal `"MicrosoftGraph"` in `GraphService` and `UserManagementModule`, matching the existing five-module convention exactly.

**Chosen approach:** (C). Duplicate the literal in both places. Add a one-line comment in each cross-referencing the convention.

**Rationale:** None of the five existing Graph-consuming modules use a constant. Introducing one only inside UserManagement would create asymmetric drift: future developers grepping for the convention would find six call sites using the literal and one using a constant, breaking the pattern they came to extend. Option (B) would be the *right* refactor — but it would require touching all five other modules to be consistent, which is explicitly out of scope. Option (A) couples the module's DI wiring to a public constant on a concrete service implementation, which is a worse coupling than two duplicated string literals. The spec should be amended (see Specification Amendments).

#### Decision 2: Per-request `HttpRequestMessage` instead of `DefaultRequestHeaders.Authorization`

**Options considered:**
- (A) Keep `httpClient.DefaultRequestHeaders.Authorization = …` per the spec.
- (B) Build an `HttpRequestMessage` per call and set `request.Headers.Authorization`.

**Chosen approach:** (B). Use a per-request `HttpRequestMessage`.

**Rationale:** Factory-provided clients are recycled between scopes; the underlying handler chain is shared across consumers. Mutating `DefaultRequestHeaders` on a shared instance is a known footgun — today there is only one call site, but the moment another `GraphService` method is added (or someone reuses the same named client elsewhere in this module), tokens can leak across calls. The sister services (`OutlookCalendarSyncService`, `GraphCatalogDocumentsStorage`, `GraphPlannerService`) are split — some use `DefaultRequestHeaders`, some don't. Per-request headers cost one extra line and remove the entire class of cross-call header-leak bugs. The spec's NFR-3 explicitly leaves this open and recommends per-request as "defensive against future code changes" — adopt that recommendation.

#### Decision 3: Register `AddHttpClient("MicrosoftGraph")` from `UserManagementModule` even though other modules already register it

**Options considered:**
- (A) Add `services.AddHttpClient("MicrosoftGraph")` in `UserManagementModule` (spec's FR-2).
- (B) Skip registration in `UserManagementModule`, relying on the other five modules to have registered it.

**Chosen approach:** (A). Register it locally, matching the existing convention.

**Rationale:** Each Graph-consuming module today registers its own `AddHttpClient("MicrosoftGraph")` defensively. This makes module load order irrelevant and lets each module remain independently composable (e.g., a test that loads only `UserManagementModule` should still work). The cost is a duplicate registration in DI, which `AddHttpClient` tolerates idempotently for naming purposes — though handler config races, see Risks.

## Implementation Guidance

### Directory / Module Structure

No new directories. Files touched:

```
backend/src/Anela.Heblo.Application/Features/UserManagement/
  ├── Services/GraphService.cs          ← constructor + client acquisition
  └── UserManagementModule.cs           ← AddHttpClient registration

backend/test/Anela.Heblo.Tests/Features/UserManagement/
  └── GraphServiceTests.cs              ← NEW
```

The new test file is the first unit test against `GraphService` (currently only `MockGraphServiceTests` and `GetGroupMembersHandlerTests` exist for this namespace).

### Interfaces and Contracts

**Unchanged:**
- `IGraphService.GetGroupMembersAsync(string, CancellationToken)` — public contract preserved.
- `UserDto` — response shape preserved.
- `IMemoryCache` keying (`group_members_{groupId}`) and TTL (20 min) — preserved.

**Changed (internal):**
- `GraphService` constructor adds `IHttpClientFactory httpClientFactory` as the **fourth** parameter (appended; do not reorder existing parameters to minimize diff blast radius and any reflection-based test wiring).

### Data Flow

Cache miss path:

```
Handler.Handle
  → GraphService.GetGroupMembersAsync(groupId, ct)
    → _cache.TryGetValue("group_members_{groupId}") → miss
    → _tokenAcquisition.GetAccessTokenForAppAsync("https://graph.microsoft.com/.default")
    → _httpClientFactory.CreateClient("MicrosoftGraph")        // pooled handler
    → new HttpRequestMessage(HttpMethod.Get, "<graph url>")
        .Headers.Authorization = "Bearer <token>"               // per-request
    → client.SendAsync(request, ct)
    → parse JSON → List<UserDto>
    → _cache.Set("group_members_{groupId}", members, 20 min)
    → return members
```

Cache hit path: unchanged. Factory is **not** invoked on a hit.

Error paths (token failure, non-2xx, OData error, unauthorized, generic exception): unchanged — all five existing `catch` branches preserved verbatim with their structured log templates.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Multiple modules register `"MicrosoftGraph"` with conflicting handler configs (`PhotobankModule` adds `AllowAutoRedirect`); last registration wins, undefined load order. | Medium | Out of scope for this change but document as a known issue. Recommend a follow-up to consolidate Graph client registration in one place (e.g., a shared `AddMicrosoftGraphHttpClient()` extension) and to apply `SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }` uniformly, matching the `FileStorageModule` pattern. |
| `DefaultRequestHeaders.Authorization` leaks across calls if the same named client is reused by future code. | Low | Decision 2 above — use per-request `HttpRequestMessage`. |
| Disposing the factory-provided `HttpClient` (forgotten `using`) defeats pooling. | Low | Code review checks; the change explicitly removes the `using` keyword. Add a unit test that fails if `Dispose` is called on the SUT's client (spec FR-4 already requires this). |
| Test using a real `HttpClient` over a stub handler accidentally hits the network. | Low | Use the existing `FakeHttpMessageHandler` helper from `backend/test/Anela.Heblo.Tests/Helpers/`; never construct a handler that defers to the default one. |
| `Microsoft.Extensions.Http` package not directly referenced in `Anela.Heblo.Application.csproj`. | Negligible | Five existing modules already call `services.AddHttpClient(...)` from this project — the package is transitively present. No package change needed. Verify during implementation; if `AddHttpClient` resolves today, it will continue to. |

## Specification Amendments

1. **NFR-3 — drop the `GraphHttpClientName` constant requirement.** Use the literal `"MicrosoftGraph"` in both `GraphService` and `UserManagementModule` to match the existing five-module convention. Rationale: see Decision 1. If a shared constant is desired in the future, it should be introduced as a separate refactor that touches all six modules consistently. Add a one-line code comment in each location: `// Matches the shared "MicrosoftGraph" named client used by Marketing/MeetingTasks/CatalogDocuments/KnowledgeBase/Photobank modules.`

2. **FR-3 / NFR-3 — switch to per-request `HttpRequestMessage`.** The "either approach is acceptable" language should be tightened to **require** per-request headers. Replace the After block with:

   ```csharp
   var httpClient = _httpClientFactory.CreateClient("MicrosoftGraph");
   using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
   request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", graphToken);
   var response = await httpClient.SendAsync(request, cancellationToken);
   ```

   The `GetAsync(...)` call moves to `SendAsync(request, cancellationToken)`. Rationale: see Decision 2. This is a one-line cost for a meaningful safety improvement.

3. **Open Question OQ-1 referenced in NFR-3 but the spec lists "Open Questions: None".** Resolve by adopting Decision 2 above.

4. **Test plan — add follow-up note.** The "Multiple modules register `MicrosoftGraph`" race is not introduced by this change but is exposed by it. Add a `## Known Follow-ups` section to the spec noting the consolidation opportunity (single `AddMicrosoftGraphHttpClient()` extension with `PooledConnectionLifetime` and `AllowAutoRedirect`).

## Prerequisites

None. All required infrastructure exists today:

- `IHttpClientFactory` is registered globally by ASP.NET Core hosting (and bootstrapped by any of the five existing `AddHttpClient` calls).
- `Microsoft.Extensions.Http` is transitively available in `Anela.Heblo.Application`.
- Test helpers (`FakeHttpMessageHandler`) and patterns (`Mock<IHttpClientFactory>.Setup(f => f.CreateClient("MicrosoftGraph"))`) already exist in the test project.
- No configuration, environment variable, migration, or infrastructure change is required.