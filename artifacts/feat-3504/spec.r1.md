# Specification: Test Coverage for `UserStorage` Session Expiry, No-Session Update, and Missing Last-Login Paths

## Summary
`frontend/src/auth/userStorage.ts` sits at 49% line coverage against a 60% threshold, and the untested lines are exactly the ones that decide whether a user is treated as authenticated. This spec defines unit tests for `UserStorage.getUserInfo()`'s expiry comparison, `UserStorage.updateUserInfo()`'s no-op behavior when no session exists, and `UserStorage.getLastLogin()`'s absent-key path. No production code changes are required — this is a test-only addition that pins down existing, correct behavior so future edits (e.g., an inverted comparison operator) fail CI instead of shipping a silent auth regression.

## Background
`UserStorage` is a static utility class wrapping `sessionStorage` under two keys: `anela_heblo_user_info` (`USER_INFO_KEY`) and `anela_heblo_last_login` (`LAST_LOGIN_KEY`). It backs the app's client-side session gate: `getUserInfo()` is called wherever the app decides if a user is still logged in, and its expiry check (`userInfo.expiresAt && new Date() > new Date(userInfo.expiresAt)`, line 48 of `userStorage.ts`) is the single line standing between "expired session correctly rejected" and "expired session silently accepted" (or the inverse: active sessions spuriously logged out). This logic has no direct test today. The brief additionally flags two lower-severity but still user-visible gaps: `updateUserInfo()` silently doing nothing when called with no active session (lines 98–107), and `getLastLogin()` returning `null` when `LAST_LOGIN_KEY` was never set (lines 85–93). Coverage tooling currently reports 49% line coverage for the file; adding tests for these three paths is expected to clear the 60% threshold.

This is a test-authoring task only. No changes to `userStorage.ts` itself are anticipated; if a test reveals an actual defect, that must be raised as a separate finding rather than silently "fixed" to make tests pass.

## Functional Requirements

