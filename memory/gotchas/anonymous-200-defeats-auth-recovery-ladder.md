# Anonymous 200 defeats the auth-recovery escalation ladder

## Symptom

User with an expired Entra ID session gets stuck in an infinite silent login loop
(`prompt: "none"` redirect → `login_required` → reload → repeat) until Microsoft's
server-side loop detection (AADSTS50196) temporarily blocks sign-in ("locked out").
This happened even after PR #3067 added the escalation ladder in `authRecovery.ts`.

## Root cause

`client.ts` reset the recovery counter on **any** `response.ok`. `FeatureFlagProvider`
fetches `GET /api/feature-flags` through the same intercepting client on every app
load, and that endpoint is `[AllowAnonymous]` — it returns 200 with **no Authorization
header** even when the session is dead. That 200 wiped the sessionStorage counter every
loop lap (either before the 401 arrived or after the counter was written but before
navigation), pinning the ladder at the silent rung forever.

Note `AuthGuard` does not prevent this: `isAuthenticated` is just
`accounts.length > 0` from MSAL's localStorage cache, so a dead server session still
renders the app (and fires the anonymous fetch).

## Fix (Aug 2026)

1. `client.ts`: reset the counter only when `response.ok && headers["Authorization"]` —
   an anonymous 200 proves nothing about authentication.
2. Defense in depth: `handleMsalAuthEvent` in `authRecovery.ts` (wired into the MSAL
   `addEventCallback` in `App.tsx`) reacts to a redirect-type `LOGIN_FAILURE` carrying
   `InteractionRequiredAuthError` by escalating straight to interactive
   `select_account` (floored via `nextRecoveryAttempt(now, MAX_INTERACTIVE_ATTEMPT)`),
   never retrying `prompt: "none"` — breaks the loop at Entra's own answer regardless
   of counter state.

## Lessons

- Any "reset the failure counter on success" logic must define *success* strictly —
  a 2xx from an `[AllowAnonymous]` endpoint is not an authenticated success.
- When adding a new `[AllowAnonymous]` endpoint called at app startup, check its
  interaction with the 401 recovery path in `client.ts`.
- Test infra: `setupTests.ts` globally mocks `@azure/msal-browser`; under
  `jest.resetModules()` the mock factory re-runs, so `instanceof` checks against mock
  classes need the test to `require()` the class from the same module registry as the
  module under test.
