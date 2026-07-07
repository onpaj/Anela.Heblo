### task: userstorage-expiry-tests

**Context for the engineer:** You have zero prior context on this codebase. Everything you need is below.

**Source file under test** (do not modify): `/home/user/worktrees/feature-3504-Coverage-Gap-Auth-Userstorage-Session-Expiry-Check/frontend/src/auth/userStorage.ts`

Relevant excerpts already confirmed by reading the file:
- Line 3-4: `const USER_INFO_KEY = "anela_heblo_user_info";` and `const LAST_LOGIN_KEY = "anela_heblo_last_login";` — module-private, not exported. Tests must use these literal strings directly.
- Line 6-9: `export interface StoredUserInfo extends UserInfo { lastLogin: string; expiresAt?: string; }` — this IS exported, so tests can import and use it for typing.
- Line 48: `if (userInfo.expiresAt && new Date() > new Date(userInfo.expiresAt)) { ... this.clearUserInfo(); return null; }` — the expiry check under test in FR-1/2/3.
- Line 64-72: `clearUserInfo()` removes both `USER_INFO_KEY` and `LAST_LOGIN_KEY` from `sessionStorage`.
- Line 85-93: `getLastLogin()` — `sessionStorage.getItem(LAST_LOGIN_KEY)`; returns `null` if absent, else `new Date(lastLogin)`.
- Line 98-107: `updateUserInfo(updates)` — calls `this.getUserInfo()`; if `null` (no session), does nothing; if present, merges `updates` into the current record and calls `sessionStorage.setItem(USER_INFO_KEY, JSON.stringify(updated))`. Does not touch `LAST_LOGIN_KEY` or the `lastLogin`/`expiresAt` fields on the merged record (they're carried over from `current` via spread, unless `updates` explicitly overrides them).

`UserInfo` type (from `/home/user/worktrees/feature-3504-Coverage-Gap-Auth-Userstorage-Session-Expiry-Check/frontend/src/auth/useAuth.ts` lines 8-13):
```ts
export interface UserInfo {
  name: string;
  email: string;
  initials: string;
  roles?: string[];
}
```

**Pattern reference** (existing file, do not modify): `/home/user/worktrees/feature-3504-Coverage-Gap-Auth-Userstorage-Session-Expiry-Check/frontend/src/auth/__tests__/authRecovery.test.ts` — shows the project convention: `__tests__/` subdirectory, direct `sessionStorage.setItem`/`.getItem`/`.clear()` calls, a local `const RECOVERY_KEY = "..."` for the storage key literal, `beforeEach(() => sessionStorage.clear())`.

**File to create:** `/home/user/worktrees/feature-3504-Coverage-Gap-Auth-Userstorage-Session-Expiry-Check/frontend/src/auth/__tests__/userStorage.test.ts`

Do NOT create `frontend/src/auth/userStorage.test.ts` (co-located) — the architecture review confirmed the only convention used anywhere in `frontend/src/auth/` is the `__tests__/` subdirectory.

**Test command:** run from the `frontend/` directory:
```
CI=true npx react-scripts test src/auth/__tests__/userStorage.test.ts --watchAll=false
```
(`CI=true` makes Jest run once and exit instead of entering watch mode.)

---

#### Step 1: Create the test file skeleton with fixtures and the first expiry test (FR-1)

- [ ] Create `/home/user/worktrees/feature-3504-Coverage-Gap-Auth-Userstorage-Session-Expiry-Check/frontend/src/auth/__tests__/userStorage.test.ts` with this content:

```ts
import { UserStorage, StoredUserInfo } from "../userStorage";
import { UserInfo } from "../useAuth";

const USER_INFO_KEY = "anela_heblo_user_info";
const LAST_LOGIN_KEY = "anela_heblo_last_login";

const baseUserInfo: UserInfo = {
  name: "Test User",
  email: "test@example.com",
  initials: "TU",
};

describe("UserStorage", () => {
  beforeEach(() => {
    sessionStorage.clear();
  });

  describe("getUserInfo", () => {
    it("returns null and clears storage when expiresAt is in the past", () => {
      const stored: StoredUserInfo = {
        ...baseUserInfo,
        lastLogin: new Date().toISOString(),
        expiresAt: new Date(Date.now() - 1000).toISOString(),
      };
      sessionStorage.setItem(USER_INFO_KEY, JSON.stringify(stored));
      sessionStorage.setItem(LAST_LOGIN_KEY, stored.lastLogin);

      const result = UserStorage.getUserInfo();

      expect(result).toBeNull();
      expect(sessionStorage.getItem(USER_INFO_KEY)).toBeNull();
      expect(sessionStorage.getItem(LAST_LOGIN_KEY)).toBeNull();
    });
  });
});
```

- [ ] Run the test command above. Confirm the single test passes (this pins the currently-correct behavior; there is no red phase here because the production code is not being written in this task — it already exists and is correct per the spec).

- [ ] Verify the test actually detects a regression (this satisfies FR-1's acceptance criterion "Test fails if the `>` comparison on line 48 is inverted to `<`"): temporarily edit `/home/user/worktrees/feature-3504-Coverage-Gap-Auth-Userstorage-Session-Expiry-Check/frontend/src/auth/userStorage.ts` line 48, changing `new Date() > new Date(userInfo.expiresAt)` to `new Date() < new Date(userInfo.expiresAt)`. Re-run the test command. Confirm the test now FAILS. Then revert the edit (change `<` back to `>`) and re-run to confirm the test passes again. Do not leave the production file modified.

- [ ] Commit: `git add frontend/src/auth/__tests__/userStorage.test.ts && git commit -m "test: add UserStorage expired-session coverage (FR-1)"`

#### Step 2: Add the future-expiry and missing-expiresAt tests (FR-2, FR-3)

- [ ] Add two more `it(...)` blocks inside the existing `describe("getUserInfo", ...)` block, after the FR-1 test:

```ts
    it("returns stored info unchanged when expiresAt is in the future", () => {
      const stored: StoredUserInfo = {
        ...baseUserInfo,
        lastLogin: new Date().toISOString(),
        expiresAt: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
      };
      sessionStorage.setItem(USER_INFO_KEY, JSON.stringify(stored));

      const result = UserStorage.getUserInfo();

      expect(result).toEqual(stored);
      expect(sessionStorage.getItem(USER_INFO_KEY)).not.toBeNull();
    });

    it("treats a missing expiresAt as never-expiring", () => {
      const stored: StoredUserInfo = {
        ...baseUserInfo,
        lastLogin: new Date().toISOString(),
      };
      sessionStorage.setItem(USER_INFO_KEY, JSON.stringify(stored));

      const result = UserStorage.getUserInfo();

      expect(result).toEqual(stored);
      expect(sessionStorage.getItem(USER_INFO_KEY)).not.toBeNull();
    });
```

- [ ] Run the test command. Confirm all 3 tests in `getUserInfo` pass.

- [ ] Verify the future-expiry test detects a regression (FR-2's acceptance criterion): repeat the same temporary line-48 flip (`>` → `<`) used in Step 1, re-run, confirm the "returns stored info unchanged when expiresAt is in the future" test now FAILS (the future-dated session gets incorrectly treated as expired). Revert the flip, re-run, confirm all 3 tests pass again.

- [ ] Commit: `git add frontend/src/auth/__tests__/userStorage.test.ts && git commit -m "test: add UserStorage valid-session and no-expiry coverage (FR-2, FR-3)"`

#### Step 3: Add the updateUserInfo no-op and merge tests (FR-4, FR-5)

- [ ] Add a new `describe("updateUserInfo", ...)` block after the `getUserInfo` block closes (still inside the outer `describe("UserStorage", ...)`):

```ts
  describe("updateUserInfo", () => {
    it("is a silent no-op when no session exists", () => {
      expect(sessionStorage.getItem(USER_INFO_KEY)).toBeNull();

      expect(() =>
        UserStorage.updateUserInfo({ name: "New Name" }),
      ).not.toThrow();

      expect(sessionStorage.getItem(USER_INFO_KEY)).toBeNull();
    });

    it("merges updates into an existing session without touching lastLogin or expiresAt", () => {
      const originalLastLogin = new Date(Date.now() - 60 * 1000).toISOString();
      const originalExpiresAt = new Date(
        Date.now() + 60 * 60 * 1000,
      ).toISOString();
      const stored: StoredUserInfo = {
        ...baseUserInfo,
        lastLogin: originalLastLogin,
        expiresAt: originalExpiresAt,
      };
      sessionStorage.setItem(USER_INFO_KEY, JSON.stringify(stored));

      UserStorage.updateUserInfo({ name: "Updated Name" });

      const raw = sessionStorage.getItem(USER_INFO_KEY);
      expect(raw).not.toBeNull();
      const updated: StoredUserInfo = JSON.parse(raw as string);
      expect(updated.name).toBe("Updated Name");
      expect(updated.email).toBe(baseUserInfo.email);
      expect(updated.lastLogin).toBe(originalLastLogin);
      expect(updated.expiresAt).toBe(originalExpiresAt);
    });
  });
```

- [ ] Run the test command. Confirm both new tests pass, and the 3 `getUserInfo` tests from Steps 1-2 still pass (5 total so far).

- [ ] Commit: `git add frontend/src/auth/__tests__/userStorage.test.ts && git commit -m "test: add UserStorage updateUserInfo no-op and merge coverage (FR-4, FR-5)"`

#### Step 4: Add the getLastLogin absent-key test (FR-6)

- [ ] Add a new `describe("getLastLogin", ...)` block after the `updateUserInfo` block closes (still inside the outer `describe("UserStorage", ...)`):

```ts
  describe("getLastLogin", () => {
    it("returns null when LAST_LOGIN_KEY is absent", () => {
      expect(sessionStorage.getItem(LAST_LOGIN_KEY)).toBeNull();

      const result = UserStorage.getLastLogin();

      expect(result).toBeNull();
    });
  });
```

- [ ] Run the test command. Confirm all 6 tests pass.

- [ ] Commit: `git add frontend/src/auth/__tests__/userStorage.test.ts && git commit -m "test: add UserStorage getLastLogin absent-key coverage (FR-6)"`

#### Step 5: Confirm isolation (FR-7) and check coverage impact

- [ ] Run the full auth test directory together to confirm no cross-test `sessionStorage` pollution between the new file and existing sibling tests:
```
CI=true npx react-scripts test src/auth/__tests__ --watchAll=false
```
Confirm all tests across `authRecovery.test.ts`, `useAuth.test.ts`, `accessMatrixConsistency.test.ts`, and the new `userStorage.test.ts` pass together, in any run order.

- [ ] Run the full frontend test suite with coverage to confirm `userStorage.ts` now clears the 60% line-coverage threshold:
```
CI=true npx react-scripts test --coverage --watchAll=false --collectCoverageFrom="src/auth/userStorage.ts"
```
Confirm the reported line coverage for `frontend/src/auth/userStorage.ts` is at or above 60%. (If it is still below 60%, that is a pre-existing gap in an out-of-scope code path per the spec's Out of Scope section — do not add further tests to force it up; report the actual number as a note rather than silently expanding scope.)

- [ ] No further commit needed for this step unless the coverage run surfaces something worth noting — if so, add a one-line comment at the top of the test file only if it clarifies scope (optional, not required).

**Acceptance criteria mapping (self-review):**
- FR-1 → Step 1 test + line-48 mutation verification.
- FR-2 → Step 2 first test + line-48 mutation verification.
- FR-3 → Step 2 second test.
- FR-4 → Step 3 first test.
- FR-5 → Step 3 second test.
- FR-6 → Step 4 test.
- FR-7 → `beforeEach(() => sessionStorage.clear())` present from Step 1 onward; explicitly re-verified in Step 5.
- NFR-1 (performance) → all tests synchronous, no network/timers; satisfied by construction.
- NFR-2 (security) → fixture uses synthetic `baseUserInfo` data only, no real credentials.
- NFR-3 (determinism) → all timestamps are relative offsets from `Date.now()` at test-execution time; no fake timers introduced, consistent with the rest of `frontend/src/auth/__tests__/`.
