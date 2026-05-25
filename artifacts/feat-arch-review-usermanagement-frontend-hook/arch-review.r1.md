```markdown
# Architecture Review: Remove duplicate type declarations in `useUserManagement.ts`

## Skip Design: true

No UI, layout, or visual design work. The change is internal to a single TypeScript hook file and erased at compile time.

## Architectural Fit Assessment

The proposal is fully aligned with the codebase's existing API contract strategy: the OpenAPI → NSwag pipeline regenerates `frontend/src/api/generated/api-client.ts` from the backend's OpenAPI spec on every build, and most hooks consume those generated symbols. `useUserManagement.ts` is an outlier that hand-redeclares two DTOs the generator already produces — exactly the drift hazard the pipeline exists to prevent.

Two architectural nuances surface when grounding the spec in the actual generated code:

1. **The generated symbols are classes with optional fields, not interfaces with required fields.** The hand-written `UserDto` declares `id`, `displayName`, `email` as **required** strings. The generated `UserDto` declares them as **optional** (`id?: string; ...`) and is a `class` (with `fromJS`/`toJSON`/`init`). The matching pure interface is `IUserDto`. The same shape mismatch applies to `GetGroupMembersResponse` vs `IGetGroupMembersResponse` (where `members` is `UserDto[] | undefined`). The spec describes the change as type-system-only with no consumer impact, but switching to the generated types **narrows what TypeScript proves about runtime values** — fields become possibly-`undefined` and consumers must either narrow or stop relying on the previously-promised non-null guarantees.
2. **The generated client already exposes the call.** `ManufactureOrderClient.manufactureOrder_GetResponsiblePersons()` (api-client.ts:6625) returns `Promise<GetGroupMembersResponse>` and reuses the generated runtime. The hook currently bypasses it with a raw `(apiClient as any).http.fetch(...)`. This is **out of scope** for the current spec, but worth flagging as the next obvious cleanup — see Specification Amendments.

Verified consumer surface: only two files import from `useUserManagement` (`ResponsiblePersonCombobox.tsx`, `useUserManagement.test.tsx`). Neither imports the `UserDto`/`GetGroupMembersResponse` symbols — both reference the hook function only — so FR-3 has no external rewrite work to do.

## Proposed Architecture

### Component Overview

```
backend OpenAPI spec
      │ (build-time codegen)
      ▼
frontend/src/api/generated/api-client.ts        ← single source of truth
      │  exports: class UserDto, interface IUserDto,
      │           class GetGroupMembersResponse, interface IGetGroupMembersResponse
      │
      ▼
frontend/src/api/hooks/useUserManagement.ts     ← consumes generated types
      │  useResponsiblePersonsQuery(): UseQueryResult<GetGroupMembersResponse>
      ▼
frontend/src/components/common/ResponsiblePersonCombobox.tsx
      ▼
UI (Select component, downstream pages)
```

### Key Design Decisions

#### Decision 1: Import the I-prefixed interface, not the class

**Options considered:**
- **A.** `import type { UserDto, GetGroupMembersResponse } from '../generated/api-client'` — uses the class symbols as types.
- **B.** `import type { IUserDto, IGetGroupMembersResponse } from '../generated/api-client'` — uses the pure structural interfaces.
- **C.** Wrap the generated types in a local alias that re-asserts required fields (`type ResponsiblePerson = Required<IUserDto>`).

**Chosen approach:** **Option A** — import `UserDto` and `GetGroupMembersResponse` (the classes used as type positions). This is what the spec writes and what other generated-type consumers in the codebase do. Type-only imports of classes are erased at compile time, so no runtime cost.

**Rationale:** Matches the spec verbatim, matches conventional NSwag usage elsewhere, and keeps the hook's JSDoc/IntelliSense pointing at the named class (more discoverable than the `I…` interface). The `I…` interface is structurally identical to the class shape for typing purposes, so Option B would be functionally equivalent but stylistically inconsistent with the rest of the codebase. Option C would silently lie about runtime data — if the backend omits a field, `Required<…>` would still claim it is present.

#### Decision 2: Accept the optional-field semantics

**Options considered:**
- **A.** Leave the consumer alone; rely on existing `response?.members || []` and trust that `displayName`/`email` are present at runtime (with implicit non-null assumption).
- **B.** Tighten the combobox to explicitly narrow `member.displayName` / `member.email` before use.
- **C.** Introduce a local "narrowed" view model inside the hook that maps the generated optional-field DTO to a required-field shape, preserving the consumer's current invariants.

**Chosen approach:** **Option A** for this PR — defer B/C to a follow-up. The spec is explicitly a type-only refactor with behavioral parity (FR-4), and the consumer already defends against `members` being undefined.

**Rationale:** Touching the consumer expands the diff beyond what the spec authorizes and the brief asked for. However, the architect must flag that **TypeScript may emit new errors on lines 46–49 of `ResponsiblePersonCombobox.tsx`** (`member.displayName`, `member.email` used as `string` when they are now `string | undefined`). If those errors appear, the surgical fix is `member.displayName ?? ''` / `member.email ?? ''` at the call site — see Specification Amendments.

#### Decision 3: Do not migrate the raw fetch to the generated client method

**Options considered:**
- **A.** Replace the `(apiClient as any).http.fetch(...)` block with `apiClient.manufactureOrder_GetResponsiblePersons()`.
- **B.** Leave the runtime path untouched.

**Chosen approach:** **Option B**, deferred. The spec scopes the change to type declarations only (Out of Scope: "Refactoring the hook's query logic"). The migration to the generated method is a separate, clearly-justified follow-up.

**Rationale:** Discipline. The brief is surgical. Bundling unrelated cleanup violates "every changed line should trace directly to the request."

## Implementation Guidance

### Directory / Module Structure

No new files. All edits land in:
- `frontend/src/api/hooks/useUserManagement.ts` (modify)

Possibly affected (if TypeScript flags optional-field usage):
- `frontend/src/components/common/ResponsiblePersonCombobox.tsx` (defensive `??` fallbacks only)

### Interfaces and Contracts

The hook's exported signature stays:

```ts
export const useResponsiblePersonsQuery = (): UseQueryResult<GetGroupMembersResponse, Error>
```

where `GetGroupMembersResponse` now resolves to the generated class symbol (`members?: UserDto[]` with `UserDto` having `id?`, `displayName?`, `email?` all optional).

Implementation diff is essentially:

```diff
+ import type { GetGroupMembersResponse, UserDto } from '../generated/api-client';
  import { useQuery } from '@tanstack/react-query';
  import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

