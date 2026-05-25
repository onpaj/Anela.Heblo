## Module
UserManagement

## Finding
`frontend/src/api/hooks/useUserManagement.ts` (lines 4–15) manually declares `UserDto` and `GetGroupMembersResponse` interfaces:

```typescript
export interface UserDto { id: string; displayName: string; email: string; }
export interface GetGroupMembersResponse { success: boolean; errorCode?: number; params?: Record<string, string>; members: UserDto[]; }
```

These types already exist in the auto-generated TypeScript client at `frontend/src/api/generated/api-client.ts` (lines 25618–25659 for `GetGroupMembersResponse`, line 25659 for `UserDto`). The generated versions are derived from the backend's OpenAPI spec and are guaranteed to stay in sync with the backend contracts.

## Why it matters
Manual duplication creates drift risk: if the backend contract changes (e.g. a field is added to `UserDto`), the generated client is updated automatically on build but the manual declaration in `useUserManagement.ts` is not — leading to a type mismatch that TypeScript cannot catch (since both sides are structural). This is exactly the problem the OpenAPI client generation pipeline is designed to prevent.

## Suggested fix
Import the generated types instead of re-declaring them:

```typescript
import type { GetGroupMembersResponse, UserDto } from '../generated/api-client';
```

Remove lines 4–15 from `useUserManagement.ts`. The hook's `queryFn` return type annotation and `response.members` usage will then reference the canonical generated types.

---
_Filed by daily arch-review routine on 2026-05-24._