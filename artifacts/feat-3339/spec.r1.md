# Specification: Admin Permission Guard for ResponsiblePersonCombobox

## Summary

`ResponsiblePersonCombobox` unconditionally fires `GET /api/UserManagement/group-members` regardless of the caller's permissions, producing 113 avoidable 403 responses per week from non-admin Manufacture users. The fix gates the TanStack Query `enabled` flag behind the `admin.administration.read` permission so the request is never sent for users who lack it, while preserving the existing manual-entry fallback as the working path for non-admins.

## Background

The backend endpoint `GET /api/UserManagement/group-members` is correctly protected by `[FeatureAuthorize(Feature.Admin_Administration)]` (`backend/src/Anela.Heblo.API/Controllers/UserManagementController.cs:10`). That policy is intentional and must not change.

`ResponsiblePersonCombobox` is embedded in three Manufacture screens used by non-admin users:

- `frontend/src/components/modals/CreateManufactureOrderModal.tsx`
- `frontend/src/components/manufacture/detail/BasicInfoSection.tsx`
- `frontend/src/components/manufacture/list/ManufactureOrderFilters.tsx`

Each time a non-admin user opens one of these views, the combobox fires the query (plus 2 retries at `retryDelay: 1000 ms`), each call returning 403. Telemetry counts 113 such errors in P7D (2026-06-17 – 2026-06-24), consistent with prior tracking in #3184 (54 × 403). The combobox already supports `allowManualEntry`, which all three call sites pass as `true`, so non-admins already have a fully functional fallback input path.

The backend policy is correct. This issue implements a frontend guard that silences the noise without changing any server-side access control.

## Functional Requirements

### FR-1: Permission-gated query in `useResponsiblePersonsQuery`

The `useResponsiblePersonsQuery` hook must accept an optional `enabled` parameter (or an `canFetch` boolean) that is ANDed with the existing `Boolean(groupId)` guard. When `enabled` is `false`, TanStack Query must not issue the network request (it remains in idle/disabled state, returning `{ data: undefined, isLoading: false, isError: false }`).

**Signature change:**
```ts
// before
export const useResponsiblePersonsQuery = (groupId: string)

// after
export const useResponsiblePersonsQuery = (groupId: string, options?: { enabled?: boolean })
```

Internal `enabled` becomes: `Boolean(groupId) && (options?.enabled ?? true)`.

**Acceptance criteria:**
- When `options.enabled` is `false`, no HTTP request is made for `GET /api/UserManagement/group-members`.
- When `options.enabled` is `true` (or omitted) and `groupId` is non-empty, the request fires as before.
- The hook's existing `staleTime`, `retry`, and `retryDelay` values are unchanged.

### FR-2: Permission check in `ResponsiblePersonCombobox`

`ResponsiblePersonCombobox` must read `usePermissionsContext().hasPermission('admin.administration.read')` and pass the result as the `enabled` option to `useResponsiblePersonsQuery`. No prop is added to `ResponsiblePersonCombobox`; the check is entirely internal.

**Acceptance criteria:**
- For a user without `admin.administration.read`: `useResponsiblePersonsQuery` is called with `enabled: false`; no network request is made.
- For a user with `admin.administration.read`: `useResponsiblePersonsQuery` is called with `enabled: true`; the member list loads and is presented in the dropdown.
- The three existing call sites (`CreateManufactureOrderModal`, `BasicInfoSection`, `ManufactureOrderFilters`) require no changes.

### FR-3: Non-admin fallback UX

When the query is disabled (non-admin user), the combobox must behave identically to the existing `isError` path — no error indicator is shown, the field is fully operable via manual entry. The `allowManualEntry={true}` prop (already set at all three call sites) drives this behaviour.

Concretely: when `canReadAdministration` is `false`, `data` from the hook is `undefined`, `isLoading` is `false`, and `isError` is `false`. The combobox renders as a free-text creatable select, identical to the today's state after a network error except that no "Could not load team members" amber banner is shown.

**Acceptance criteria:**
- A non-admin user sees no error banner in `ResponsiblePersonCombobox`.
- A non-admin user can type a name and submit it as a custom (manual) entry.
- A non-admin user can clear the field.
- The field is not disabled (unless `disabled={true}` is passed by the parent, which is unrelated).

### FR-4: Admin experience is unchanged

For users holding `admin.administration.read`, the combobox behaviour is identical to today: member list fetched, displayed as dropdown options, searchable, with manual entry as a fallback.

**Acceptance criteria:**
- An admin user sees the member dropdown populated from the API response.
- An admin user can also type a custom entry (manual fallback still works).
- No additional loading state or visual change versus the current behaviour.

### FR-5: Test coverage

The following test cases must be added or updated in `frontend/src/components/common/__tests__/ResponsiblePersonCombobox.test.tsx` and `frontend/src/api/hooks/__tests__/useUserManagement.test.tsx`.

