# Ralph Progress Log

This file tracks progress across iterations. Agents update this file
after each iteration and it's included in prompts for context.

## Codebase Patterns (Study These First)

- **UserStorage**: `frontend/src/auth/userStorage.ts` - static class for managing app-level user session data in sessionStorage. Use `UserStorage.clearUserInfo()` to clear only app data without touching MSAL's PKCE state.
- **clearTokenCache**: exported from `frontend/src/api/client.ts` - clears the API client token cache. Both `UserStorage.clearUserInfo()` + `clearTokenCache()` together replace a full `sessionStorage.clear()` when you need to preserve MSAL state.

---

## 2026-02-18 - US-001
- What was implemented: Replaced `sessionStorage.clear()` with targeted clearing in the `setGlobalAuthRedirectHandler` callback in `frontend/src/App.tsx`. The fix preserves MSAL's PKCE verifier in sessionStorage so the auth code exchange can succeed after login redirect.
- Files changed: `frontend/src/App.tsx`
  - Added import: `import { UserStorage } from "./auth/userStorage";`
  - Replaced `sessionStorage.clear()` with `UserStorage.clearUserInfo(); clearTokenCache();`
- **Learnings:**
  - `UserStorage` was not previously imported in App.tsx; it lives in `frontend/src/auth/userStorage.ts`
  - `clearTokenCache` was already imported from `./api/client`
  - Pre-existing lint errors exist in test files (unrelated to this change); `App.tsx` itself is lint-clean
  - The react-i18next `node_modules` type errors are pre-existing and not introduced by this change
---
