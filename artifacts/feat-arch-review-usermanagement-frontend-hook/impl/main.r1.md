Final review: ✅ APPROVED with no issues.

---

# Implementation: Remove duplicate type declarations in `useUserManagement.ts`

## What was implemented
Deleted the hand-rolled `UserDto` and `GetGroupMembersResponse` interface declarations from `useUserManagement.ts` and replaced them with a type-only import from the auto-generated OpenAPI client. Applied the authorized `?? ''` narrowing fallback in `ResponsiblePersonCombobox.tsx` where the now-optional generated fields were previously assumed non-null.

## Files created/modified
- `frontend/src/api/hooks/useUserManagement.ts` — removed 12 lines of manual type declarations (lines 4–15); added `import type { GetGroupMembersResponse } from '../generated/api-client'`
- `frontend/src/components/common/ResponsiblePersonCombobox.tsx` — added `?? ''` nullish-coalescing fallbacks on lines 46–49 for `member.displayName` and `member.email`

## Tests
Pre-existing Jest configuration is broken (TypeScript preset not configured) — a pre-existing issue unrelated to this refactor. Build (`npm run build`) and lint (`npm run lint`) both pass cleanly with no new errors.

## How to verify
```bash
git -C <worktree> show --stat HEAD   # 2 files, commit ca6685fb
cd frontend && npm run build          # exits 0, no TS errors
cd frontend && npm run lint           # exits 0, 149 pre-existing warnings
```

## Notes
- Baseline Jest infrastructure was broken before this change — flagged, not fixed (out of scope)
- `UserDto` was not imported since it is not referenced anywhere in the hook body; importing it unused would trip lint
- Two follow-ups documented in the commit message but not touched:
  1. Replace `(apiClient as any).http.fetch(...)` with `apiClient.manufactureOrder_GetResponsiblePersons()`
  2. Audit `useMeetingTasks.ts` once `/api/meeting-tasks` is added to the NSwag spec

## PR Summary
Removes the hand-rolled `UserDto` and `GetGroupMembersResponse` interfaces from `useUserManagement.ts` and imports them from the auto-generated API client instead. This was the only hook in the codebase still re-declaring generated DTOs, so after this change the OpenAPI → NSwag pipeline is the single source of truth for both shapes and contract drift becomes structurally impossible.

The generated `UserDto` declares `displayName`/`email` as optional, so `ResponsiblePersonCombobox.tsx` receives the minimum-scope `?? ''` narrowing fallback at the call site rather than widening the generated types.

### Changes
- `frontend/src/api/hooks/useUserManagement.ts` — removed 12-line manual type block; added `import type { GetGroupMembersResponse } from '../generated/api-client'`
- `frontend/src/components/common/ResponsiblePersonCombobox.tsx` — added `?? ''` fallbacks on 4 lines to satisfy the generated type's optional-field semantics

## Status
DONE