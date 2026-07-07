# Architecture Review: UserStorage Test Coverage (Session Expiry, No-Session Update, Missing Last-Login)

## Skip Design: true

This is a test-only addition to `frontend/src/auth/userStorage.ts`. No production code, no UI components, no visual surface changes. There is nothing for a UI/UX design pass to review.

## Architectural Fit Assessment

This is a narrow, low-risk, test-only change and it fits the existing test architecture cleanly — with one correction to the spec's assumed file location (see Amendments).

- `UserStorage` is a self-contained static utility with a single external dependency (`sessionStorage`, native in jsdom) and one internal type import (`UserInfo` from `./useAuth`). It has no React component surface, no MSAL/network dependency, and no side effects beyond `sessionStorage` and `console.log`/`console.warn`. This makes it one of the easiest modules in the codebase to unit test in isolation — no mocking framework, no fake timers, no DOM rendering.
- The project already has an established convention for testing sibling modules in `frontend/src/auth/`: `frontend/src/auth/__tests__/authRecovery.test.ts` (and `useAuth.test.ts`, `accessMatrixConsistency.test.ts`) live under a `__tests__/` subdirectory, not as co-located `*.test.ts` files next to the module. `authRecovery.test.ts` interacts with `sessionStorage` directly (`sessionStorage.setItem`, `.getItem`, `.clear()`) with no mock/shim library — jsdom's native `sessionStorage` is used as-is. This is the pattern to replicate.
- Test runner is CRA's `react-scripts test` (Jest + jsdom under the hood), confirmed via `frontend/package.json`. No custom jest config beyond `transformIgnorePatterns` for `date-fns`. No fake-timer convention exists elsewhere in `auth/__tests__/`, consistent with the spec's NFR-3 preference for relative `Date.now()` offsets over clock mocking.
- No architectural risk: this change cannot affect runtime behavior (assuming the spec's out-of-scope constraint — no production code changes — is honored), and it isolates cleanly to one file addition.

## Proposed Architecture

### Component Overview

```
frontend/src/auth/
├── userStorage.ts                     (unchanged — subject under test)
├── useAuth.ts                         (unchanged — source of UserInfo type)
└── __tests__/
    ├── authRecovery.test.ts           (existing — pattern reference)
    ├── useAuth.test.ts                (existing)
    ├── accessMatrixConsistency.test.ts (existing)
    └── userStorage.test.ts            (NEW — this feature)
```

No new components, modules, or runtime dependencies. The only artifact is one new test file exercising the existing `UserStorage` static class through its public API (`setUserInfo`, `getUserInfo`, `clearUserInfo`, `getLastLogin`, `updateUserInfo`) plus direct `sessionStorage` seeding for the two key literals.

### Key Design Decisions

#### Decision 1: Test file location
**Options considered:**
- (a) Co-located sibling file `frontend/src/auth/userStorage.test.ts`, as the spec proposes (spec explicitly flags this as unconfirmed: "no existing test file was found... confirm the project's standard... before writing").
- (b) `frontend/src/auth/__tests__/userStorage.test.ts`, matching the three existing test files in this exact directory.

**Chosen approach:** (b) — `frontend/src/auth/__tests__/userStorage.test.ts`.

**Rationale:** Every existing test in `frontend/src/auth/` (and the large majority of the ~65 test files across `frontend/src/`) uses the `__tests__/` subdirectory convention, not co-location. There is no precedent anywhere in `frontend/src/auth/` for a sibling `*.test.ts` file. Deviating here would be inconsistent with zero benefit. This is a direct correction to the spec, not a judgment call — treat it as settled, not open.

#### Decision 2: sessionStorage access — real jsdom storage vs. mock/shim
**Options considered:**
- (a) Mock `sessionStorage` with a custom in-memory shim or `jest.spyOn`.
- (b) Use jsdom's native `sessionStorage` directly, matching `authRecovery.test.ts`.

**Chosen approach:** (b).

**Rationale:** `authRecovery.test.ts` already establishes this pattern in the same directory (`sessionStorage.setItem(RECOVERY_KEY, ...)`, `sessionStorage.clear()` in `beforeEach`). jsdom provides a real, spec-compliant `Storage` implementation, so there is no reason to introduce a mock layer — it would only add indirection and risk diverging from real browser behavior (e.g., `JSON.parse`/`stringify` round-tripping, `getItem` returning `string | null`).

#### Decision 3: Key literals vs. exporting `USER_INFO_KEY`/`LAST_LOGIN_KEY`
**Options considered:**
- (a) Export the two key constants from `userStorage.ts` so tests reference them symbolically.
- (b) Duplicate the literal strings (`"anela_heblo_user_info"`, `"anela_heblo_last_login"`) directly in the test file, as the spec directs.

**Chosen approach:** (b), per spec — no production code changes.

**Rationale:** The spec explicitly scopes this as test-only; exporting the constants would be a (harmless but) unrequested production change and is out of scope per the brief ("No changes to `userStorage.ts` itself are anticipated"). Duplicating the two literal strings is a minor, acceptable coupling — if they ever drift, the tests fail loudly (seeded key never read back), which is itself a useful signal. Define them as local `const` at the top of the test file (mirroring `authRecovery.test.ts`'s `const RECOVERY_KEY = "auth.recovery";`) rather than inline in every test.

#### Decision 4: Clock handling for expiry tests
**Options considered:**
- (a) `jest.useFakeTimers()` / mock `Date.now()`.
- (b) Relative offsets from the real `Date.now()` at test-execution time (`Date.now() - 1000`, `Date.now() + 60*60*1000`).

**Chosen approach:** (b), per spec NFR-3.

**Rationale:** No test in `frontend/src/auth/__tests__/` currently fakes the clock. Introducing fake timers here would be new machinery for a single test file solving a problem (flakiness) that relative real-time offsets already solve at the second/hour scale used here. Keep it simple and consistent with the rest of the auth test suite.

## Implementation Guidance

### Directory / Module Structure
Create exactly one new file:
- `frontend/src/auth/__tests__/userStorage.test.ts`

No changes to `frontend/src/auth/userStorage.ts` or any other production file.

### Interfaces and Contracts
No new interfaces. Tests exercise the existing public static API of `UserStorage` (`frontend/src/auth/userStorage.ts`):
- `setUserInfo(userInfo: UserInfo): void` — optional seeding helper (per spec, prefer direct `sessionStorage.setItem` with a hand-built `StoredUserInfo` for expiry tests so `expiresAt` is explicit).
- `getUserInfo(): StoredUserInfo | null` — FR-1, FR-2, FR-3.
- `clearUserInfo(): void` — assertion target / teardown helper.
- `getLastLogin(): Date | null` — FR-6.
- `updateUserInfo(updates: Partial<UserInfo>): void` — FR-4, FR-5.

Local test-file constants (mirroring the production module, since they aren't exported):
```ts
const USER_INFO_KEY = "anela_heblo_user_info";
const LAST_LOGIN_KEY = "anela_heblo_last_login";
```

Minimal `UserInfo` fixture (matches the interface in `frontend/src/auth/useAuth.ts:8-13` — all fields optional except `name`/`email`/`initials`):
```ts
const baseUserInfo: UserInfo = {
  name: "Test User",
  email: "test@example.com",
  initials: "TU",
};
```

### Data Flow
Standard AAA (arrange/act/assert) unit test flow, no async/await needed (all `UserStorage` methods are synchronous):
1. **Arrange** — `beforeEach` calls `sessionStorage.clear()` (and `localStorage.clear()` if any shared setup does, though `UserStorage` never touches `localStorage`). Seed `sessionStorage` via `sessionStorage.setItem(USER_INFO_KEY, JSON.stringify(storedUserInfo))` for expiry/update tests, or leave unseeded for the absent-key tests (FR-4, FR-6).
2. **Act** — call the `UserStorage` static method under test directly (no rendering, no React Testing Library needed).
3. **Assert** — check the method's return value and/or read back `sessionStorage.getItem(...)` to confirm persisted/cleared state.

No component tree, no MSAL mocking, no `jest.mock(...)` calls are needed for this file — unlike `authRecovery.test.ts`, `userStorage.ts` has zero external module dependencies to mock (it only imports the `UserInfo` type, which is erased at compile time).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Test file placed at `frontend/src/auth/userStorage.test.ts` (co-located) instead of `__tests__/`, breaking directory convention | Low | Explicit Directory Structure guidance above; enforced in code review — only one correct path |
| `console.log`/`console.warn` calls in `userStorage.ts` produce noisy test output | Low | Acceptable as-is; do not suppress via mocking `console` unless it already causes CI log-noise complaints elsewhere (no evidence of that convention in `authRecovery.test.ts`) |
| Cross-test `sessionStorage` pollution (static class, shared browser storage) | Medium | `beforeEach(() => sessionStorage.clear())` per FR-7, already the established pattern in `authRecovery.test.ts` |
| A test inadvertently encodes an inverted/incorrect expectation, defeating the purpose (pinning wrong behavior as "correct") | Medium | Explicitly assert both directions per FR-1/FR-2 (past → null + cleared; future → returned unchanged) so a single inverted comparison in production code fails at least one test, not none |
| Coverage still short of 60% after these tests (untested paths not called out in brief, e.g. `setUserInfo`'s try/catch error branches) | Low | Out of scope per spec; if the 60% gate still fails after landing these tests, that's a follow-up coverage-gap ticket, not a blocker for this spec |

## Specification Amendments

1. **Test file path (correction, not open question):** The spec lists the file location as unconfirmed and suggests `frontend/src/auth/userStorage.test.ts`. Based on direct inspection of `frontend/src/auth/__tests__/` (three existing files: `authRecovery.test.ts`, `useAuth.test.ts`, `accessMatrixConsistency.test.ts`), the established and only-used convention in this directory is `frontend/src/auth/__tests__/userStorage.test.ts`. Use this path; do not create a sibling file.
2. **sessionStorage mocking (confirmation):** Spec NFR-3 says "do not fake/mock the system clock unless an existing project convention already does so." Confirmed: no such convention exists in `frontend/src/auth/__tests__/`. Use real relative-offset timestamps as specified. Similarly, no `sessionStorage` shim/mock convention exists — use jsdom's native `sessionStorage` directly, matching `authRecovery.test.ts`.
3. No functional requirements need to change; FR-1 through FR-7 are all implementable as written against the actual `userStorage.ts` source (confirmed lines match: expiry check at line 48, `updateUserInfo` no-op at lines 98-107, `getLastLogin` absent-key path at lines 85-93).

## Prerequisites

None. The test runner (`react-scripts test`, Jest + jsdom), `sessionStorage` (native in jsdom), and the `UserInfo`/`StoredUserInfo` types all already exist and require no setup, migration, or configuration change. Implementation can start immediately by creating `frontend/src/auth/__tests__/userStorage.test.ts`.
