# Ralph Progress Log

This file tracks progress across iterations. Agents update this file
after each iteration and it's included in prompts for context.

## Codebase Patterns (Study These First)

- **UserStorage**: `frontend/src/auth/userStorage.ts` - static class for managing app-level user session data in sessionStorage. Use `UserStorage.clearUserInfo()` to clear only app data without touching MSAL's PKCE state.
- **clearTokenCache**: exported from `frontend/src/api/client.ts` - clears the API client token cache. Both `UserStorage.clearUserInfo()` + `clearTokenCache()` together replace a full `sessionStorage.clear()` when you need to preserve MSAL state.
- **Frontend test runner**: Use `CI=true npm test -- --testPathPattern="..." --no-coverage` from the `frontend/` directory. `npx jest` alone doesn't work - Babel isn't configured for TypeScript without react-scripts.
- **AuthGuard mock pattern**: When mocking auth hooks in tests, `login` must return a Promise (`jest.fn().mockResolvedValue(undefined)`) because `AuthGuard` calls `login().catch(...)`.

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

## 2026-02-18 - US-003
- What was implemented: Added returnUrl saving to localStorage before `loginRedirect()` fires in the `setGlobalAuthRedirectHandler` callback in `frontend/src/App.tsx`. Reads `window.location.pathname + window.location.search`, and saves it under key `'auth.returnUrl'` only if non-empty and not equal to `'/'`.
- Files changed: `frontend/src/App.tsx`
  - Added 4 lines before the `UserStorage.clearUserInfo()` call: reads `returnUrl` from `window.location`, conditionally calls `localStorage.setItem('auth.returnUrl', returnUrl)`
- **Learnings:**
  - The pre-existing react-i18next node_modules type errors do not affect `App.tsx` src code; typecheck on src is clean
  - Standard DOM APIs (`window.location.pathname`, `window.location.search`, `localStorage.setItem`) require no imports in TypeScript
  - The returnUrl save must happen BEFORE clearing session data, so the user's current route is captured before any state is wiped
---

## 2026-02-18 - US-002
- What was implemented: Added `isRedirecting` boolean guard in module scope of `frontend/src/App.tsx` to prevent concurrent `loginRedirect()` calls from corrupting each other's PKCE state.
- Files changed: `frontend/src/App.tsx`
  - Added `let isRedirecting = false;` in module scope (before `queryClient` creation)
  - Added `if (isRedirecting) return;` at the start of the `setGlobalAuthRedirectHandler` callback
  - Added `isRedirecting = true;` immediately after the early-return guard
  - Added `isRedirecting = false;` in the `.catch()` handler to allow retry after failed redirect
- **Learnings:**
  - Module-scope variables persist across React re-renders and component remounts, making them ideal for singleton guards
  - The `.catch()` reset is critical: without it, a failed `loginRedirect()` would permanently block future redirect attempts
  - Pre-existing react-i18next node_modules type errors (not from src/) are pre-existing and unrelated to this change
---

## 2026-02-18 - US-005
- What was implemented: Added `isHandlingRedirect` check in the `initializeApp` useEffect in `frontend/src/App.tsx`, immediately after the MSAL instance is created. On a clean app start (no `code=` in URL search or hash), `localStorage.removeItem('auth.returnUrl')` is called to clear any stale returnUrl. During MSAL redirect callbacks, the key is preserved so US-004's navigation logic can use it.
- Files changed: `frontend/src/App.tsx`
  - Added 4 lines after `setMsalInstance(instance)`: checks `window.location.search.includes('code=') || window.location.hash.includes('code=')`, and conditionally calls `localStorage.removeItem('auth.returnUrl')`
- **Learnings:**
  - Placement right after `setMsalInstance` ensures MSAL instance exists before any redirect handling, and cleanup happens before token provider/redirect handler setup
  - Standard DOM APIs (`window.location.search`, `window.location.hash`, `localStorage.removeItem`) need no imports in TypeScript
  - `window.location.hash` must also be checked because MSAL can use hash-based redirect mode
---

## 2026-02-18 - US-004
- What was implemented: Added `useNavigate` from react-router-dom to `AuthGuard.tsx` and a new `useEffect` that runs when `isAuthenticated` and `inProgress` change. When `isAuthenticated === true` and `inProgress === 'none'`, reads `localStorage.getItem('auth.returnUrl')`. If non-empty and different from `window.location.pathname`, removes the key and calls `navigate(returnUrl)`.
- Files changed:
  - `frontend/src/components/auth/AuthGuard.tsx` - added `useNavigate` import and returnUrl redirect effect
  - `frontend/src/components/auth/__tests__/AuthGuard.test.tsx` - new test file with 6 tests covering navigation and non-navigation cases
- **Learnings:**
  - ESLint `import/first` rule: all ES imports must come before any statements (including `jest.mock()` calls). `jest.mock()` is hoisted by Jest at runtime, so placing them after imports in source order is lint-clean.
  - `jest.MockedFunction<typeof X>` cast variables placed after imports don't trigger `import/first` - only ES `import` statements must be first.
  - Pre-existing lint errors exist in many test files across the codebase; my new files are clean.
---
