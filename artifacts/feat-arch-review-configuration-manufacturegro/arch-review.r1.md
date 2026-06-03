I have explored the project enough — verified the Configuration handler/response, the Manufacture module layout, the Manufacture UseCases pattern, the Auth setup (only `[Authorize]`-decorated controllers require auth; `ConfigurationController` is anonymous by omission), the frontend `useConfigurationQuery` hook and its three consumers, the existing test patterns, and the `filesystem.md` conventions.

# Architecture Review: Decouple ManufactureGroupId from Configuration Module

## Skip Design: true

Backend-mostly module-boundary refactor. The only UI-adjacent change is repointing three existing React components from `useConfigurationQuery().manufactureGroupId` to a new `useManufactureSettingsQuery().manufactureGroupId`. No new components, screens, layouts, or visual decisions.

## Architectural Fit Assessment

The proposal is squarely aligned with the codebase's documented Vertical Slice organization (`docs/architecture/filesystem.md`, "Feature autonomy: Each feature manages its own contracts, services, and infrastructure"). The Manufacture module is already a **complex** feature using the `UseCases/{UseCase}/` layout (e.g. `GetCalendarView`, `GetManufactureOrder`, …) — the new endpoint should follow exactly that pattern.

Three concrete existing facts shape the architecture:

- **Authentication is opt-in at the controller level.** `AuthenticationExtensions.cs:104-119` declares a `DefaultPolicy` that requires `HebloUser` role, but it only applies via `[Authorize]`. `ConfigurationController.cs` has no `[Authorize]` attribute and is therefore already anonymous-by-omission. `ManufactureOrderController.cs:19` has `[Authorize]` at class level. **This is the critical constraint:** the new endpoint cannot live inside `ManufactureOrderController` — it must be a separate controller, because mixing `[AllowAnonymous]` methods inside an `[Authorize]`-decorated controller is fragile and obscures the security posture.
- **Cross-cutting config-key constants live in `Domain/Features/Configuration/ConfigurationConstants.cs`,** but feature-scoped constants live in the feature's own `{Feature}Constants.cs` under `Application/Features/{Feature}/` (e.g. `ManufactureConstants.cs`). The Manufacture-owned key belongs in the Manufacture Application folder, not in the cross-cutting Domain constants.
- **Frontend already uses React Query with `staleTime: Infinity` and `gcTime: Infinity`** in `useConfiguration.ts`. Parallelism between two `useQuery` hooks is automatic — no `Promise.all` orchestration in `App.tsx` is needed. The spec's reference to "parallel calls" is satisfied by simply having two independent hooks fetched lazily by the components that consume them.

The integration points are: (1) the new MediatR handler reads `IConfiguration`; (2) the new controller is registered automatically via `app.MapControllers()`; (3) MediatR handler discovery is automatic via assembly scanning (no `ManufactureModule.cs` change required — confirmed by reading the module file: it only registers options/services/adapters, not handlers); (4) NSwag regenerates the TypeScript client on PostBuild.

## Proposed Architecture

### Component Overview

