I have enough to write the review. Producing the architecture review now.

```markdown
# Architecture Review: Move `GetGroupMembers` Endpoint Out of `ManufactureOrderController`

## Skip Design: true
No new or modified visual components. `ResponsiblePersonCombobox` gains a `groupId` prop but the rendered UI, layout, states (loading/error/disabled), and visual treatment are unchanged. Pure backend + wiring refactor.

## Architectural Fit Assessment

The proposed direction aligns cleanly with the codebase's Vertical Slice + one-controller-per-module rule:

- **`BaseApiController` + `HandleResponse<T>`** is already the canonical pattern for routing `BaseResponse.Success/ErrorCode` to HTTP status codes — the new controller plugs into it with no new infrastructure.
- **`[Route("api/[controller]")]`** is the dominant routing style (Catalog, Manufacture*, Configuration, FeatureFlags use it; only a few like `PurchaseOrdersController` use kebab). New `UserManagementController` matches the majority.
- **MCP tool classes** are registered uniformly in `McpModule.cs`; adding `UserManagementMcpTools` mirrors the existing chain (`WithTools<...>`).
- **`GetConfigurationResponse`** is already the FE-facing config envelope (TS client method `configuration_GetConfiguration`); extending it for `ManufactureGroupId` follows existing precedent.

Two integration realities **the spec does not fully account for** and must be reflected before implementation:

1. **`ConfigurationController` is currently anonymous (no `[Authorize]`)** — verified at `ConfigurationController.cs:7-9`. That endpoint is intentionally callable pre-auth so the SPA can decide `useMockAuth`. Exposing `manufactureGroupId` through it makes the value readable without authentication. The group ID is not a secret (NFR-2 already calls it non-sensitive), so this is acceptable — but the spec text "the configuration response is already returned to authenticated clients" is **factually wrong** and must be corrected.

2. **There is no `useConfiguration()` React hook or app-config context in the frontend** — verified by grep. `runtimeConfig.ts` is a build-time env-var singleton (does not call `/api/Configuration`), and `versionService.ts` calls the API directly without exposing a hook. FR-6's wording "use a central configuration consumer hook (e.g. `useConfiguration()` or the existing app-config context provider)" reads as if one exists; it does not. The implementation must introduce one.

## Proposed Architecture

### Component Overview

```
┌────────────────────────┐   GET /api/UserManagement/group-members?groupId={id}
│  Frontend              │ ──────────────────────────────────────────────────┐
│  ResponsiblePersonCmb  │                                                   │
│       │                │   GET /api/Configuration  (anonymous)             │
│       ▼                │ ──────────────────────────────────────────────┐   │
│  useResponsiblePersons │                                               │   │
│  Query(groupId)        │                                               │   │
│       ▲                │                                               │   │
│  useConfigurationQuery │  ◄── new hook (single fetch, staleTime ∞)     │   │
└────────────────────────┘                                               │   │
                                                                         ▼   ▼
                       ┌───────────────────────────────────────────────────────────┐
                       │  Anela.Heblo.API                                          │
                       │   ┌──────────────────────────┐  ┌──────────────────────┐  │
                       │   │ ConfigurationController  │  │ UserManagementCtrl   │  │
                       │   │  (anonymous)             │  │  [Authorize]         │  │
                       │   │  +manufactureGroupId     │  │  GET group-members   │  │
                       │   └─────────────┬────────────┘  └──────────┬───────────┘  │
                       │   ┌─────────────▼────────────┐  ┌──────────▼───────────┐  │
                       │   │ GetConfigurationHandler  │  │ GetGroupMembers      │  │
                       │   │  reads IConfiguration    │  │   Handler            │  │
                       │   │  ["ManufactureGroupId"]  │  │  → IGraphService     │  │
                       │   └──────────────────────────┘  └──────────────────────┘  │
                       │                                                           │
                       │   ManufactureOrderController                              │
                       │   ─── GetResponsiblePersons / IConfiguration REMOVED      │
                       │                                                           │
                       │   MCP: UserManagementMcpTools.GetGroupMembers (new)       │
                       │   MCP: ManufactureOrderMcpTools.GetResponsiblePersons     │
                       │        REMOVED                                            │
                       └───────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Validate `groupId` via `[ApiController]` model-state, not manual `string.IsNullOrEmpty`
