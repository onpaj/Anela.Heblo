# Design: Replace untyped `(apiClient as any)` access in Dashboard hooks with typed generated client

## Component Design

All changes are confined to `frontend/src/api/hooks/useDashboard.ts` (and its test file, `frontend/src/api/hooks/__tests__/useDashboard.test.tsx`). No new files or modules are introduced. `frontend/src/components/dashboard/DashboardSettings.tsx` and `frontend/src/components/pages/Dashboard.tsx` remain unmodified; they continue to import the same six hooks and the same four local type names from `useDashboard.ts`.

### Hook responsibilities

Each hook keeps its existing external signature (`useQuery`/`useMutation` shape, same query keys, same `mutate`/`mutateAsync` input types). Internally, each replaces the manual `(apiClient as any).baseUrl` + `(apiClient as any).http.fetch(...)` pattern with a direct call to the corresponding typed method on the client returned by `getAuthenticatedApiClient()`.

- **`useAvailableTiles`** — Query hook. Calls `apiClient.dashboard_GetAvailableTiles()`, maps the resolved `DashboardTileDto[]` through `toDashboardTile` into `DashboardTile[]`. Drops the previously-redundant `await` on `getAuthenticatedApiClient()` (it is synchronous). No error special-casing; errors propagate as thrown `SwaggerException`.

- **`useUserDashboardSettings`** — Query hook. Calls `apiClient.dashboard_GetUserSettings()`, maps the resolved `UserDashboardSettingsDto` through `toUserDashboardSettings` into `UserDashboardSettings`. Wraps the call in try/catch: if the caught error is forbidden (`isForbidden(error)`), returns the fallback `{ tiles: [], lastModified: new Date().toISOString() }` instead of throwing; any other error rethrows unchanged.

- **`useTileData`** — Query hook, `refetchInterval: 30000` (unchanged). Calls `apiClient.dashboard_GetTileData(undefined)` (no tile parameters, matching current behavior), maps resolved `DashboardTileDto[]` through `toDashboardTile`. Same try/catch-and-check-`isForbidden` pattern as `useUserDashboardSettings`, but the fallback value on 403 is `[]`.

- **`useSaveDashboardSettings`** — Mutation hook. `mutationFn` builds a `SaveUserSettingsRequest` class instance from the hook's `SaveDashboardSettingsRequest` input (`tiles` mapped to `UserDashboardTileDto` instances), then calls `apiClient.dashboard_SaveUserSettings(request)`. Drops the redundant `await` on `getAuthenticatedApiClient()`. `showErrorToasts` stays at its default (`true`); `onSuccess` still invalidates the `dashboard`/`settings` and `dashboard`/`data` query keys. Resolved mutation type becomes `Promise<FileResponse>`, unconsumed by callers.

- **`useEnableTile`** — Mutation hook. Calls `apiClient.dashboard_EnableTile(tileId)`. Drops the redundant `await` on `getAuthenticatedApiClient()`. `showErrorToasts` unchanged (`true`); `onSuccess` cache invalidation unchanged. Resolved type becomes `Promise<FileResponse>`, unconsumed.

- **`useDisableTile`** — Mutation hook. Calls `apiClient.dashboard_DisableTile(tileId)`. Same treatment as `useEnableTile`.

### Mapping functions (colocated in `useDashboard.ts`)

