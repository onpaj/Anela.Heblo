## Module / File
`frontend/src/auth/userStorage.ts`

## Coverage
Line coverage: 49% (filter threshold: 60%)

## What's not tested
**`getUserInfo` — expiry check:**
When stored user info has an `expiresAt` timestamp in the past, `getUserInfo` clears storage and returns `null`. No test verifies this expired-session path. If the comparison were accidentally inverted (`<` instead of `>`), expired sessions would be returned as valid and active sessions would be cleared — a silent auth regression.

**`updateUserInfo` — silent no-op when no session:**
`updateUserInfo` reads the current session first and only writes if something is there. If there is no current session, the update silently does nothing. No test confirms this behavior; a caller that expects an update to "create" a session would be surprised.

**`getLastLogin` — null when no entry:**
The `getLastLogin` method returns `null` when `LAST_LOGIN_KEY` is absent from sessionStorage. No test covers the absent-key path.

## Why it matters
`getUserInfo` is the auth gate — if the expiry comparison regresses, the app could serve expired credentials or log users out spuriously. The `updateUserInfo` silent-skip means callers who expect it to set initial data will find nothing persisted.

## Suggested approach
- Test `getUserInfo` with an `expiresAt` 1 second in the past: assert returns `null` and sessionStorage is cleared.
- Test `getUserInfo` with an `expiresAt` 1 hour in the future: assert the stored info is returned unchanged.
- Test `updateUserInfo` when no session exists: assert nothing is written to sessionStorage. ~0.5 day effort.

---
_Filed by weekly coverage-gap routine on 2026-07-06. Based on CI run #28716987459 (2ad2a2593e1834798a3def9ac2551b46c2e595cb)._
