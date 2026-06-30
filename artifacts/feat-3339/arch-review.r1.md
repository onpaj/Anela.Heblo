# Architecture Review: Admin Permission Guard for ResponsiblePersonCombobox

## Skip Design: true
No new or changed screens, layouts, or visual design decisions. The non-admin path silently downgrades to the existing manual-entry behaviour with no visible indicator change. No new UI components are introduced.

## Architectural Fit Assessment

This change fits the existing patterns precisely. The codebase already has an established permission-gating idiom: call `usePermissionsContext().hasPermission(permString)` inside a component, derive a boolean, and use it to conditionally enable or suppress behaviour. This pattern is confirmed in `ArticleGenerationForm`, `TransportBoxList`, `PhotobankPage`, `Sidebar`, and 11 other files. The `PermissionsContext` is available globally (mounted in the app root), so no provider wrapping is required at the call site.

The `useResponsiblePersonsQuery` hook already uses TanStack Query's `enabled` flag to suppress network traffic (`enabled: Boolean(groupId)`). Extending that guard with a second condition is the natural, minimal change — it does not alter the query key, cache lifetime, retry policy, or any consumer.

Integration points:
- `frontend/src/auth/PermissionsContext.tsx` — provides `hasPermission`, already consumed by 31 files
- `frontend/src/api/hooks/useUserManagement.ts` — single hook that wraps the guarded endpoint
- `frontend/src/components/common/ResponsiblePersonCombobox.tsx` — single component that calls the hook; has 3 render call sites
- `frontend/src/components/common/__tests__/ResponsiblePersonCombobox.test.tsx` — existing test file that already mocks `useResponsiblePersonsQuery`
- `frontend/src/api/hooks/__tests__/useUserManagement.test.tsx` — existing test file for the hook

There are no backend changes required. The controller's `[FeatureAuthorize(Feature.Admin_Administration)]` policy is correct and untouched.

## Proposed Architecture

### Component Overview

```
PermissionsContext (app root)
  └── hasPermission('admin.administration.read') ──────────────────────────────┐
                                                                               │
ResponsiblePersonCombobox.tsx                                                  │
  ├── reads canReadAdministration from usePermissionsContext()  ◄──────────────┘
  └── calls useResponsiblePersonsQuery(groupId, { enabled: canReadAdministration })
                │
                ▼
useUserManagement.ts :: useResponsiblePersonsQuery(groupId, options?)
  ├── enabled: Boolean(groupId) && (options?.enabled ?? true)
  ├── [admin path]  enabled=true  → fires GET /api/UserManagement/group-members
  └── [non-admin]   enabled=false → query stays idle, no HTTP request

Call sites (unchanged):
  CreateManufactureOrderModal.tsx  ──┐
  BasicInfoSection.tsx              ├──► <ResponsiblePersonCombobox groupId=... allowManualEntry={true} />
  ManufactureOrderFilters.tsx       ┘
```

### Key Design Decisions

#### Decision 1: Permission check lives in `ResponsiblePersonCombobox`, not in call sites
**Options considered:**
- A: Add `canFetch` prop to `ResponsiblePersonCombobox` and move the permission check to each of the 3 call sites.
- B: Check permission inside `ResponsiblePersonCombobox` using `usePermissionsContext()` — no prop changes, no call site changes.

**Chosen approach:** B — internal check in the component.

**Rationale:** Option A requires touching 3 call sites and exposes a footgun: any future call site that forgets to pass the prop will re-introduce the 403 regression. Option B makes the guard self-enforcing — the component always gates itself, regardless of how it is instantiated. The component already owns its query invocation; this is an extension of that responsibility. The spec explicitly calls out that call sites must not change (FR-2, FR-5). The `usePermissionsContext` hook is always available once the provider is mounted (throws if called outside, catching misconfigured trees early).

#### Decision 2: `options?: { enabled?: boolean }` as second argument to `useResponsiblePersonsQuery`
**Options considered:**
- A: Add `enabled` directly as a second positional boolean parameter.
- B: Wrap in an options object `{ enabled?: boolean }`.
- C: Keep the hook unchanged; only check permissions inside the component before calling the hook conditionally (would require calling hooks conditionally, which violates Rules of Hooks).

**Chosen approach:** B — optional options object.

**Rationale:** Option A is brittle to extend. Option C is illegal (Rules of Hooks). Option B mirrors TanStack Query's own idiom (`useQuery({ ..., enabled })`) and the pattern used in `usePermissions(enabled: boolean)` in this codebase. The `options?.enabled ?? true` default means existing call sites that omit the argument are unaffected — backwards compatibility is preserved (NFR-3).

#### Decision 3: No change to the non-admin UX render path
**Options considered:**
- A: Add a visual indicator (tooltip or info message) informing non-admins that the dropdown is unavailable.
- B: Silently degrade to the existing manual-entry path — no indicator.

**Chosen approach:** B.

**Rationale:** The combobox already degrades to free-text when `isError=true`, and non-admins are familiar with typing responsible-person names manually (this has been the only UX they have had — the dropdown would never have loaded for them, only produced an invisible 403). Adding a new visual element adds risk, requires design review, and is out of scope. The existing `allowManualEntry={true}` at all three call sites is sufficient.

## Implementation Guidance

### Directory / Module Structure

No new files are required. Changes touch exactly two existing files:

