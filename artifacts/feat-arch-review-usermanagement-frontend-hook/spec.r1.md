# Specification: Remove duplicate type declarations in `useUserManagement.ts`

## Summary
The `useUserManagement.ts` hook manually re-declares `UserDto` and `GetGroupMembersResponse` types that already exist in the auto-generated OpenAPI TypeScript client. Replace the manual declarations with imports from the generated client to eliminate contract drift risk.

## Background
The frontend consumes backend contracts via an auto-generated TypeScript client at `frontend/src/api/generated/api-client.ts`, produced from the backend's OpenAPI spec on every build. This pipeline exists specifically to keep frontend types in lockstep with backend DTOs.

`frontend/src/api/hooks/useUserManagement.ts` bypasses this pipeline by declaring its own `UserDto` (line 4) and `GetGroupMembersResponse` (lines 6–15). These shadow the generated definitions at lines 25618–25659 of `api-client.ts`. Because the structural-typing rules in TypeScript treat the two definitions as compatible when their shapes match, drift between the manual and generated versions cannot be detected by the compiler. If the backend adds, renames, or removes a field on either DTO, the hook will silently retain a stale shape until a runtime bug surfaces — exactly the failure mode the generation pipeline was introduced to prevent.

This is a small, surgical cleanup that aligns the file with the existing pattern used by every other API hook in the codebase.

## Functional Requirements

### FR-1: Remove manual type declarations
Delete the manually authored `UserDto` and `GetGroupMembersResponse` interface declarations (lines 4–15) from `frontend/src/api/hooks/useUserManagement.ts`.

**Acceptance criteria:**
- The file no longer contains an `export interface UserDto` declaration.
- The file no longer contains an `export interface GetGroupMembersResponse` declaration.
- No other code in the file references the deleted declarations as local symbols.

### FR-2: Import canonical types from generated client
Add a type-only import of `UserDto` and `GetGroupMembersResponse` from `../generated/api-client` so the rest of the hook references the generated symbols.

**Acceptance criteria:**
- A single `import type { GetGroupMembersResponse, UserDto } from '../generated/api-client';` statement (or equivalent) is present at the top of the file.
- All in-file references to `UserDto` and `GetGroupMembersResponse` (e.g., the `useQuery` generic parameter and the `queryFn` return type) resolve to the imported types.
- TypeScript compilation succeeds (`npm run build`).
- Lint passes (`npm run lint`).

### FR-3: Preserve external consumer compatibility
The hook previously re-exported `UserDto` and `GetGroupMembersResponse` (via the `export interface` form). Any module that imports those symbols from `useUserManagement.ts` must continue to compile after the change.

**Acceptance criteria:**
- A repository-wide search for `from '.*useUserManagement'` reveals no consumers importing `UserDto` or `GetGroupMembersResponse` from this hook, **or** any such consumers are updated to import the symbols directly from `../generated/api-client`.
- No new TypeScript errors appear in dependent files after the change.

### FR-4: Behavioral parity
The hook's runtime behavior — query key, fetch URL, response handling, error mapping, and React Query options — must remain identical. This is a type-system-only refactor.

**Acceptance criteria:**
- A diff of the file shows no changes outside the type declarations, the new import line, and any type annotations that now reference the imported symbols.
- Any existing tests covering the hook (unit or integration) pass without modification.
- Manual smoke test: the user-management UI that consumes the hook continues to render group members correctly.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected. Type-only imports are erased at compile time.

### NFR-2: Security
No security impact. No data flows, auth boundaries, or input handling are modified.

### NFR-3: Maintainability
After this change, contract drift between frontend and backend for the affected DTOs becomes structurally impossible: the generated client is the single source of truth and is regenerated on every build from the backend's OpenAPI spec.

## Data Model
No data model changes. The shapes of `UserDto` and `GetGroupMembersResponse` are unchanged — only the location of their canonical declaration moves from a hand-written hook file to the generated client.

For reference, the generated shapes (as of the brief) are:
- `UserDto`: `{ id: string; displayName: string; email: string; }`
- `GetGroupMembersResponse`: `{ success: boolean; errorCode?: number; params?: Record<string, string>; members: UserDto[]; }`

## API / Interface Design
No API or UI changes. The change is internal to `frontend/src/api/hooks/useUserManagement.ts`.

## Dependencies
- `frontend/src/api/generated/api-client.ts` — must contain the up-to-date `UserDto` and `GetGroupMembersResponse` exports. Regenerated on every build from the backend OpenAPI spec; no manual action required.

## Out of Scope
- Audit or migration of other hooks that may also re-declare generated types. If discovered during this change, mention them but do not modify them in the same PR.
- Any change to the backend DTOs, OpenAPI spec, or the client generation pipeline itself.
- Refactoring the hook's query logic, error handling, or React Query configuration.
- Renaming the hook, its file, or its public API.

## Open Questions
None.

## Status: COMPLETE