- export interface UserDto { id: string; displayName: string; email: string; }
- export interface GetGroupMembersResponse {
-   success: boolean;
-   errorCode?: number;
-   params?: Record<string, string>;
-   members: UserDto[];
- }
```

`UserDto` may be unused after deletion if no annotation in the file references it directly (the `queryFn` only annotates `GetGroupMembersResponse`). If unused, omit it from the import to keep `npm run lint` clean.

### Data Flow

Unchanged at runtime. The hook still:
1. Awaits `getAuthenticatedApiClient()`.
2. Builds absolute URL with `${baseUrl}/api/ManufactureOrder/responsible-persons`.
3. Calls `http.fetch`, throws on non-OK, returns `response.json()`.
4. React Query caches under `[...QUERY_KEYS.userManagement, 'responsible-persons']` for 15 minutes.

The only change is that the compiler now types the JSON payload via the generated class shape rather than the local interface.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Generated `UserDto`/`GetGroupMembersResponse` fields are optional; consumer code assuming non-null may break at compile time | Medium | Run `npm run build` immediately after the swap. If errors surface in `ResponsiblePersonCombobox.tsx`, apply `member.displayName ?? ''` / `member.email ?? ''` narrowing in that file. Do **not** widen the generated types. |
| `UserDto` import unused after edit, tripping `no-unused-vars` lint | Low | Only import what the file references. If `UserDto` is not annotated anywhere in the file, import only `GetGroupMembersResponse`. |
| Generated `GetGroupMembersResponse` lacks the runtime fields `success`/`errorCode`/`params` that the old manual interface had | Medium | The generated `GetGroupMembersResponse extends BaseResponse` — `BaseResponse` provides those fields. Verify by reading `BaseResponse` definition; if the inherited shape covers the consumer's actual usage, no consumer change is needed. If a field used by a consumer is missing, that is a backend OpenAPI gap, not a frontend bug — file as a separate finding. |
| Test file (`useUserManagement.test.tsx`) mocks payloads as plain objects — generated class types may demand `fromJS()` construction | Low | Tests assert on React Query lifecycle (`isLoading`, `failureCount`, etc.) and never type-check the response payload itself. Spot-check after the change; no edit anticipated. |
| Future contributor re-introduces a manual DTO declaration in another hook | Low | Mention `useMeetingTasks.ts` (lines 6–101) in the PR description as an out-of-scope sibling already justified with a `// TODO: migrate to generated client when /api/meeting-tasks is added to NSwag` comment. No action this PR. |

## Specification Amendments

1. **FR-4 (Behavioral parity) needs a caveat.** The generated `UserDto` declares `id`, `displayName`, `email` as **optional**; the manual interface declared them as **required**. Even though runtime payloads are unchanged, the compiler may emit new errors at consumer sites. Amend FR-4 to read: *"The hook's runtime behavior must remain identical. Compile-time errors arising solely from the stricter optional-field typing on consumers may be resolved with nullish-coalescing fallbacks (`?? ''`) at the call site, scoped to the minimum necessary lines."*

2. **FR-3 acceptance is already satisfied.** Repo-wide grep confirms no consumer imports `UserDto` or `GetGroupMembersResponse` from `useUserManagement` — only the hook function is imported. The "consumers updated" branch of the acceptance criteria does not apply. Note this in the PR description rather than auditing again.

3. **Add an explicit "next steps" non-requirement.** Two follow-up opportunities surfaced during review but are correctly out of scope:
   - Replace the raw `(apiClient as any).http.fetch(...)` with `apiClient.manufactureOrder_GetResponsiblePersons()` — eliminates the `as any` cast and uses the generated request pipeline.
   - Audit `useMeetingTasks.ts` once `/api/meeting-tasks` is added to the NSwag spec (already TODO'd).
   Both should be filed as separate issues from the PR description; do not bundle.

## Prerequisites

- `frontend/src/api/generated/api-client.ts` already contains the up-to-date `UserDto` and `GetGroupMembersResponse` exports (verified at lines 26226 and 26267).
- `npm run build` must run cleanly on `main` before the change so any new errors are unambiguously attributable to the refactor.
- No backend changes, migrations, infrastructure, or config required.
```