```
frontend/src/
├── api/hooks/
│   ├── useUserManagement.ts                          ← CHANGE: add options param
│   └── __tests__/useUserManagement.test.tsx          ← CHANGE: add 2 test cases
└── components/common/
    ├── ResponsiblePersonCombobox.tsx                 ← CHANGE: add permission read + import
    └── __tests__/ResponsiblePersonCombobox.test.tsx  ← CHANGE: add 2 test cases + mock
```

No backend files, no new files, no call site files.

### Interfaces and Contracts

**`useResponsiblePersonsQuery` new signature** (`frontend/src/api/hooks/useUserManagement.ts`):
```ts
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

**`ResponsiblePersonCombobox` additions** (`frontend/src/components/common/ResponsiblePersonCombobox.tsx`):

New import (add after existing imports):
```ts
import { usePermissionsContext } from '../../auth/PermissionsContext';
```

New lines inside the component function body, immediately before the `useResponsiblePersonsQuery` call:
```ts
const { hasPermission } = usePermissionsContext();
const canReadAdministration = hasPermission('admin.administration.read');
```

Updated hook call:
```ts
const { data: response, isLoading, isError } = useResponsiblePersonsQuery(groupId, {
  enabled: canReadAdministration,
});
```

The permission string `'admin.administration.read'` is confirmed at line 103 of `frontend/src/auth/accessMatrix.generated.ts`. Do not use a magic string literal in any other form.

**No changes to `ResponsiblePersonComboboxProps` interface** — no new prop is added.

### Data Flow

**Non-admin user opens a Manufacture screen:**
1. `PermissionsProvider` has already resolved permissions at app boot (`usePermissions` — cached, staleTime 5 min).
2. `ResponsiblePersonCombobox` mounts; calls `usePermissionsContext()` — synchronous, no network.
3. `hasPermission('admin.administration.read')` returns `false`.
4. `useResponsiblePersonsQuery(groupId, { enabled: false })` is called; TanStack Query sets `status: 'pending'`, `fetchStatus: 'idle'`; no network request is issued.
5. Component renders: `data` is `undefined`, `isLoading` is `false`, `isError` is `false`.
6. `options` memo produces `[]`; combobox renders as an empty searchable/creatable select.
7. User types a name — `allowManualEntry={true}` creates a custom option; `onChange` fires as normal.

**Admin user opens a Manufacture screen:**
1-2. Same as above.
3. `hasPermission('admin.administration.read')` returns `true`.
4. `useResponsiblePersonsQuery(groupId, { enabled: true })` fires as today.
5. Member list loads; dropdown is populated. Behaviour is identical to current prod.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `usePermissionsContext` throws if called outside `PermissionsProvider` | Low | Provider is mounted at app root; all three call sites are deep within the authenticated app tree. No structural change to the provider tree is made. Existing tests already mock the context module. |
| Permissions not yet loaded when component mounts (isLoading=true on first render) | Low | During load, `hasPermission` returns `false` (see `PermissionsContext.tsx` line 27: `data?.permissions ?? []`). This means the query is briefly disabled until permissions resolve. For admins, this causes a ~0 to one-render delay before the member list is fetched — imperceptible. Non-admins are unaffected. |
| Permission string typo silently disables the dropdown for admins | Low | The string `'admin.administration.read'` appears verbatim at `accessMatrix.generated.ts:103`. The spec cites this line. A test case that verifies admin behaviour exercises this path; a typo would fail the test. |
| Future `useResponsiblePersonsQuery` call site omits `options` and regresses | Low | Default `options?.enabled ?? true` keeps the old behaviour; no regression for omitting callers. A new call site in a Manufacture screen that should also gate would need to be identified manually — there is no compile-time enforcement. If the pattern spreads, consider renaming to make the permission-gate responsibility explicit in a wrapper. |
| Test file for `ResponsiblePersonCombobox` does not currently mock `usePermissionsContext` | Medium | The test file must add a `jest.mock('../../auth/PermissionsContext', ...)` block — confirmed to be missing from the existing test. Without it the test will throw `usePermissionsContext must be used within PermissionsProvider`. The mock pattern is established in `ArticleGenerationForm.test.tsx` and `Sidebar.test.tsx` — copy that pattern directly. |

## Specification Amendments

The spec is complete and correct. Two implementation notes that the spec does not make explicit:

1. **Test file gap — PermissionsContext mock is required.** The existing `ResponsiblePersonCombobox.test.tsx` does not mock `usePermissionsContext`. All existing tests will throw `usePermissionsContext must be used within PermissionsProvider` as soon as the import is added to the component. The implementer must add the mock to the existing `describe` block alongside the existing `useUserManagement` mock — not only in the new test cases. This is an implicit prerequisite that the spec omits.

2. **`isSuperUser` path.** `hasPermission` already short-circuits for super users (`data?.isSuperUser ?? false || ...`). No special handling is needed — super users transparently pass the `admin.administration.read` check.

## Prerequisites

None. All dependencies are already in place:
- `usePermissionsContext` / `PermissionsProvider` — in production use by 31 files; no setup required.
- `admin.administration.read` permission string — confirmed in the generated access matrix.
- `allowManualEntry={true}` — already set at all three call sites.
- TanStack Query `enabled` flag — already used in the same hook for the `groupId` guard.
- No migrations, no config changes, no infrastructure changes, no backend changes.
