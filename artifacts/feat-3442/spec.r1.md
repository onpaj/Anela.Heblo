# Specification: Replace untyped `(apiClient as any)` access in Dashboard hooks with typed generated client

## Summary
`frontend/src/api/hooks/useDashboard.ts` contains six hooks that all bypass the generated `ApiClient`'s public, typed surface by casting to `any` and reaching into `baseUrl` and `http.fetch` directly. The generated client already exposes fully typed methods for every one of these six endpoints (`dashboard_GetAvailableTiles`, `dashboard_GetUserSettings`, `dashboard_SaveUserSettings`, `dashboard_GetTileData`, `dashboard_EnableTile`, `dashboard_DisableTile`). This change migrates all six hooks to call those typed methods directly, removing every `as any` cast, the manual URL construction, and the redundant `await` on the synchronous `getAuthenticatedApiClient()` call in `useAvailableTiles`.

## Background
An arch-review finding (2026-06-30) identified that all six Dashboard hooks share this pattern:

```typescript
const apiClient = getAuthenticatedApiClient(false);
const relativeUrl = `/api/dashboard/settings`;
const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
const response = await (apiClient as any).http.fetch(fullUrl, { method: 'GET' });
```

This reaches into two undocumented internals of `ApiClient` (`baseUrl` and `http`) instead of calling the class's public, typed methods. If the generated client's internal shape changes (e.g. NSwag template update, constructor signature change), these six call sites break at runtime with no compile-time signal, because the `any` cast suppresses type checking entirely.

Inspection of the actual code in this worktree (as of this spec) confirms:
- **`frontend/src/api/hooks/useDashboard.ts`** (145 lines) has the described pattern at all six call sites: `useAvailableTiles` (lines 36–43), `useUserDashboardSettings` (lines 53–56), `useTileData` (lines 69–72), `useSaveDashboardSettings` (lines 87–91), `useEnableTile` (lines 111–115), `useDisableTile` (lines 131–136). Line numbers have drifted slightly from the brief but the pattern is identical at all six sites.
- **`frontend/src/api/client.ts`** exports `getApiBaseUrl()` (lines 178–181) returning exactly the value the hooks re-derive via `(apiClient as any).baseUrl`.
- **`frontend/src/api/generated/api-client.ts`** already contains fully typed methods for all six endpoints (lines 2616–2851 approx.):
  - `dashboard_GetAvailableTiles(): Promise<DashboardTileDto[]>`
  - `dashboard_GetUserSettings(): Promise<UserDashboardSettingsDto>`
  - `dashboard_SaveUserSettings(request: SaveUserSettingsRequest): Promise<FileResponse>`
  - `dashboard_GetTileData(tileParameters: { [key: string]: string } | null | undefined): Promise<DashboardTileDto[]>`
  - `dashboard_EnableTile(tileId: string): Promise<FileResponse>`
  - `dashboard_DisableTile(tileId: string): Promise<FileResponse>`

  This means the "typed methods aren't generated yet" fallback branch of the suggested fix does **not** apply here — a full migration to typed methods is possible for all six hooks, not just a `baseUrl` swap.
- The mutation endpoints (`dashboard_SaveUserSettings`, `dashboard_EnableTile`, `dashboard_DisableTile`) return `Promise<FileResponse>` rather than a JSON DTO. This is because the backend controller (`backend/src/Anela.Heblo.API/Controllers/DashboardController.cs`) declares these actions as bare `Task<ActionResult>` returning `Ok()` with no typed body — NSwag cannot infer a JSON response contract from an untyped `ActionResult`, so it falls back to treating the response as a binary/file payload (`Accept: application/octet-stream`, response consumed via `.blob()`). The current hand-rolled code ignores the response body entirely for these three calls (it only reads `response.status` in two of them), so switching to `FileResponse` does not lose any information the hooks currently use — but the mutation hooks' return type changes from implicit `void`/`Response` to `Promise<FileResponse>`.
- There is direct precedent for this exact migration in this repository: commit `2e178ff` ("Bank hooks — replace manual DTO interfaces and raw fetch with generated client", feature 3395) performed the identical refactor on `useBankStatements.ts` — replacing hand-rolled fetch/URL logic and manual DTO interfaces with generated typed client methods and generated DTOs, and updating the corresponding hook test file to mock the typed methods instead of raw `fetch`. This spec follows that established pattern.
- `frontend/src/api/hooks/__tests__/useDashboard.test.tsx` currently mocks `getAuthenticatedApiClient` to return a fake object shaped like `{ baseUrl, http: { fetch: mockFetch } }` and asserts on literal URL strings and raw `fetch` call args (e.g. `/api/dashboard/tiles`, lowercase). These tests must be rewritten to mock the six typed methods (`dashboard_GetAvailableTiles`, etc.) directly, matching the approach taken for `useBankStatements.test.ts` in commit `2e178ff`.
- Callers of these hooks — `frontend/src/components/dashboard/DashboardSettings.tsx` (uses `useAvailableTiles`, `useUserDashboardSettings`, `useEnableTile`, `useDisableTile`) and `frontend/src/components/pages/Dashboard.tsx` (uses `useUserDashboardSettings`, `useTileData`, `useSaveDashboardSettings`) — consume `data`, `isLoading`, `mutate(...)` from the hooks' return values. As long as the exported `DashboardTile`, `UserDashboardTile`, `UserDashboardSettings`, `SaveDashboardSettingsRequest` shapes used by these hooks stay structurally compatible (or the hooks map generated DTOs onto them), no caller changes should be required — this must be verified during implementation.