Two mapping functions reconcile generated DTOs (optional fields, per NSwag's nullable-property convention) with the local interfaces (required fields, as consumed by `DashboardSettings.tsx` / `Dashboard.tsx`):

- **`toDashboardTile(dto: DashboardTileDto): DashboardTile`** — defaults every optional DTO field to a safe non-null value (`?? ''`, `?? false`, `?? []`, `?? 'Medium'` for `size`) so the local `DashboardTile` type's required fields are always satisfied. Used by both `useAvailableTiles` and `useTileData`.

- **`toUserDashboardSettings(dto: UserDashboardSettingsDto): UserDashboardSettings`** — maps `dto.tiles` (optional array of `UserDashboardTileDto`, each field optional) to the local `UserDashboardTile[]` shape, defaulting missing fields; defaults `lastModified` to `new Date().toISOString()` when absent. Used by `useUserDashboardSettings`.

### 403 detection helper

**`isForbidden(error: unknown): boolean`** — structural check (`typeof error === 'object' && error !== null && 'status' in error && (error as { status?: number }).status === 403`), not an `instanceof SwaggerException` check. This is deliberate: `SwaggerException` is the class the generated `throwException` helper throws for every non-2xx response on these endpoints (no typed `result` body exists for a 403 on `dashboard_GetUserSettings`/`dashboard_GetTileData`), and it exposes a plain numeric `status` property set by its constructor — but a structural check is more robust across the test-mock boundary, where the thrown object may be a plain literal rather than a real `SwaggerException` instance. Used by `useUserDashboardSettings` and `useTileData` only; not introduced as a shared cross-file helper (no other hook currently needs it).

## Data Schemas

### Generated types consumed (from `frontend/src/api/generated/api-client.ts`)

- `DashboardTileDto` — all fields optional (`tileId?: string`, `title?: string`, `description?: string`, `size?: string`, `category?: string`, `defaultEnabled?: boolean`, `autoShow?: boolean`, `requiredPermissions?: string[]`, `isUnauthorized?: boolean`, `data?: any`).
- `UserDashboardSettingsDto` — `{ tiles?: UserDashboardTileDto[]; lastModified?: Date }`.
- `UserDashboardTileDto` — `{ tileId?: string; isVisible?: boolean; displayOrder?: number }`.
- `SaveUserSettingsRequest` — generated class (not a plain object); constructed as `new SaveUserSettingsRequest({ tiles: [...] })` per the "generated DTOs are classes" rule.
- `FileResponse` — resolved (but unconsumed) type for the three mutation methods, a consequence of the backend controller actions being bare `Task<ActionResult>` with no typed JSON response body (out of scope to change).
- `SwaggerException` — thrown by `throwException` for any non-2xx status on these six endpoints; plain numeric `status` property. Imported type-only (used only in error-shape checks, never constructed).

### Local types (exported from `useDashboard.ts`, unchanged call-site shape, one narrowing removed)

```typescript
export interface DashboardTile {
  tileId: string;
  title: string;
  description: string;
  size: string; // widened from 'Small' | 'Medium' | 'Large' — DashboardTileDto.size is string
  category: string;
  defaultEnabled: boolean;
  autoShow: boolean;
  requiredPermissions: string[];
  isUnauthorized?: boolean;
  data?: any;
}

export interface UserDashboardTile {
  tileId: string;
  isVisible: boolean;
  displayOrder: number;
}

export interface UserDashboardSettings {
  tiles: UserDashboardTile[];
  lastModified: string;
}

export interface SaveDashboardSettingsRequest {
  tiles: UserDashboardTile[];
}
```

`DashboardTile.size` is widened from the literal union `'Small' | 'Medium' | 'Large'` to plain `string`, matching `DashboardTileDto.size`'s generated type and `SizeBadge`'s existing `string` prop — no cast needed at the mapping boundary.

### Request/response shapes per hook (post-refactor)

| Hook | Request (typed method call) | Success response (post-mapping) | Error handling |
|---|---|---|---|
| `useAvailableTiles` | `dashboard_GetAvailableTiles()` | `DashboardTile[]` (via `toDashboardTile`) | Propagates `SwaggerException` unchanged |
| `useUserDashboardSettings` | `dashboard_GetUserSettings()` | `UserDashboardSettings` (via `toUserDashboardSettings`) | Catch `SwaggerException`; if `isForbidden`, return `{ tiles: [], lastModified: new Date().toISOString() }`; else rethrow |
| `useTileData` | `dashboard_GetTileData(undefined)` | `DashboardTile[]` (via `toDashboardTile`) | Catch `SwaggerException`; if `isForbidden`, return `[]`; else rethrow |
| `useSaveDashboardSettings` | `dashboard_SaveUserSettings(new SaveUserSettingsRequest({ tiles: settings.tiles.map(t => new UserDashboardTileDto(t)) }))` | `FileResponse` (unconsumed) | Propagates `SwaggerException`; global error toast shown (`showErrorToasts` default `true`, unchanged) |
| `useEnableTile` | `dashboard_EnableTile(tileId: string)` | `FileResponse` (unconsumed) | Propagates `SwaggerException`; global error toast shown (unchanged) |
| `useDisableTile` | `dashboard_DisableTile(tileId: string)` | `FileResponse` (unconsumed) | Propagates `SwaggerException`; global error toast shown (unchanged) |

No database schema, backend contract, or event payload changes are involved — this is a frontend-only type and call-site reconciliation, with identical HTTP semantics to the current implementation (same endpoints, same authenticated `http.fetch` wrapper in `frontend/src/api/client.ts`).