```
┌────────────────────────────────────────────────────────────────────┐
│  SPA (frontend)                                                    │
│                                                                    │
│   useConfigurationQuery() ──► GET /api/configuration   (anonymous) │
│        (returns: version, environment, useMockAuth, timestamp)     │
│                                                                    │
│   useManufactureSettingsQuery() ──► GET /api/manufacture/settings  │
│        (returns: manufactureGroupId)                  (anonymous)  │
└────────────────────────────────────────────────────────────────────┘
                                  │
                                  ▼
┌────────────────────────────────────────────────────────────────────┐
│  Anela.Heblo.API/Controllers/                                      │
│   ┌──────────────────────────────┐  ┌──────────────────────────┐  │
│   │ ConfigurationController       │  │ ManufactureSettingsCtrl │  │
│   │   (anonymous-by-omission)     │  │   [AllowAnonymous]      │  │
│   └──────────────┬────────────────┘  └────────────┬─────────────┘  │
│                  │ IMediator                       │ IMediator     │
└──────────────────┼─────────────────────────────────┼───────────────┘
                   ▼                                 ▼
┌────────────────────────────────────────────────────────────────────┐
│  Anela.Heblo.Application/                                          │
│                                                                    │
│   Features/Configuration/                                          │
│     GetConfigurationHandler                                        │
│        (no longer reads ManufactureGroupId)                        │
│                                                                    │
│   Features/Manufacture/                                            │
│     ManufactureConfigurationKeys.GroupId = "ManufactureGroupId"    │
│     UseCases/GetManufactureSettings/                               │
│       └─ GetManufactureSettingsHandler ─► IConfiguration           │
└────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Separate controller, not a new action on `ManufactureOrderController`
**Options considered:**
- A. Add `[AllowAnonymous] GetSettings()` to `ManufactureOrderController`.
- B. Create a dedicated `ManufactureSettingsController` (no class-level `[Authorize]`).

**Chosen approach:** B.
**Rationale:** `ManufactureOrderController` has `[Authorize]` at the class level. Mixing anonymous and authenticated methods in the same controller hides the security posture and invites accidents (a future refactor that drops a method-level `[AllowAnonymous]` would silently start requiring auth, breaking the SPA bootstrap). Match the established pattern of `ConfigurationController` — a small, single-purpose, anonymous controller.

#### Decision 2: Where to place the constant
**Options considered:**
- A. Add `GroupId = "ManufactureGroupId"` to existing `ManufactureConstants.cs` (mixes numeric domain limits with infra config keys).
- B. New file `ManufactureConfigurationKeys.cs` at `Application/Features/Manufacture/`.
- C. Put it under `Domain/Features/Manufacture/`.

**Chosen approach:** B.
**Rationale:** Existing `ManufactureConstants.cs` is purely domain-numeric (page sizes, difficulty defaults, month-back limits) — adding an infra config-key string would muddle its purpose. The cross-cutting `ConfigurationConstants` (Domain layer) is for app-wide keys (`USE_MOCK_AUTH`, `APP_VERSION`); feature-specific config keys should stay in the feature module. Option C is wrong because config-key plumbing is an Application-layer concern (the Domain layer must not depend on `Microsoft.Extensions.Configuration`).

#### Decision 3: Follow the `UseCases/{UseCase}/` layout for the new handler
**Options considered:**
- A. Flat layout (`GetManufactureSettingsHandler.cs` at module root).
- B. `UseCases/GetManufactureSettings/` folder with Handler, Request, Response.

**Chosen approach:** B.
**Rationale:** Manufacture is already a **complex** feature per `filesystem.md` — all its existing handlers live under `UseCases/`. Consistency wins; a new flat handler would stand out as an exception.

#### Decision 4: Frontend parallelism via React Query, not `Promise.all`
**Options considered:**
- A. Prefetch both queries explicitly in `App.tsx` with `queryClient.prefetchQuery(...)`.
- B. Mirror the existing `useConfiguration.ts` shape with a new `useManufactureSettings.ts`; let React Query handle parallelism naturally.

**Chosen approach:** B.
**Rationale:** The existing hook is lazy-fetched by consumers. Three components (`CreateManufactureOrderModal`, `BasicInfoSection`, `ManufactureOrderFilters`) call `useConfigurationQuery()` — they will now also call `useManufactureSettingsQuery()`. React Query dedupes and parallelizes automatically. Adding explicit prefetch in `App.tsx` would change the loading model and is not required by the spec's NFR-1 (parallelism is guaranteed by independent `useQuery` calls).

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.API/Controllers/
  └── ManufactureSettingsController.cs                 # NEW

backend/src/Anela.Heblo.Application/Features/Manufacture/
  ├── ManufactureConfigurationKeys.cs                  # NEW
  └── UseCases/GetManufactureSettings/                 # NEW
      ├── GetManufactureSettingsHandler.cs
      ├── GetManufactureSettingsRequest.cs
      └── GetManufactureSettingsResponse.cs

backend/src/Anela.Heblo.Application/Features/Configuration/
  ├── GetConfigurationHandler.cs                       # MODIFIED — drop ManufactureGroupId branch
  └── GetConfigurationResponse.cs                      # MODIFIED — drop property + doc comment

backend/test/Anela.Heblo.Tests/Features/Configuration/
  ├── GetConfigurationEndpointTests.cs                 # MODIFIED — delete GetConfiguration_ShouldExposeManufactureGroupIdField
  └── GetConfigurationHandlerTests.cs                  # MODIFIED — delete the 3 ManufactureGroupId tests (file now becomes minimal — consider deleting if no useful tests remain)

backend/test/Anela.Heblo.Tests/Features/Manufacture/Settings/  # NEW folder
  ├── GetManufactureSettingsHandlerTests.cs            # mirror handler tests removed from Configuration
  └── GetManufactureSettingsEndpointTests.cs           # anonymous integration test via HebloWebApplicationFactory

frontend/src/api/hooks/
  └── useManufactureSettings.ts                        # NEW (mirror of useConfiguration.ts shape)

frontend/src/components/modals/CreateManufactureOrderModal.tsx     # MODIFIED — swap hook
frontend/src/components/manufacture/detail/BasicInfoSection.tsx    # MODIFIED — swap hook
frontend/src/components/manufacture/list/ManufactureOrderFilters.tsx  # MODIFIED — swap hook
```

