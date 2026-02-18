# PRD: Fix Blank Screen After Token Expiration Re-Login

## Introduction

When a user leaves the app idle long enough for their Microsoft Entra ID access token to expire,
the next action triggers 401 responses. The app redirects to login, but after successful re-login
the user sees a **completely blank white screen** — even after a hard refresh (F5). The URL looks
correct (e.g. `/catalog`) but nothing renders.

**Root cause:** When multiple API calls fire simultaneously after idle, each 401 triggers
`sessionStorage.clear()` + `loginRedirect()`. The second `sessionStorage.clear()` wipes the PKCE
verifier that the first `loginRedirect()` just wrote into sessionStorage. MSAL can no longer
validate the auth code when Microsoft redirects back, leaving the app in a broken state.

**Secondary issue:** After re-login, the user always lands on `/` (Dashboard) rather than on the
page they were on before the token expired.

**GitHub issue:** https://github.com/onpaj/Anela.Heblo/issues/369

---

## Goals

- Eliminate the blank white screen that appears after token expiration re-login
- Ensure hard refresh after re-login works correctly
- Return the user to the page they were on before the token expired
- Keep the fix contained to `App.tsx` and `AuthGuard.tsx` — no new files

---

## User Stories

### US-001: Replace aggressive sessionStorage.clear() with targeted clearing

**Description:** As a developer, I want the auth redirect handler to only clear app-level
session data so that MSAL's PKCE verifier and interaction state in sessionStorage are preserved
for the auth code exchange after login.

**Context:**
`App.tsx` line 168 calls `sessionStorage.clear()` before `instance.loginRedirect()`. This nukes
MSAL's own sessionStorage keys (`msal.*`) including the PKCE verifier and nonce that MSAL just
wrote for the in-flight redirect. After login, MSAL cannot validate the returned auth code.

**Change:**
In the `setGlobalAuthRedirectHandler` callback in `App.tsx`, replace:
```typescript
sessionStorage.clear();
```
with:
```typescript
UserStorage.clearUserInfo();
clearTokenCache();
```
`UserStorage` and `clearTokenCache` are already imported in `App.tsx`. Do NOT add
`sessionStorage.clear()` anywhere else in this handler.

**Acceptance Criteria:**
- [ ] `sessionStorage.clear()` is removed from the `setGlobalAuthRedirectHandler` callback in `App.tsx`
- [ ] `UserStorage.clearUserInfo()` is called instead to clear user info keys
- [ ] `clearTokenCache()` is called instead to clear the app-level token cache
- [ ] MSAL's sessionStorage keys (`msal.*`) are not touched by the handler
- [ ] Typecheck passes (`npm run build` or `npx tsc --noEmit`)

---

### US-002: Deduplicate concurrent loginRedirect calls with a guard flag

**Description:** As a developer, I want only the first 401 to trigger `loginRedirect()` when
multiple API calls fail simultaneously after idle, so that concurrent calls do not corrupt each
other's PKCE state.

**Context:**
When the user returns from idle, 3–10 in-flight API calls can all receive 401 at once. Each one
invokes `globalAuthRedirectHandler`, causing multiple concurrent `loginRedirect()` calls. The second
call either throws `interaction_in_progress` (its catch then calls `window.location.href = "/"`,
bypassing MSAL) or writes over the PKCE state of the first call.