**Options considered:**
- (A) Decorate parameter with `[Required, FromQuery] string groupId` and rely on `[ApiController]`'s automatic `ProblemDetails` 400 response.
- (B) Manual `if (string.IsNullOrWhiteSpace(groupId)) return BadRequest(...)` returning a `GetGroupMembersResponse` with `ErrorCode`.

**Chosen approach:** (A), with one caveat: the project's other validation failures travel through `BaseResponse` shaped JSON (see `ErrorResponseHelper.CreateValidationError` in `PurchaseOrdersController`). For a single string query parameter, `[Required]` + `[ApiController]` automatic 400 is the simplest path and the spec already prefers it (FR-1 acceptance criteria). Keep the action body to: build request → `_mediator.Send` → `HandleResponse`.

**Rationale:** Matches the spec's "action body is at most" requirement; eliminates the service-locator logger; consistent with how `[ApiController]` works in the rest of the codebase for trivial scalar validation.

#### Decision 2: Expose `manufactureGroupId` via existing anonymous `ConfigurationController`
**Options considered:**
- (A) Add field to current anonymous `GetConfigurationResponse` (spec choice).
- (B) Add `[Authorize]` to `ConfigurationController` to gate the new field (would break pre-auth `versionService.checkVersion`).
- (C) Split into anonymous `/api/Configuration/public` (version, env, mockAuth) and authorized `/api/Configuration/user` (group IDs, future tenant settings).

**Chosen approach:** (A).
**Rationale:** Group ID is a non-sensitive Entra identifier (see NFR-2). The value is already injected via env var in `appsettings.Production.json` and used at runtime; making it readable pre-auth does not expose a secret. (B) would break version-check at startup. (C) is YAGNI for a single field. **Amend spec to acknowledge the endpoint is anonymous, not "returned to authenticated clients."**

#### Decision 3: Introduce a single `useConfigurationQuery` hook on the frontend
**Options considered:**
- (A) Add `useConfigurationQuery` in `frontend/src/api/hooks/useConfiguration.ts`, used by `versionService` *and* the three manufacture callers.
- (B) Each caller invokes `apiClient.configuration_GetConfiguration()` ad-hoc.
- (C) Push group ID through a top-level React context provider populated at app boot.

**Chosen approach:** (A) — a tiny React Query hook with `staleTime: Infinity` and `gcTime: Infinity`. `versionService` can keep its current path (it has version-specific polling logic) without touching that area; the new hook covers UI consumers.

**Rationale:** A `staleTime: Infinity` React Query gives every consumer the same cached payload via the query cache — no re-fetch per call site (FR-6 acceptance criterion). Avoids the broader refactor of context-providers (out of scope). One file, one hook.

#### Decision 4: Keep `useResponsiblePersonsQuery` enabled only when `groupId` is non-empty
**Options considered:**
- (A) `enabled: Boolean(groupId)` on `useQuery`, return loading state until config resolves.
- (B) Default to fetch immediately with an empty `groupId`, server returns 400, hook surfaces error.

**Chosen approach:** (A). Combobox's `isDisabled` is set when `!groupId || isLoading` — preserves current "manual entry" affordance as soon as the group ID arrives.
**Rationale:** Avoids guaranteed-to-fail backend round-trip during the brief window before configuration resolves. Aligns with FR-5 acceptance criterion.

#### Decision 5: Use constructor-injected `BaseApiController.Logger`, not service-locator
**Options considered:**
- (A) Use `BaseApiController.Logger` property (existing pattern, lazy-resolves via `ILoggerFactory`).
- (B) Inject `ILogger<UserManagementController>` via constructor.
- (C) Do no controller-side logging; rely on `GetGroupMembersHandler` (which already logs).

**Chosen approach:** (C), consistent with FR-3.
**Rationale:** The handler already logs request entry and exception path. Adding controller-side logging duplicates effort and re-introduces a logger dependency. Note: the handler currently does **not** log a success/count line — that is a minor pre-existing observability gap; flagged below, not blocking.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.API/
├── Controllers/
│   ├── UserManagementController.cs          [NEW]
│   └── ManufactureOrderController.cs        [MODIFIED — remove GetResponsiblePersons,
│                                                          remove IConfiguration ctor param if unused,
│                                                          remove using of UserManagement.GetGroupMembers]
└── MCP/Tools/
    ├── UserManagementMcpTools.cs            [NEW]
    └── ManufactureOrderMcpTools.cs          [MODIFIED — remove GetResponsiblePersons,
                                                          remove UserManagement using]