## Functional Requirements

### FR-1: Migrate `useAvailableTiles` to the typed client
Replace the manual `baseUrl`/`http.fetch` call with `apiClient.dashboard_GetAvailableTiles()`. Remove the unnecessary `await` on `getAuthenticatedApiClient()` (it is synchronous; only `getAuthenticatedApiClient(false)` variants in the other five hooks already omit the `await` correctly — this one is the outlier).

**Acceptance criteria:**
- No `as any` cast remains in `useAvailableTiles`.
- `getAuthenticatedApiClient()` is called without `await`.
- The hook calls `apiClient.dashboard_GetAvailableTiles()` and returns its resolved value (or maps it to `DashboardTile[]` if the local interface and `DashboardTileDto` diverge — see FR-6).
- Existing behavior (query key, no special-case status handling) is preserved.

### FR-2: Migrate `useUserDashboardSettings` to the typed client
Replace the manual fetch with `apiClient.dashboard_GetUserSettings()`.

**Acceptance criteria:**
- No `as any` cast remains in `useUserDashboardSettings`.
- The existing 403 special case (silently return `{ tiles: [], lastModified: new Date().toISOString() }` instead of throwing) must be preserved. Since the typed method throws a `SwaggerException`/`ApiException`-style error for any non-200/204 status (it does not return the response object for inspection), the 403 case must be handled by catching the thrown exception and inspecting its `status` property (the generated `throwException` helper attaches `status` to the thrown error — confirm exact shape in `generated/api-client.ts`'s `throwException` and `ApiException` class before implementing) and returning the same fallback value when `status === 403`. Any other error must continue to propagate/throw as before.
- Existing behavior for `!response.ok` (non-403 errors) — previously an explicit throw of `API Call Error (${status})` — is preserved in spirit: the typed client's own `throwException` will throw for these cases; do not suppress or reformat that error message unless required for a caller that depends on the exact string (verify no caller matches on this message before changing it).

### FR-3: Migrate `useTileData` to the typed client
Replace the manual fetch with `apiClient.dashboard_GetTileData(undefined)` (the hook currently sends no `tileParameters`, matching the method's `tileParameters: {...} | null | undefined` signature).

**Acceptance criteria:**
- No `as any` cast remains in `useTileData`.
- The existing 403 special case (return `[]` instead of throwing) is preserved using the same catch-and-inspect-status approach as FR-2.
- `refetchInterval: 30000` is unchanged.

### FR-4: Migrate `useSaveDashboardSettings` to the typed client
Replace the manual fetch with `apiClient.dashboard_SaveUserSettings(request)`, where `request` is built from the hook's `SaveDashboardSettingsRequest` input, mapped to the generated `SaveUserSettingsRequest` class (see FR-6 — DTOs are classes, not the plain object literal currently passed).

**Acceptance criteria:**
- No `as any` cast remains in `useSaveDashboardSettings`.
- Removed the unnecessary `await` on `getAuthenticatedApiClient()` in this hook (currently uses `await getAuthenticatedApiClient()` at line 87 — inconsistent with the other five hooks that call it as `getAuthenticatedApiClient(false)` synchronously; align to the synchronous, `showErrorToasts: false` call style used by `useUserDashboardSettings`/`useTileData`, unless there's a reason this mutation should show error toasts — flag this as a behavior decision, see Open Questions).
- `onSuccess` cache invalidation of `dashboard`/`settings` and `dashboard`/`data` query keys is unchanged.
- The mutation's resolved value becomes a `FileResponse` object; since callers do not currently consume the mutation's resolved data (only `onSuccess` side effects), this type change should not require caller changes, but must be verified.

### FR-5: Migrate `useEnableTile` and `useDisableTile` to the typed client
Replace the manual fetch with `apiClient.dashboard_EnableTile(tileId)` / `apiClient.dashboard_DisableTile(tileId)`.

**Acceptance criteria:**
- No `as any` cast remains in either hook.
- Remove the unnecessary `await` on `getAuthenticatedApiClient()` in both hooks (currently `await getAuthenticatedApiClient()`), aligned to the synchronous call style, unless the toast-suppression flag matters (see Open Questions — same as FR-4).
- `onSuccess` cache invalidation behavior is unchanged.

### FR-6: Reconcile local hand-written interfaces with generated DTOs
The hooks currently export local interfaces (`DashboardTile`, `UserDashboardTile`, `UserDashboardSettings`, `SaveDashboardSettingsRequest`) that duplicate the generated `DashboardTileDto`, `UserDashboardTileDto`, `UserDashboardSettingsDto`, `SaveUserSettingsRequest` types. Following the precedent in commit `2e178ff` (`useBankStatements.ts`), re-export the generated types where the shapes are equivalent, and only keep a local interface where the generated type is missing a field the UI needs or where field optionality differs in a way that requires a wrapper/mapper.

Known shape differences to resolve during implementation:
- `DashboardTileDto.size` is typed as `string` in the generated client, but the local `DashboardTile.size` is a string literal union `'Small' | 'Medium' | 'Large'`. Decide whether to keep the narrower local type (requires a cast or validation at the hook boundary) or widen consumers to `string` — recommend keeping the literal union via a thin mapping function to preserve type safety for consuming components, since `DashboardGrid.tsx`/`DashboardTile.tsx` likely switch on this field.
- `DashboardTileDto` fields are all optional (`tileId?: string`, etc.) since NSwag marks C# nullable/all properties as optional; local interfaces mark most fields as required. Decide whether hooks should validate/assert non-null at the boundary or whether consuming components should be updated to handle `undefined`. Recommend a small mapping function per hook that asserts required fields are present (throwing or filtering out malformed entries) rather than pushing `| undefined` through the whole component tree.
- `SaveDashboardSettingsRequest` (local) has a plain `tiles: UserDashboardTile[]` object shape; `SaveUserSettingsRequest` (generated) is a **class** requiring `new SaveUserSettingsRequest({ tiles: [...] })` or equivalent construction (per this repo's rule that generated DTOs are classes, not plain objects — see `docs/architecture/development_guidelines.md`). The mutation's `mutationFn` must construct the class instance before calling `dashboard_SaveUserSettings`.

**Acceptance criteria:**
- `frontend/src/api/hooks/useDashboard.ts` compiles with no `any` and no unresolved type errors (`dotnet build`/`npm run build` not applicable here — use `npm run build` and `tsc` checks specifically).
- Any mapping functions introduced are colocated in `useDashboard.ts` (or a small local helper) and covered by the updated tests.
- `DashboardSettings.tsx` and `Dashboard.tsx` require no changes, OR any required changes are minimal and limited to the type-level adjustments described above (e.g., handling `| undefined` on a field) — confirm via `npm run build` with no `any`-suppressed errors.

### FR-7: Update `useDashboard.test.tsx` to mock typed client methods
`frontend/src/api/hooks/__tests__/useDashboard.test.tsx` currently mocks `getAuthenticatedApiClient` to return `{ baseUrl, http: { fetch: mockFetch } }` and asserts on raw URL strings passed to `fetch`. This must be rewritten to mock `dashboard_GetAvailableTiles`, `dashboard_GetUserSettings`, `dashboard_GetTileData`, `dashboard_SaveUserSettings`, `dashboard_EnableTile`, `dashboard_DisableTile` directly as jest mock functions on the mocked `ApiClient` instance, following the same restructuring `useBankStatements.test.ts` underwent in commit `2e178ff`.

**Acceptance criteria:**
- All existing test cases (success path, error path, 403-fallback path for settings/data, special-character tile IDs, URL-construction assertions) are preserved in intent but rewritten to assert against typed method calls and arguments instead of raw fetch URLs.
- No test references `mockFetch`, `baseUrl`, or `http.fetch` after the change.
- `npm test` passes for this file with no regressions.

## Non-Functional Requirements

### NFR-1: Performance
No behavior or performance change expected — this is a call-site refactor with identical HTTP semantics (same endpoints, same methods, same headers implicitly added by `getAuthenticatedApiClient`'s `authenticatedHttp.fetch` wrapper, since the generated methods still route through `this.http.fetch` which is the same authenticated wrapper).

### NFR-2: Security
No change. Authentication headers, 401 handling, and toast suppression logic all live in `getAuthenticatedApiClient()`'s injected `http.fetch` implementation (`frontend/src/api/client.ts`), which is unaffected by this refactor — the typed methods call `this.http.fetch` internally, the same object.

## Data Model
No backend or persisted data model changes. Frontend-only type reconciliation between:
- Generated: `DashboardTileDto`, `UserDashboardSettingsDto`, `UserDashboardTileDto`, `SaveUserSettingsRequest`, `FileResponse` (all in `frontend/src/api/generated/api-client.ts`).
- Local (to be reconciled per FR-6): `DashboardTile`, `UserDashboardTile`, `UserDashboardSettings`, `SaveDashboardSettingsRequest` (in `frontend/src/api/hooks/useDashboard.ts`).

## API / Interface Design
No backend endpoint changes. Frontend hook signatures (`useAvailableTiles()`, `useUserDashboardSettings()`, `useTileData()`, `useSaveDashboardSettings()`, `useEnableTile()`, `useDisableTile()`) keep their existing external call signatures (same `useQuery`/`useMutation` shape, same `mutate(tileId: string)` / `mutate(settings: SaveDashboardSettingsRequest)` inputs) so that `DashboardSettings.tsx` and `Dashboard.tsx` do not need to change their call sites — only the internal implementation and possibly the resolved `data` shape's TypeScript type change.

## Dependencies
- Generated OpenAPI client `frontend/src/api/generated/api-client.ts` (already contains the required typed methods — no backend/OpenAPI regeneration needed).
- `getAuthenticatedApiClient()` / `getApiBaseUrl()` in `frontend/src/api/client.ts` (unchanged, just consumed correctly).
- Precedent implementation: commit `2e178ff` (`useBankStatements.ts` refactor) as the reference pattern for this migration and its test rewrite.

## Out of Scope
- Any backend controller changes (e.g., giving `SaveUserSettings`/`EnableTile`/`DisableTile` a typed JSON response body instead of bare `ActionResult`, which would let NSwag generate a JSON-returning method instead of `FileResponse`). This is a larger, separate backend contract change and is not required to fix the arch-review finding.
- Regenerating or modifying the OpenAPI spec / generated client itself.
- Any UI/UX changes to Dashboard components.
- Broader `any`-cast cleanup elsewhere in the codebase (this spec is scoped to the six hooks in `useDashboard.ts` named in the finding).

## Open Questions
1. For `useSaveDashboardSettings`, `useEnableTile`, and `useDisableTile`, the current code uses `await getAuthenticatedApiClient()` (default `showErrorToasts = true`), while `useUserDashboardSettings`/`useTileData` use `getAuthenticatedApiClient(false)` (toasts suppressed). Should the mutation hooks continue showing global error toasts on failure (current behavior, `showErrorToasts` defaults to `true`), or should this be made consistent with the query hooks? Recommend preserving current per-hook toast behavior exactly (i.e., keep `showErrorToasts` default/true for the three mutation hooks) and only remove the redundant `await` — since changing toast behavior is outside the stated scope of "bypass the generated ApiClient" and would be a silent behavior change.
2. The generated `ApiException`/`throwException` error shape (used to detect 403 for the settings/data fallback in FR-2/FR-3) needs to be confirmed exactly (property name for HTTP status on the thrown error) before implementation — this is a quick lookup in `generated/api-client.ts`'s `throwException` function/`ApiException` class, not a product decision, but flagging it so the implementer checks it rather than guessing.

## Status: HAS_QUESTIONS