**Change:**
Add an `isRedirecting` boolean flag in module scope (outside the `App` component, or as a `useRef`)
inside the `setGlobalAuthRedirectHandler` callback. The flag must be:
- Set to `true` before calling `loginRedirect()`
- Checked at the start of the handler — return early if already `true`
- Reset to `false` in the `.catch()` block (so failed redirects don't permanently lock the flag)

```typescript
// Pseudocode - exact implementation may vary
let isRedirecting = false;

setGlobalAuthRedirectHandler(() => {
  if (isRedirecting) return;  // deduplicate
  isRedirecting = true;

  // ... save returnUrl, clear user data, call loginRedirect ...

  instance.loginRedirect({ ... })
    .catch((error) => {
      console.error('Login redirect failed:', error);
      isRedirecting = false;  // allow retry
      window.location.href = '/';
    });
});
```

**Acceptance Criteria:**
- [ ] A boolean guard variable (e.g. `isRedirecting`) is checked at the top of the `setGlobalAuthRedirectHandler` callback — returns immediately if already `true`
- [ ] The guard is set to `true` before calling `loginRedirect()`
- [ ] The guard is reset to `false` in the `.catch()` handler of `loginRedirect()`
- [ ] If multiple 401 responses arrive simultaneously, only one `loginRedirect()` call is made
- [ ] Typecheck passes

---

### US-003: Save returnUrl to localStorage before triggering login redirect

**Description:** As a developer, I want to save the user's current URL to `localStorage`
before the login redirect so it can be restored after re-login.

**Context:**
Currently after re-login the `redirectUri` is `window.location.origin` (`/`), so the user always
lands on Dashboard. There is no mechanism to know where they were before the token expired.
`localStorage` is used (not `sessionStorage`) because it persists through the full page redirect
cycle and is not cleared by `UserStorage.clearUserInfo()` or `clearTokenCache()`.

**Change:**
In the `setGlobalAuthRedirectHandler` callback in `App.tsx`, before calling `loginRedirect()`,
save the current path + search string to `localStorage`:

```typescript
const returnUrl = window.location.pathname + window.location.search;
if (returnUrl && returnUrl !== '/') {
  localStorage.setItem('auth.returnUrl', returnUrl);
}
```

**Acceptance Criteria:**
- [ ] `localStorage.setItem('auth.returnUrl', ...)` is called inside `setGlobalAuthRedirectHandler` before `loginRedirect()`
- [ ] The saved value includes `window.location.pathname` and `window.location.search` (e.g. `/catalog?page=2`)
- [ ] If the current path is `/`, nothing is saved (avoid saving root as returnUrl)
- [ ] The key used is exactly `'auth.returnUrl'`
- [ ] Typecheck passes

---

### US-004: Restore user to returnUrl after successful re-login

**Description:** As a user, I want to be taken back to the page I was on before my session
expired after I successfully log in again, so I don't lose my context.

**Context:**
After MSAL processes the auth code redirect, the app loads at `/` (the `redirectUri`). `AuthGuard`
checks `isAuthenticated` and renders children once the user is authenticated. This is the right
place to check for a saved `returnUrl` and navigate there using React Router (not
`window.location.href`, which would cause a full page reload).

**Change:**
In `AuthGuard.tsx`, after the user is confirmed authenticated, check `localStorage` for
`auth.returnUrl`. If found and different from the current path, navigate there and clear the key:

```typescript
// Inside AuthGuard, after isAuthenticated is true and inProgress is "none"
useEffect(() => {
  if (isAuthenticated && inProgress === 'none') {
    const returnUrl = localStorage.getItem('auth.returnUrl');
    if (returnUrl && returnUrl !== window.location.pathname) {
      localStorage.removeItem('auth.returnUrl');
      navigate(returnUrl);
    }
  }
}, [isAuthenticated, inProgress]);
```

`navigate` comes from `useNavigate()` (React Router). This hook is available inside `AuthGuard`
because `AuthGuard` is rendered inside `<Router>` in `App.tsx`.

**Acceptance Criteria:**
- [ ] `AuthGuard.tsx` reads `localStorage.getItem('auth.returnUrl')` after `isAuthenticated === true` and `inProgress === 'none'`
- [ ] If a non-empty returnUrl is found and differs from the current path, `navigate(returnUrl)` is called (using React Router's `useNavigate`)
- [ ] `localStorage.removeItem('auth.returnUrl')` is called immediately before/after navigating (clean up)
- [ ] If returnUrl is empty, null, or equals the current path, nothing happens (no extra navigation)
- [ ] No full page reload is caused — React Router `navigate()` is used, NOT `window.location.href`
- [ ] Typecheck passes
- [ ] Verify in browser using dev-browser skill

---

### US-005: Clean up stale returnUrl on clean app start

**Description:** As a developer, I want stale `auth.returnUrl` entries in `localStorage` to be
cleaned up when the app starts normally (not via a login redirect), so that leftover data from
a previously failed redirect cycle doesn't cause unexpected navigation.

**Context:**
If `loginRedirect()` was called but the user manually navigated away, closed the tab, or the
redirect failed non-gracefully, `auth.returnUrl` might linger in `localStorage`. On the next
clean app start (no `?code=` in the URL), this stale value should be removed.

**Change:**
In `App.tsx`, inside the `initializeApp` useEffect, after the MSAL instance is created but before
setting up the token provider, add:

```typescript
// Clean up stale returnUrl if this is a clean start (no auth redirect in URL)
const isHandlingRedirect = window.location.search.includes('code=') ||
                           window.location.hash.includes('code=');
if (!isHandlingRedirect) {
  localStorage.removeItem('auth.returnUrl');
}
```

This runs only on a fresh app start, not during the MSAL redirect callback where the returnUrl
is still needed.

**Acceptance Criteria:**
- [ ] On a normal app start (no `?code=` or `#code=` in the URL), `localStorage.removeItem('auth.returnUrl')` is called during app initialization in `App.tsx`
- [ ] On an MSAL redirect callback start (URL contains `code=`), the cleanup is skipped — the returnUrl is preserved for US-004 to use
- [ ] Typecheck passes

---

## Functional Requirements

- FR-1: Only one `loginRedirect()` call fires per token expiry event, even if 10 API calls return 401 simultaneously
- FR-2: MSAL's own sessionStorage keys (`msal.*`) are never cleared by the app's auth redirect handler
- FR-3: App-level user data (`UserStorage`, token cache) is cleared on token expiry, same as before
- FR-4: The user's current URL path+search is saved to `localStorage('auth.returnUrl')` before any login redirect
- FR-5: After re-login, if `auth.returnUrl` exists in localStorage, React Router navigates there without a full page reload
- FR-6: `auth.returnUrl` is removed from localStorage once used (in US-004) or on clean app start (in US-005)

---

## Non-Goals

- No changes to backend/API — this is a pure frontend fix
- No changes to MSAL configuration (`msalConfig.ts`, cache location, redirect URI)
- No new files — changes are limited to `App.tsx` and `AuthGuard.tsx`
- No fix for the case-insensitive search bug (separate issue)
- No changes to E2E test mode (`isE2ETestMode()` check remains unchanged)
- No custom login page or login UI — uses Microsoft Entra ID login as-is

---

## Technical Considerations

- **Files changed:** `frontend/src/App.tsx`, `frontend/src/components/auth/AuthGuard.tsx`
- **`isRedirecting` flag location:** Module scope or `useRef` inside the App component — must survive re-renders but reset on page reload (so a `let` variable outside the component function works; a React `useRef` also works if the component doesn't unmount)
- **`useNavigate` in AuthGuard:** Already available since `AuthGuard` is inside `<Router>` in App.tsx — just add `const navigate = useNavigate()` at the top of the component
- **`localStorage` vs `sessionStorage`:** `returnUrl` MUST go to `localStorage` — `sessionStorage` is cleared during the handler (US-001 removes `sessionStorage.clear()` but the MSAL redirect takes the user to a new page load, which starts a fresh sessionStorage context in some browsers)
- **MSAL `inProgress` check:** The `inProgress === 'none'` guard in US-004's useEffect is important — navigating while MSAL is still processing the redirect could interfere with auth handling

---

## Success Metrics

- Token expiry → re-login → correct page renders without blank screen: **100% of the time**
- Hard refresh after re-login: app renders correctly
- User lands on the same page they left (e.g. `/catalog?page=2`) after re-login

---

## Open Questions

- Should the `isRedirecting` flag be reset on a successful login (not just failed one)? Since the page fully reloads after a successful MSAL redirect, the module-scope variable is automatically reset — no extra reset needed for the success path.
- What if `auth.returnUrl` points to a route that requires specific query state (e.g., a modal open state in URL params)? The full `pathname + search` is saved, so query params are preserved.
