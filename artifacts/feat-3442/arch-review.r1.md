# Architecture Review: Replace untyped `(apiClient as any)` access in Dashboard hooks with typed generated client

## Skip Design: true

## Architectural Fit Assessment

This is a textbook call-site refactor with an exact, already-merged precedent in this repo (commit `2e178ff`, `useBankStatements.ts`). It aligns cleanly with `docs/development/api-client-generation.md`'s "CRITICAL: URL Construction Rules" section, which explicitly names `(apiClient as any).baseUrl` / `(apiClient as any).http.fetch` as the forbidden pattern and prescribes `getAuthenticatedApiClient()` + typed methods as the fix. No architectural boundary is crossed: this stays entirely inside `frontend/src/api/hooks/useDashboard.ts` and its test file, touches no backend code, no contracts, no module boundaries. `DashboardSettings.tsx` and `Dashboard.tsx` are the only consumers and, per the spec and my own read of both files, need zero behavioral changes — only type-shape awareness at the hook boundary.

Confirmed by direct inspection:
- All six generated methods (`dashboard_GetAvailableTiles`, `dashboard_GetUserSettings`, `dashboard_SaveUserSettings`, `dashboard_GetTileData`, `dashboard_EnableTile`, `dashboard_DisableTile`) already exist in `frontend/src/api/generated/api-client.ts` (lines 2616–2856) — no regeneration needed.
- `SwaggerException` (line 41216) is a plain class with a numeric `status` field; `throwException` (line 41240) throws it directly whenever no typed `result` is supplied — which is always true for these six dashboard endpoints, since none of them are annotated with a typed non-200 `ProducesResponseType`. `ApiException` does not exist anywhere in the codebase — confirmed.
- The three mutation methods (`dashboard_SaveUserSettings`, `dashboard_EnableTile`, `dashboard_DisableTile`) return `Promise<FileResponse>` because their controller actions are bare `Task<ActionResult>` with no typed response DTO, so NSwag falls back to treating them as binary payloads (`Accept: application/octet-stream`, `response.blob()`). This is a pre-existing backend contract gap, correctly called out as out-of-scope in the spec.
- `useBankStatements.ts` is the right template: `getAuthenticatedApiClient()` called synchronously (no `await`), generated DTOs imported and re-exported directly where shapes match, `new BankImportRequestDto({...})` used to construct a class instance for a POST body, `.mockReturnValue` used in tests to mock the whole `ApiClient` with jest.fn() methods rather than mocking raw `fetch`. `frontend/src/api/testUtils.ts` already provides `mockAuthenticatedApiClient()` and `createQueryClientWrapper()` — the current `useDashboard.test.tsx` reinvents this by hand and should switch to the shared helpers, matching `useBankStatements.test.ts`.
- Components consume required (non-optional) fields on `DashboardTile` (`tileId`, `title`, `description`, `category`, `defaultEnabled`, `autoShow`, `size`) directly with no `?.`/fallback handling in `DashboardSettings.tsx` and `Dashboard.tsx` (e.g. `tile.title`, `<SizeBadge size={tile.size} />`, `.filter(tile => ... tile.defaultEnabled)`). The generated `DashboardTileDto` marks every field optional (NSwag's nullable-property convention). This divergence must be resolved at the hook boundary, not pushed into the components. `SizeBadge`'s prop is already typed as plain `string`, so widening `DashboardTile.size` from the literal union to `string` requires no `SizeBadge` change — good, that removes one degree of freedom from the mapping decision.

## Proposed Architecture

### Component Overview

```
DashboardSettings.tsx ─┐
                        ├─> useDashboard.ts (hooks) ──> ApiClient (generated, typed)
Dashboard.tsx ─────────┘         │                              │
                                 │ maps DashboardTileDto/        │ this.http.fetch
                                 │ UserDashboardSettingsDto      │ (same authenticatedHttp
                                 │ -> local DashboardTile/       │  wrapper as today —
                                 │    UserDashboardSettings      │  auth headers, 401
                                 │ (mapping fn, colocated)       │  redirect, toasts
                                 ▼                              ▼
                         local exported types            DashboardController.cs
                         (still enforce non-null          (unchanged)
                          required fields for UI)
```

No new components, no new files besides the existing hook/test pair. `client.ts` (`getAuthenticatedApiClient`, `getApiBaseUrl`) is unchanged — it is already correct and is the thing the hooks should have been calling all along.

### Key Design Decisions

#### Decision 1: How to reconcile optional generated DTOs with required local UI types
**Options considered:**
1. Re-export generated DTOs directly and push `| undefined` handling into `DashboardSettings.tsx`/`Dashboard.tsx`.
2. Keep local interfaces as-is (required fields) and add a small mapping function per query hook that converts `DashboardTileDto`/`UserDashboardSettingsDto` → the local shape, asserting/defaulting required fields.
3. Loosen local interfaces to match generated optionality but leave components unchanged (type errors surface at component field access instead).

**Chosen approach:** Option 2 — keep `DashboardTile`, `UserDashboardTile`, `UserDashboardSettings` as local interfaces with the same required-field shape they have today, and add one small mapping function per read hook (`useAvailableTiles`, `useTileData`, `useUserDashboardSettings`) that converts the generated DTO into the local type. Widen `DashboardTile.size` to `string` (dropping the `'Small' | 'Medium' | 'Large'` literal union) since `SizeBadge` already accepts plain `string` and the generated `DashboardTileDto.size` is `string`.

**Rationale:** This is exactly the pattern the spec recommends in FR-6 and it is zero-risk for the two consuming components — no prop or callsite changes required, verified by reading both files. Pushing `| undefined` into `DashboardSettings.tsx`/`Dashboard.tsx` would touch two files with no test coverage benefit and violates "surgical changes" — this task is scoped to the hook file and its test, not the consumers. Malformed/missing-field tile entries (a field NSwag marked optional but is actually always present from a healthy backend) should be filtered out rather than thrown, matching the existing tolerant style in these hooks (`Array.isArray(...) ? ... : []` guards already present in the consumers).

#### Decision 2: Where the 403-fallback and error-message-preservation logic lives
**Options considered:**
1. Wrap each read hook's `queryFn` in try/catch, checking `error instanceof SwaggerException || typeof error.status === 'number'` and branching on `status === 403`.
2. Introduce a shared helper (e.g. `isForbidden(error)`) in `client.ts` used by any future hook needing this pattern.

**Chosen approach:** Option 1, inline per hook — do not introduce a new shared abstraction for this refactor.

**Rationale:** Only two of six hooks (`useUserDashboardSettings`, `useTileData`) need the 403 fallback, and the spec explicitly scopes this change to `useDashboard.ts` only ("Out of Scope: Broader any-cast cleanup elsewhere"). Per "surgical changes" in CLAUDE.md, do not build cross-hook infrastructure that no other caller currently needs — `docs/architecture/development_guidelines.md`'s ADR pattern ("Do not add a shared helper unless a real consumer exists") applies by analogy. If a third hook needs this pattern later, extract then.

#### Decision 3: Mutation return type change (`void`-ish → `Promise<FileResponse>`)
**Options considered:**
1. Let `useMutation`'s `mutationFn` return type become `FileResponse` and leave it unconsumed (matches spec FR-4/FR-5).
2. Explicitly discard the return value by having `mutationFn` return `void` (wrap the call, ignore the resolved `FileResponse`).

**Chosen approach:** Option 1 — let the resolved type be `Promise<FileResponse>`, consistent with what the generated method actually returns; do not add a wrapper purely to force `void`.

**Rationale:** Both `DashboardSettings.tsx` (`enableTile.mutateAsync(tile.tileId)`, awaited but result unused) and `Dashboard.tsx` (`saveDashboardSettings.mutateAsync({ tiles: updatedTiles })`, awaited but result unused) discard the resolved value already — confirmed by reading both call sites. No caller change needed. Forcing `void` would just be extra code with no behavioral or type-safety benefit for zero consumers.

## Implementation Guidance

### Directory / Module Structure
No new files/directories. Modify exactly two files:
- `frontend/src/api/hooks/useDashboard.ts`
- `frontend/src/api/hooks/__tests__/useDashboard.test.tsx`

Do not touch `DashboardSettings.tsx`, `Dashboard.tsx`, `SizeBadge.tsx`, `CategoryBadge.tsx`, or `DashboardGrid.tsx` unless `npm run build` surfaces a genuine type error after the hook change (spec FR-6 requires this to be verified, not assumed).

### Interfaces and Contracts

Keep the four local interfaces exported from `useDashboard.ts`, with one change (`size: string` instead of the literal union):

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
// UserDashboardTile, UserDashboardSettings, SaveDashboardSettingsRequest: unchanged shape
```

Import from generated client: `DashboardTileDto`, `UserDashboardSettingsDto`, `UserDashboardTileDto`, `SaveUserSettingsRequest`, `SwaggerException` (type-only import for `SwaggerException` is fine since it's only used in `catch`/`instanceof`-style checks, not constructed).

Mapping helpers (colocated in `useDashboard.ts`, not a new file — mirrors FR-6's "colocated in useDashboard.ts" acceptance criterion):

```typescript
const toDashboardTile = (dto: DashboardTileDto): DashboardTile => ({
  tileId: dto.tileId ?? '',
  title: dto.title ?? '',
  description: dto.description ?? '',
  size: dto.size ?? 'Medium',
  category: dto.category ?? '',
  defaultEnabled: dto.defaultEnabled ?? false,
  autoShow: dto.autoShow ?? false,
  requiredPermissions: dto.requiredPermissions ?? [],
  isUnauthorized: dto.isUnauthorized,
  data: dto.data,
});

const toUserDashboardSettings = (dto: UserDashboardSettingsDto): UserDashboardSettings => ({
  tiles: (dto.tiles ?? []).map(t => ({
    tileId: t.tileId ?? '',
    isVisible: t.isVisible ?? false,
    displayOrder: t.displayOrder ?? 0,
  })),
  lastModified: dto.lastModified?.toISOString() ?? new Date().toISOString(),
});

const isForbidden = (error: unknown): boolean =>
  typeof error === 'object' && error !== null && 'status' in error &&
  (error as { status?: number }).status === 403;
```

`isForbidden` uses a structural check rather than `instanceof SwaggerException` — safer across bundling/test-mock boundaries where the thrown object may be a plain mock, not a real class instance (this mirrors how the spec describes the 403 check: "a plain `number`" on the shape, not a class identity check).

Each of the six hooks becomes a thin call: `getAuthenticatedApiClient()` (no `await` — it is synchronous), the typed method call, and (for the three read hooks) mapping + 403 handling. `useSaveDashboardSettings`'s `mutationFn` constructs `new SaveUserSettingsRequest({ tiles: settings.tiles.map(t => new UserDashboardTileDto(t)) })` before calling `dashboard_SaveUserSettings` — required per the "DTOs are classes" rule.

### Data Flow

**Read path (`useAvailableTiles`, `useTileData`):**
`apiClient.dashboard_GetAvailableTiles()` → resolves `DashboardTileDto[]` → `.map(toDashboardTile)` → `DashboardTile[]` returned from `queryFn` → consumed unchanged by `DashboardSettings.tsx` / `Dashboard.tsx`.

**Read path with 403 fallback (`useUserDashboardSettings`, `useTileData`):**
`try { return mapped result } catch (error) { if (isForbidden(error)) return fallback; throw error; }` — fallback values (`{ tiles: [], lastModified: new Date().toISOString() }` and `[]` respectively) unchanged from today.

**Write path (`useSaveDashboardSettings`, `useEnableTile`, `useDisableTile`):**
Hook builds typed request (class instance where required) → calls typed mutation method → resolves `FileResponse` (unused) → `onSuccess` invalidates the same two query keys as today, unchanged.

Auth headers, 401 redirect, and toast suppression are untouched — the typed methods route through `this.http.fetch`, which is the same `authenticatedHttp` object built in `getAuthenticatedApiClient()` (`frontend/src/api/client.ts` lines 276–377). Confirmed by reading the generated method bodies (`return this.http.fetch(url_, options_)...`) — no new HTTP semantics are introduced.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `DashboardTileDto.size` widened to `string` breaks a component relying on the literal union for exhaustiveness (e.g. a `switch`) | Low | Verified: only consumer is `SizeBadge`, whose prop is already `string`. `npm run build` will catch any other case exhaustively; none found via search. |
| 403-detection via structural check (`'status' in error`) is looser than `instanceof SwaggerException` and could swallow unrelated errors that happen to carry a `status` field | Low | Scope the check narrowly (`status === 403` exactly, not `>= 400`); this matches the spec's own described approach and today's behavior (`response.status === 403`) is equally coarse. |
| Test rewrite (FR-7) under-covers a previously-covered scenario (e.g. the "special character tile ID" test) during the mock restructuring | Medium | Port each existing `it(...)` block 1:1, swapping `mockFetch` assertions for `mockClient.dashboard_XxxTile` call/argument assertions; use `frontend/src/api/testUtils.ts`'s `mockAuthenticatedApiClient` + `createQueryClientWrapper`, matching `useBankStatements.test.ts` exactly. |
| Mutation resolved type change (`FileResponse`) silently breaks a caller that inspects `.status`/`.ok` on the resolved mutation value | Low | Confirmed via direct read of both call sites: only `mutateAsync(...)` awaited with no further access; no caller inspects the resolved value. |
| `SaveUserSettingsRequest`/`UserDashboardTileDto` class construction diverges from the plain-object shape currently sent, changing JSON body shape | Low | The generated class's `toJSON()` serializes to the same field names (`tileId`, `isVisible`, `displayOrder`, `tiles`) as the current plain object — verified by reading `SaveUserSettingsRequest.toJSON()`/`UserDashboardTileDto.toJSON()` in `api-client.ts`. No backend-visible change. |

## Specification Amendments

None required — the spec (r2, status COMPLETE) is implementation-accurate based on my independent read of the same files. One clarification worth stating explicitly for the implementer (not a spec defect): `DashboardTile.size`'s literal union (`'Small' | 'Medium' | 'Large'`) should be widened to plain `string` rather than preserved via a cast, since `SizeBadge.tsx`'s prop type is already `string` — the spec left this as an open "decide during implementation" item (FR-6 second bullet); this review resolves it in favor of widening, the simpler of the two options, with no consumer-side cost.

## Prerequisites

None. No migrations, no config, no infrastructure changes, no OpenAPI regeneration — the generated client already contains everything needed. Implementation can start immediately against `frontend/src/api/hooks/useDashboard.ts` and its test file.