backend/src/Anela.Heblo.API/MCP/
└── McpModule.cs                             [MODIFIED — add WithTools<UserManagementMcpTools>()]

backend/src/Anela.Heblo.Application/Features/Configuration/
├── GetConfigurationResponse.cs              [MODIFIED — add string? ManufactureGroupId]
└── GetConfigurationHandler.cs               [MODIFIED — populate from IConfiguration["ManufactureGroupId"]]

backend/test/Anela.Heblo.Tests/
├── Controllers/
│   ├── UserManagementControllerTests.cs     [NEW]
│   └── ManufactureOrderControllerTests.cs   [MODIFIED — delete 4 GetResponsiblePersons tests]
└── Features/
    └── Configuration/
        └── GetConfigurationHandlerTests.cs  [NEW or EXTENDED — cover ManufactureGroupId]

frontend/src/api/hooks/
├── useConfiguration.ts                      [NEW — useConfigurationQuery hook]
└── useUserManagement.ts                     [MODIFIED — signature + URL + queryKey]

frontend/src/components/common/
└── ResponsiblePersonCombobox.tsx            [MODIFIED — add required groupId prop]
└── __tests__/ResponsiblePersonCombobox.test.tsx [MODIFIED — pass groupId in tests]

frontend/src/components/modals/
└── CreateManufactureOrderModal.tsx          [MODIFIED — read manufactureGroupId, pass to combobox]

frontend/src/components/manufacture/detail/
└── BasicInfoSection.tsx                     [MODIFIED — same]

frontend/src/components/manufacture/list/
└── ManufactureOrderFilters.tsx              [MODIFIED — same]

