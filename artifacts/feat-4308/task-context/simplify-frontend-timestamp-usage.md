### task: simplify-frontend-timestamp-usage

**Files:**
- Modify: `frontend/src/services/versionService.ts`
- Modify: `frontend/src/services/__tests__/versionService.test.ts`

**Prerequisite:** `task: regenerate-frontend-client` must be complete — this task fixes the compile error that task deliberately left in place.

- [ ] **Step 1: Update the failing test fixture first**

In `frontend/src/services/__tests__/versionService.test.ts`, in `makeMockApiClient()`, change:

```typescript
function makeMockApiClient(version: string) {
  return {
    configuration_GetConfiguration: jest.fn().mockResolvedValue({
      version,
      environment: 'test',
      useMockAuth: false,
      timestamp: new Date('2024-01-01T00:00:00Z'),
    }),
  };
}
```

to:

```typescript
function makeMockApiClient(version: string) {
  return {
    configuration_GetConfiguration: jest.fn().mockResolvedValue({
      version,
      environment: 'test',
      useMockAuth: false,
    }),
  };
}
```

- [ ] **Step 2: Run the frontend test suite to confirm no test asserts on the literal mocked timestamp value**

Run: `cd frontend && npx jest src/services/__tests__/versionService.test.ts`
Expected: PASS — if any test fails here asserting `timestamp` equals `'2024-01-01T00:00:00Z'` or similar, that assertion must be loosened to check the value is a valid ISO-8601 string (e.g. `expect(() => new Date(result.timestamp).toISOString()).not.toThrow()` or `expect(result.timestamp).toEqual(expect.any(String))`), since `checkVersion()` will populate it from the live clock after Step 3 below. Re-run after loosening until green.

- [ ] **Step 3: Simplify the `checkVersion()` call site**

In `frontend/src/services/versionService.ts`, change:

```typescript
      return {
        version: response.version || "0.0.0",
        environment: response.environment || "unknown",
        useMockAuth: response.useMockAuth || false,
        timestamp:
          response.timestamp?.toISOString() || new Date().toISOString(),
      };
```

to:

```typescript
      return {
        version: response.version || "0.0.0",
        environment: response.environment || "unknown",
        useMockAuth: response.useMockAuth || false,
        timestamp: new Date().toISOString(),
      };
```

Do not change the `VersionInfo` type declaration (line ~7) — `timestamp: string` stays as-is; only its source expression here changes.

- [ ] **Step 4: Type-check and run the full frontend build**

Run: `cd frontend && npx tsc --noEmit`
Expected: PASS, 0 errors (this resolves the deliberate failure left by the previous task's Step 3).

Run: `cd frontend && npm run build`
Expected: Build succeeds.

- [ ] **Step 5: Run the frontend test suite and lint**

Run: `cd frontend && npx jest src/services/__tests__/versionService.test.ts`
Expected: PASS (all tests green).

Run: `cd frontend && npm run lint`
Expected: No new lint errors introduced by this change.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/services/versionService.ts frontend/src/services/__tests__/versionService.test.ts
git commit -m "fix: source versionService timestamp from local clock only"
```

---

## Self-Review

**Spec coverage:**
- FR-1 (remove `Timestamp` from DTO) → `task: remove-backend-timestamp-field`, Step 4.
- FR-2 (remove assignment from handler) → `task: remove-backend-timestamp-field`, Step 5.
- FR-3 (simplify frontend consumer) → `task: simplify-frontend-timestamp-usage`, Step 3.
- FR-4 (regenerate OpenAPI client) → `task: regenerate-frontend-client`, Steps 1–2.
- Arch-review FR-5 (update backend integration test) → `task: remove-backend-timestamp-field`, Step 2.
- Arch-review FR-6 (clean up frontend test fixture) → `task: simplify-frontend-timestamp-usage`, Step 1.
- NFR-1 (no behavior change) → verified structurally: the frontend's effective value is `new Date().toISOString()` in both the pre- and post-change code paths (it was already the fallback), and Step 2 of the last task requires loosening any test that pinned a literal mock timestamp, so no test encodes a false expectation of backend-sourced time.

**Placeholder scan:** No "TBD"/"TODO"/"handle appropriately" language; every step shows exact before/after code and exact commands with expected output.

**Type consistency:** `GetConfigurationResponse` (backend class and generated frontend class), `VersionInfo`, `checkVersion()`, and `makeMockApiClient()` are referenced with matching names and shapes across all three tasks.