No change to `ManufactureModule.cs` (MediatR handlers are auto-discovered by assembly scanning, confirmed in `ManufactureModule.cs:23`'s comment).

### Interfaces and Contracts

```csharp
// Application/Features/Manufacture/ManufactureConfigurationKeys.cs
namespace Anela.Heblo.Application.Features.Manufacture;

public static class ManufactureConfigurationKeys
{
    public const string GroupId = "ManufactureGroupId";
}
```

```csharp
// Application/Features/Manufacture/UseCases/GetManufactureSettings/GetManufactureSettingsRequest.cs
public class GetManufactureSettingsRequest : IRequest<GetManufactureSettingsResponse> { }

// GetManufactureSettingsResponse.cs — MUST be class, not record (project rule for OpenAPI generation)
public class GetManufactureSettingsResponse : BaseResponse
{
    /// <summary>Microsoft Entra group ID used for manufacture responsible-person lookups.
    /// Null when the configuration key is missing or empty.</summary>
    public string? ManufactureGroupId { get; set; }
}

// GetManufactureSettingsHandler.cs
public class GetManufactureSettingsHandler
    : IRequestHandler<GetManufactureSettingsRequest, GetManufactureSettingsResponse>
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GetManufactureSettingsHandler> _logger;
    // ctor + Handle — read via ManufactureConfigurationKeys.GroupId, null-coalesce empty to null
}
```

```csharp
// API/Controllers/ManufactureSettingsController.cs
[ApiController]
[Route("api/manufacture/settings")]
public class ManufactureSettingsController : ControllerBase
{
    private readonly IMediator _mediator;
    public ManufactureSettingsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [AllowAnonymous]  // explicit, even though no [Authorize] is present — documents intent
    public Task<GetManufactureSettingsResponse> GetSettings(CancellationToken ct)
        => _mediator.Send(new GetManufactureSettingsRequest(), ct);
}
```

**Route note:** Use the explicit `[Route("api/manufacture/settings")]` rather than `[Route("api/[controller]")]` (which would resolve to `/api/manufacturesettings`). The spec mandates `/api/manufacture/settings`.

```typescript
// frontend/src/api/hooks/useManufactureSettings.ts — mirror useConfiguration.ts
import { useQuery, UseQueryResult } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';
import { GetManufactureSettingsResponse } from '../generated/api-client';

const MANUFACTURE_SETTINGS_QUERY_KEY = ['manufacture-settings'] as const;

export const useManufactureSettingsQuery = (): UseQueryResult<GetManufactureSettingsResponse> =>
  useQuery({
    queryKey: MANUFACTURE_SETTINGS_QUERY_KEY,
    queryFn: async () => (await getAuthenticatedApiClient()).manufactureSettings_GetSettings(),
    staleTime: Infinity,
    gcTime: Infinity,
    retry: 1,
  });
```

### Data Flow

**Cold start (SPA bootstrap):**
1. `App.tsx` finishes MSAL init (independent of these calls).
2. First Manufacture-related view mounts → its component calls both `useConfigurationQuery()` and `useManufactureSettingsQuery()`; React Query issues both HTTP requests in parallel.
3. Backend: `ConfigurationController.GetConfiguration` and `ManufactureSettingsController.GetSettings` resolve via MediatR. Each handler reads `IConfiguration` (already DI'd) and returns its DTO. No DB, no external dependency.
4. NSwag-generated TS client deserializes both. Three Manufacture components now read `manufactureSettings?.manufactureGroupId` instead of `appConfig?.manufactureGroupId`.
5. Both responses are cached forever (`staleTime: Infinity`) — no refetch storm.

**Anonymous reachability:** Anonymous-by-default for both endpoints because their controllers omit `[Authorize]`. The explicit `[AllowAnonymous]` on the new method is documentation; if a future maintainer adds `[Authorize]` to the new controller's class, the method attribute keeps the bootstrap working.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Stale `manufactureGroupId` reference in a frontend file missed by grep (e.g. inside generated TS until regeneration runs) | Medium | After backend change, run `dotnet build` so NSwag regenerates `api-client.ts`. Then re-grep frontend for `manufactureGroupId` — only `useManufactureSettings.ts` and the three consumer components should remain. |
| `Authorize` policy drift: someone later sets a global `FallbackPolicy` requiring auth, silently breaking the SPA bootstrap | Low | Keep `[AllowAnonymous]` on the new controller's action *explicitly* (not just by omitting `[Authorize]`); add an integration test that calls `GET /api/manufacture/settings` with no `Authorization` header and asserts 200. |
| Test file `GetConfigurationHandlerTests.cs` becomes empty after removing the three `ManufactureGroupId` tests — leaves an orphan test class | Low | Delete the file entirely if no other tests remain there. The handler still has full coverage via the endpoint test for version/environment/mock-auth. |
| Frontend bootstrap loading state changes (two queries instead of one) — could create a brief race where `manufactureGroupId` is `undefined` while UI is rendering | Low | The three consumers already null-coalesce to `""` (`appConfig?.manufactureGroupId ?? ""`); preserve identical behavior on the new hook so the loading window is no worse than today. |
| Route mismatch: `[Route("api/[controller]")]` would produce `/api/manufacturesettings`, breaking the contract in the spec | Medium | Use the explicit literal route `[Route("api/manufacture/settings")]`. Add an endpoint test asserting the path exactly. |
| `ManufactureGroupId` config key removed from `appsettings.json` someday — fails silently because handler returns null | Low | This is the same posture as today; out of scope per spec. Optionally log a single Information-level message at handler entry when the key is absent (mirror Configuration handler's `_logger.LogDebug` style). |
| Cross-module contract: `ManufactureConfigurationKeys` becomes a dumping ground for unrelated Manufacture keys | Low | Document the file's narrow purpose ("anonymous bootstrap config keys for Manufacture"); resist adding non-bootstrap keys. |

## Specification Amendments

1. **Add an explicit route literal requirement.** Specify the new controller must use `[Route("api/manufacture/settings")]` (literal), not `[Route("api/[controller]")]`, to guarantee the documented URL. The spec asserts the URL but does not pin the routing mechanism.

2. **Clarify "[AllowAnonymous] (or equivalent)".** Make it normative that `[AllowAnonymous]` is applied to the method (or class) of the new controller, *not* simply by omitting `[Authorize]`. The codebase's current convention is anonymous-by-omission, but explicit attribution protects against future global policy changes.

3. **`ManufactureModule.cs` is unchanged.** Spec implies a DI registration concern in §FR-2 ("follows the existing MediatR + MVC controller pattern"). State explicitly: no edit to `ManufactureModule.cs` is required — MediatR assembly scanning registers the handler automatically. Avoid spurious changes.

4. **Test-file deletion is allowed.** The current `GetConfigurationHandlerTests.cs` contains *only* the three `ManufactureGroupId` tests. After removing them the file is empty. Spec should permit deleting it outright instead of leaving an orphan class.

5. **Frontend "parallel call" wording.** Replace "frontend must issue `/api/configuration` and `/api/manufacture/settings` in parallel" with "frontend issues the two queries as independent React Query hooks; React Query dispatches them in parallel by default — no explicit `Promise.all` or `App.tsx` prefetch is required." This avoids a misimplementation that adds unneeded plumbing.

6. **Constant naming.** Spec offers `ManufactureConfigurationKeys.GroupId`. Lock that exact name — the existing `ManufactureConstants` is reserved for numeric domain constants; do not merge.

## Prerequisites

None. No migration, no infrastructure change, no new package, no Key Vault secret. The `ManufactureGroupId` key already exists in `appsettings.json:7` and `appsettings.Production.json:37` and is wired through Azure App Settings for production. Implementation can begin immediately.