docs/integrations/
└── mcp-server.md                            [MODIFIED — section headers + new bullet]
```

### Interfaces and Contracts

**Backend — `UserManagementController` (new):**
```csharp
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class UserManagementController : BaseApiController
{
    private readonly IMediator _mediator;

    public UserManagementController(IMediator mediator) => _mediator = mediator;

    [HttpGet("group-members")]
    [ProducesResponseType(typeof(GetGroupMembersResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<GetGroupMembersResponse>> GetGroupMembers(
        [FromQuery, Required] string groupId,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetGroupMembersRequest { GroupId = groupId }, cancellationToken);
        return HandleResponse(response);
    }
}
```

**Backend — `GetConfigurationResponse` (extended):**
```csharp
public class GetConfigurationResponse : BaseResponse
{
    public string Version { get; set; } = default!;
    public string Environment { get; set; } = default!;
    public bool UseMockAuth { get; set; }
    public DateTime Timestamp { get; set; }
    public string? ManufactureGroupId { get; set; }   // NEW — nullable; null when config key missing
}
```

**Backend — `GetConfigurationHandler` (extended):**
- In `BuildApplicationConfigurationAsync`, read `_configuration["ManufactureGroupId"]`.
- Assign to response. Do **not** throw on missing — nullable by design.
- (Optional but recommended) extend the internal `ApplicationConfiguration.CreateWithDefaults` chain to carry the value, OR set the property directly on the response. Either is acceptable; the latter is simpler given the field is purely a passthrough.

**Backend — `UserManagementMcpTools` (new):**
- Mirror `ManufactureOrderMcpTools` shape: `[McpServerToolType]` class with `IMediator` ctor, `[McpServerTool]` method `GetGroupMembers(string groupId, CancellationToken ct)`.
- Register in `McpModule.cs`: append `.WithTools<UserManagementMcpTools>()`.

**Frontend — `useConfigurationQuery` (new):**
```typescript
import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';

export const useConfigurationQuery = () => useQuery({
  queryKey: ['configuration'],
  queryFn: async () => {
    const apiClient = await getAuthenticatedApiClient();
    return apiClient.configuration_GetConfiguration();
  },
  staleTime: Infinity,
  gcTime: Infinity,
  retry: 1,
});
```

**Frontend — `useResponsiblePersonsQuery` (signature change):**
```typescript
export const useResponsiblePersonsQuery = (groupId: string) =>
  useQuery({
    queryKey: [...QUERY_KEYS.userManagement, 'group-members', groupId],
    enabled: Boolean(groupId),
    queryFn: async (): Promise<GetGroupMembersResponse> => {
      const apiClient = await getAuthenticatedApiClient();
      const relativeUrl = `/api/UserManagement/group-members?groupId=${encodeURIComponent(groupId)}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: { 'Content-Type': 'application/json' },
      });
      if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);
      return response.json();
    },
    staleTime: 15 * 60 * 1000,
    retry: 2,
    retryDelay: 1000,
  });
```

**Frontend — `ResponsiblePersonCombobox` prop addition:**
```typescript
interface ResponsiblePersonComboboxProps {
  groupId: string;                            // NEW, required — drives query
  value?: string | null;
  onChange: (value: string | null) => void;
  placeholder?: string;
  disabled?: boolean;
  error?: string;
  className?: string;
  allowManualEntry?: boolean;
}
```
Inside the component: `useResponsiblePersonsQuery(groupId)`; combine `disabled || !groupId || isLoading` for the Select's `isDisabled`. Manual-entry path remains available so users can still type when the directory call hasn't resolved or fails.

**Each manufacture caller** reads `manufactureGroupId` from `useConfigurationQuery()` and forwards:
```typescript
const { data: config } = useConfigurationQuery();
// ...
<ResponsiblePersonCombobox
  groupId={config?.manufactureGroupId ?? ''}
  value={...}
  onChange={...}
/>
```

### Data Flow

**Read-time (typical manufacture screen):**
1. SPA mounts → `useConfigurationQuery` fires → `GET /api/Configuration` (anonymous) → backend reads `IConfiguration["ManufactureGroupId"]` → returns payload with `manufactureGroupId`.
2. `useConfigurationQuery` cache holds it forever (`staleTime: Infinity`).
3. User opens any of the three manufacture screens → component reads `manufactureGroupId` from cache → passes to `ResponsiblePersonCombobox`.
4. Combobox calls `useResponsiblePersonsQuery(groupId)` → `GET /api/UserManagement/group-members?groupId=…` → `GetGroupMembersHandler` → `IGraphService.GetGroupMembersAsync` → returns members.
5. Combobox renders dropdown.

**MCP flow (unchanged conceptually):**
- AI client → `UserManagementMcpTools.GetGroupMembers(groupId)` → `_mediator.Send(new GetGroupMembersRequest { GroupId = groupId })` → handler → graph → serialized JSON.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `ConfigurationController` is anonymous; exposing `manufactureGroupId` makes it readable pre-auth. Spec wording calls it "returned to authenticated clients" — that is incorrect. | Medium | Group ID is a non-sensitive Entra identifier. Accept the anonymous exposure. Correct the spec wording (see Amendments). Add a unit test that the field is present in the anonymous response. |
| Three manufacture call sites currently pre-fetch responsible persons on render; gating on `groupId` introduces a brief loading window before config resolves. | Low | `useConfigurationQuery` is fired at app shell level (recommend invoking it once in `App.tsx` or layout) so the result is cached by the time these screens mount. `staleTime: Infinity` ensures one network hop per session. |
| `[Required]` on a `string` query parameter accepts whitespace-only input (`groupId=%20`) → would 200 from `[ApiController]` validation, then `IGraphService` likely errors. | Low | `GetGroupMembersHandler` already wraps in try/catch and returns `BaseResponse.Success = false`. Optionally validate non-whitespace in the handler or use `[Required, MinLength(1)]`. Document either choice. |
| `OperationId` collisions / OpenAPI client churn — removing `ManufactureOrder_GetResponsiblePersons` deletes a TS client method that may be referenced elsewhere. | Low | Grep `manufactureOrder_GetResponsiblePersons` in `frontend/src/` after API codegen; the spec mentions only `useResponsiblePersonsQuery` uses raw fetch. Confirmed by grep — no generated-method consumers. |
| Handler does not log success/count line, so observability silently regresses vs. current controller behavior. | Low | Optional: add `_logger.LogInformation("GetGroupMembers returned {Count} members for {GroupId}", members.Count, request.GroupId)` in `GetGroupMembersHandler`. Out of scope per spec, but cheap; treat as nice-to-have. |
| Tests in `ManufactureOrderControllerTests.cs` mock `IConfiguration`; after `IConfiguration` is removed from the controller, the entire ctor signature changes and unrelated tests break compilation. | Medium | Verify `ManufactureOrderController` still needs `IConfiguration` for any remaining action (grep `_configuration` within the file — currently used **only** by `GetResponsiblePersons`). When removed, update all existing test constructions to drop the `IConfiguration` mock argument. Include this fan-out in the PR. |
| New `useConfigurationQuery` overlaps with `versionService.checkVersion()` — two independent paths fetching `/api/Configuration`. | Low | Acceptable: `versionService` polls every 5 min for version changes; `useConfigurationQuery` caches once. Different concerns. Not worth coupling. |

## Specification Amendments

1. **NFR-2 wording fix.** Replace:
   > "the configuration response is already returned to authenticated clients and follows the existing pattern for safe-to-expose config values."

   with:
   > "`ConfigurationController` is intentionally anonymous (no `[Authorize]`) so the SPA can fetch version and `useMockAuth` before sign-in. `ManufactureGroupId` is a non-sensitive Entra group identifier and is acceptable to expose anonymously."

2. **FR-1 acceptance criterion correction.** "Controller must be decorated with `[Authorize]` and `[ApiController]`, matching surrounding controllers" — clarify that `UserManagementController` itself is `[Authorize]` (matching `ManufactureOrderController`), but **not** every neighbour is (`ConfigurationController` is intentionally anonymous). This avoids reviewer confusion when comparing.

3. **FR-6 acceptance criterion — explicitly introduce `useConfigurationQuery`.** Replace:
   > "A central configuration consumer hook (e.g. `useConfiguration()` or the existing app-config context provider) is used; do not re-fetch `/api/Configuration` per call site."

   with:
   > "Introduce `frontend/src/api/hooks/useConfiguration.ts` exporting `useConfigurationQuery()` that wraps `apiClient.configuration_GetConfiguration()` with `staleTime: Infinity` and `gcTime: Infinity`. All three manufacture call sites consume this hook to read `manufactureGroupId`. No new React context is required."

4. **FR-1 validation behaviour — clarify the `[Required]` path.** The action will return ASP.NET Core's default `ValidationProblemDetails` 400 (driven by `[ApiController]`), not a `BaseResponse`-shaped 400. This differs from `HandleResponse`-routed 400s and is the simplest, idiomatic answer. The frontend hook treats any non-2xx as a thrown `Error`, so the payload shape difference is invisible to the UI. Document this explicitly.

5. **NFR-4 — add `UserManagement` test folder.** New `UserManagementControllerTests.cs` belongs under `backend/test/Anela.Heblo.Tests/Controllers/` (matching existing convention); no separate folder is needed.

6. **FR-2 — drop `IConfiguration` from constructor.** Grep confirms `_configuration` in `ManufactureOrderController.cs` is referenced **only** by the deleted method (`line 158`). Removing the dependency requires updating every `new ManufactureOrderController(...)` call in tests (`ManufactureOrderControllerTests.cs`, ~10 occurrences expected). Add this as an explicit subtask.

7. **Optional handler logging (recommended).** Add success-line log in `GetGroupMembersHandler` to preserve observability lost when removing controller-side logs:
   ```csharp
   _logger.LogInformation("GetGroupMembers returned {Count} members for group {GroupId}", members.Count, request.GroupId);
   ```
   Mark this as a nice-to-have under FR-3, not a blocker.

## Prerequisites

None blocking. Verify before implementation:

- **`appsettings.Production.json` (and any Azure App Service env var)** already injects `ManufactureGroupId` — confirmed at `appsettings.Production.json:37`. No infrastructure change needed.
- **No external/public consumers of the removed URL** — confirmed by spec (NFR-3); solo developer project.
- **OpenAPI client regenerates on `npm run build`** — confirmed by existing pipeline; the new `manufactureGroupId` field will appear on `GetConfigurationResponse` automatically.
- **MCP tool registration site** — `McpModule.cs:13-22`. Single-line addition; no DI plumbing changes.
- **`appsettings.Development.json`** — verify a `ManufactureGroupId` value (or omit the key to test the null-branch). If missing, the field is `null` and the combobox stays disabled; that is acceptable but should be explicitly tested.
```