Hook tests (`useUserManagement.test.tsx`):
- When `options.enabled` is `false`, the query does not fire (verify via mock call count = 0 or `status === 'idle'`).
- When `options.enabled` is `true`, the query fires as before.

Component tests (`ResponsiblePersonCombobox.test.tsx`):
- When `usePermissionsContext` returns `hasPermission('admin.administration.read') === false`: `useResponsiblePersonsQuery` is called with `enabled: false`; no error banner is rendered; the combobox input is present and accepts input.
- When `usePermissionsContext` returns `hasPermission('admin.administration.read') === true`: `useResponsiblePersonsQuery` is called with `enabled: true`; members are displayed.

The existing `jest.mock('../../../api/hooks/useUserManagement', ...)` and `jest.mock` pattern for `usePermissionsContext` (already used in sibling test files) must be used consistently.

**Acceptance criteria:**
- All new and existing test cases in the two test files pass under `npm test`.
- No existing tests are deleted; only additions and amendments.

## Non-Functional Requirements

### NFR-1: Performance

The fix eliminates 3 HTTP round-trips per non-admin page load (1 request + 2 retries) that previously returned 403. No new HTTP traffic is introduced. The permissions context value is already resolved at component mount time (it comes from `usePermissions`, which is cached); the `hasPermission` call is synchronous and effectively free.

### NFR-2: Security

The backend `[FeatureAuthorize(Feature.Admin_Administration)]` policy on `GET /api/UserManagement/group-members` must not be modified. The frontend guard is a UX and telemetry improvement only — it is not a security boundary.

### NFR-3: Backwards compatibility

The `useResponsiblePersonsQuery` signature change uses an optional second argument with a safe default (`enabled: true` when omitted), so any future call sites that do not pass the option continue to behave as today.

### NFR-4: Build and lint

`npm run build` and `npm run lint` must pass with zero new warnings or errors.

## Data Model

No data model changes. The affected entities are frontend-only: a React component and a TanStack Query hook.

Key state:
- `canReadAdministration: boolean` — derived from `usePermissionsContext().hasPermission('admin.administration.read')` inside `ResponsiblePersonCombobox`. Drives the `enabled` option passed to the hook.
- `options.enabled: boolean` — new optional field on `useResponsiblePersonsQuery`'s second argument.

## API / Interface Design

### Hook interface change

File: `frontend/src/api/hooks/useUserManagement.ts`

```ts
// New signature
export const useResponsiblePersonsQuery = (
  groupId: string,
  options?: { enabled?: boolean }
) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.userManagement, 'group-members', groupId],
    enabled: Boolean(groupId) && (options?.enabled ?? true),
    queryFn: async (): Promise<GetGroupMembersResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.userManagement_GetGroupMembers(groupId);
    },
    staleTime: 15 * 60 * 1000,
    retry: 2,
    retryDelay: 1000,
  });
};
```

### Component change

File: `frontend/src/components/common/ResponsiblePersonCombobox.tsx`

Add near the top of the component function body, before the `useResponsiblePersonsQuery` call:

```ts
const { hasPermission } = usePermissionsContext();
const canReadAdministration = hasPermission('admin.administration.read');
```

Change the hook call from:

```ts
const { data: response, isLoading, isError } = useResponsiblePersonsQuery(groupId);
```

to:

```ts
const { data: response, isLoading, isError } = useResponsiblePersonsQuery(groupId, {
  enabled: canReadAdministration,
});
```

Add the import at the top of the file:

```ts
import { usePermissionsContext } from '../../auth/PermissionsContext';
```

No other changes to the component or its call sites.

### No backend changes

`backend/src/Anela.Heblo.API/Controllers/UserManagementController.cs` is not touched.

## Dependencies

- `usePermissionsContext` / `PermissionsContext` — already available at `frontend/src/auth/PermissionsContext.tsx`; the `hasPermission` function is synchronous and always present once the provider is mounted.
- `admin.administration.read` permission string — confirmed at `frontend/src/auth/accessMatrix.generated.ts:103`.
- TanStack Query `enabled` flag — already used in the hook for the `Boolean(groupId)` guard; this change extends the same pattern.
- All three parent components (`CreateManufactureOrderModal`, `BasicInfoSection`, `ManufactureOrderFilters`) already pass `allowManualEntry={true}` — no changes needed there.

## Out of Scope

- Relaxing or changing the backend `[FeatureAuthorize(Feature.Admin_Administration)]` policy.
- Exposing a new, non-admin endpoint for fetching group members.
- Changing `allowManualEntry` defaults or adding a new prop to control manual-entry for non-admins (the existing `allowManualEntry={true}` at all call sites covers the fallback).
- Any changes to `CreateManufactureOrderModal`, `BasicInfoSection`, or `ManufactureOrderFilters` — they need no modification.
- E2E test coverage (E2E suite runs nightly and is not part of the PR CI gate for this change).
- Changes to any other hook, component, or controller that consumes `UserManagementController`.

## Open Questions

None.

## Status: COMPLETE