### FR-1: `getUserInfo()` returns `null` and clears storage when `expiresAt` is in the past
Add a test that seeds `sessionStorage` with a `StoredUserInfo` JSON blob (matching the `StoredUserInfo` interface: base `UserInfo` fields + `lastLogin: string` + `expiresAt?: string`) under `USER_INFO_KEY`, with `expiresAt` set to a timestamp strictly in the past (e.g. `new Date(Date.now() - 1000).toISOString()`, per the brief's "1 second in the past"). Call `UserStorage.getUserInfo()` and assert:
- The return value is `null`.
- `sessionStorage.getItem(USER_INFO_KEY)` is `null` afterward (i.e. `clearUserInfo()` was invoked, per line 50).
- `sessionStorage.getItem(LAST_LOGIN_KEY)` is also `null` afterward, since `clearUserInfo()` removes both keys.

**Acceptance criteria:**
- Test fails if the `>` comparison on line 48 is inverted to `<` (i.e., it would then fail to clear an expired entry and return the stale data instead of `null`).
- Test fails if `clearUserInfo()` is not called on the expired path (storage keys remain populated).

### FR-2: `getUserInfo()` returns stored info unchanged when `expiresAt` is in the future
Add a test that seeds `sessionStorage` with a valid `StoredUserInfo` blob under `USER_INFO_KEY` with `expiresAt` set roughly one hour in the future (`new Date(Date.now() + 60 * 60 * 1000).toISOString()`, per the brief). Call `UserStorage.getUserInfo()` and assert:
- The return value deep-equals the originally stored object (all `UserInfo` fields, `lastLogin`, and `expiresAt` preserved, no mutation).
- `sessionStorage` still contains the entry after the call (not cleared).

**Acceptance criteria:**
- Test fails if the expiry comparison is inverted (would incorrectly treat a future-dated session as expired and return `null` / clear storage).
- Test fails if `getUserInfo()` mutates or drops any field when passing through valid data.

### FR-3: `getUserInfo()` treats a missing `expiresAt` as never-expiring
The expiry check is short-circuited by `userInfo.expiresAt &&`, so a stored record with no `expiresAt` field must be returned as-is regardless of current time. Add a test that seeds `sessionStorage` with a `StoredUserInfo` blob that omits `expiresAt` entirely, calls `getUserInfo()`, and asserts the record is returned unchanged (not cleared).

**Acceptance criteria:**
- Test fails if the `&&` short-circuit on line 48 is removed or altered such that an absent `expiresAt` is treated as expired.

### FR-4: `updateUserInfo()` is a silent no-op when no session exists
Add a test that ensures `sessionStorage` has no entry under `USER_INFO_KEY` (either by not seeding it, or by explicitly clearing it first) and no entry under `LAST_LOGIN_KEY`. Call `UserStorage.updateUserInfo({ someField: "value" })` (using a valid partial of whatever fields `UserInfo` defines) and assert:
- `sessionStorage.getItem(USER_INFO_KEY)` is still `null` after the call — nothing was created.
- No exception is thrown.

**Acceptance criteria:**
- Test fails if a future change makes `updateUserInfo()` create a new session record when none existed (a behavior change that should be caught, since the brief explicitly calls out that current behavior is "silently do nothing" and callers relying on create-on-update would be surprised).

### FR-5: `updateUserInfo()` merges updates into an existing session without touching `lastLogin`
(Companion positive-path test, needed to characterize the branch alongside FR-4.) Seed `sessionStorage` with a valid, non-expired `StoredUserInfo` blob. Call `UserStorage.updateUserInfo({ ...some updated field... })` and assert:
- The stored record now reflects the updated field(s).
- `lastLogin` and `expiresAt` on the stored record are unchanged from the original (per the method's doc comment "Update user info without changing login timestamp").

**Acceptance criteria:**
- Test fails if `updateUserInfo()` overwrites `lastLogin` or `expiresAt` unexpectedly, or fails to persist the update via `sessionStorage.setItem`.

### FR-6: `getLastLogin()` returns `null` when `LAST_LOGIN_KEY` is absent
Add a test that ensures `sessionStorage` has no entry under `LAST_LOGIN_KEY`, calls `UserStorage.getLastLogin()`, and asserts the return value is `null`.

**Acceptance criteria:**
- Test fails if a future change causes this path to throw or return something other than `null` (e.g., `new Date(null)` / `new Date(undefined)` producing an Invalid Date object instead of `null`).

### FR-7 (supporting, not in brief but needed for isolation): Test setup/teardown clears `sessionStorage`
Each test must start from a clean `sessionStorage` (no leftover keys from a previous test) since `UserStorage` is a static class with module-level constants for keys. Use a `beforeEach`/`afterEach` (or equivalent) that calls `sessionStorage.clear()`.

**Acceptance criteria:**
- Running the full new test file in isolation and interleaved with other suites produces identical results (no cross-test pollution of `sessionStorage`).

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — these are synchronous unit tests against an in-memory `sessionStorage` mock/shim (jsdom). Full suite should execute in well under 1 second.

### NFR-2: Security
No new security surface. Tests must not log or hardcode real credentials; use synthetic `UserInfo` fixture data (e.g., `{ name: "Test User", email: "test@example.com" }` or whatever minimal shape satisfies the `UserInfo` type from `./useAuth`).

### NFR-3: Determinism
Expiry tests must not be flaky due to real clock drift. Use relative offsets from `Date.now()` at test-execution time (as the brief suggests: ±1 second / ±1 hour) rather than fixed absolute timestamps, so the tests remain valid indefinitely. Do not fake/mock the system clock unless an existing project convention already does so elsewhere in the auth test suite — check for a shared test-utils pattern first.

## Data Model
No new data model. Existing types in scope:
- `UserInfo` (imported from `./useAuth`) — the base authenticated-user shape.
- `StoredUserInfo` (defined in `userStorage.ts`, line 6) — extends `UserInfo` with:
  - `lastLogin: string` — ISO timestamp string.
  - `expiresAt?: string` — optional ISO timestamp string; absence means no expiry is enforced.
- Storage keys (module-level constants, not exported): `USER_INFO_KEY = "anela_heblo_user_info"`, `LAST_LOGIN_KEY = "anela_heblo_last_login"`. Tests must reference these via the same literal strings (or, if the module is refactored to export them, via the export) since they are not currently exported — tests will need to interact with `sessionStorage` directly using the literal key strings to seed/inspect state, mirroring what the implementation does internally.

## API / Interface Design
No public API changes. Methods under test, all static on `UserStorage`:
- `setUserInfo(userInfo: UserInfo): void` — not the focus of this spec but may be reused as a seeding helper in place of manually constructing `sessionStorage` entries, where convenient, provided it doesn't obscure the specific `expiresAt` value under test (prefer direct `sessionStorage.setItem` with a hand-built `StoredUserInfo` for expiry tests so the exact `expiresAt` is explicit and readable).
- `getUserInfo(): StoredUserInfo | null` — primary subject of FR-1, FR-2, FR-3.
- `clearUserInfo(): void` — used as an assertion target (verifying it was invoked) and as a teardown helper.
- `hasValidUserInfo(): boolean` — thin wrapper over `getUserInfo()`; already implicitly exercised by the above but not explicitly required by the brief. Optional stretch test, not required for this spec's acceptance.
- `getLastLogin(): Date | null` — subject of FR-6.
- `updateUserInfo(updates: Partial<UserInfo>): void` — subject of FR-4, FR-5.

Test file location/naming: follow existing project convention for co-located tests — expected at `frontend/src/auth/userStorage.test.ts` (no existing test file was found in `frontend/src/auth/` at spec-writing time; confirm the project's standard test runner/config, e.g. Jest with jsdom environment, via `frontend/package.json` / `jest.config` before writing, since this repo does not yet have a sibling test file to pattern-match against for this specific module).

## Dependencies
- Test runner and DOM environment already configured for the `frontend` project (assumed Jest + jsdom, consistent with Create React App / standard React test setup — verify against `frontend/package.json`).
- No new libraries required. `sessionStorage` is available natively in jsdom's test environment.
- No backend, network, or other module dependencies.

## Out of Scope
- Any change to the production logic in `userStorage.ts`. If a test uncovers an actual bug (not expected, per source review — the expiry comparison, no-op update, and absent-key handling all appear correct as written), that is a separate fix to be raised, not silently patched here.
- Testing `setUserInfo()`'s own expiry-stamping logic (the 24-hour default) as a first-class requirement — it may be exercised incidentally as a seeding helper but is not one of the three gaps called out in the brief.
- Testing `hasValidUserInfo()` directly (thin wrapper, low risk, not flagged in the brief).
- Broader auth-flow integration tests (e.g., `useAuth` hook behavior, login/logout UI flows) — this spec is scoped strictly to the `UserStorage` static class.
- Raising the file's coverage threshold itself, or auditing other files' coverage — scope is limited to `userStorage.ts`.

## Open Questions
None.

## Status: COMPLETE
