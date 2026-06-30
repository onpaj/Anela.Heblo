### task: update-tests-for-typed-client-methods

**Files:**
- Modify: `frontend/src/api/hooks/__tests__/useBankStatements.test.ts`

**Context:**

The current test file mocks the private `http.fetch` field via `createMockApiClient()` (which creates `{ baseUrl, http: { fetch: mockFetch } }`). After the hook is rewritten to use typed methods, the test must mock those methods on the real `ApiClient` instance returned by `getAuthenticatedApiClient`.

The `createMockApiClient` / `mockAuthenticatedApiClient` helpers in `testUtils.ts` set up `(getAuthenticatedApiClient as jest.Mock).mockReturnValue(mockClient)`. We keep using this pattern but replace the mock client structure with one that exposes the typed method names.

- [ ] Step 1: Replace the `beforeEach` setup in `useBankStatementAccounts` describe block. Remove the `mockFetch` variable and the raw mock client. Instead spy on the real typed method. Rewrite the `beforeEach` as:
  ```typescript
  beforeEach(() => {
    jest.clearAllMocks();
    mockClient = {
      bankStatements_GetAccounts: jest.fn(),
      bankStatements_GetBankStatements: jest.fn(),
      bankStatements_ImportStatements: jest.fn(),
    };
    (getAuthenticatedApiClient as jest.Mock).mockReturnValue(mockClient);
  });
  ```
  Remove the `let mockFetch: jest.Mock;` variable declaration since it is no longer needed.

- [ ] Step 2: Update each test that called `mockFetch.mockResolvedValue(...)` to call the appropriate method mock instead.

  For `useBankStatementAccounts` tests, replace every:
  ```typescript
  mockFetch.mockResolvedValue({
    ok: true,
    json: () => Promise.resolve([...accountData...])
  });
  ```
  with:
  ```typescript
  mockClient.bankStatements_GetAccounts.mockResolvedValue([...accountData...]);
  ```

  The raw `BankAccountDto`-shaped objects remain the same (the generated method already returns parsed objects). Example for the first test:
  ```typescript
  mockClient.bankStatements_GetAccounts.mockResolvedValue([
    { name: 'ComgateCZK', accountNumber: '2301495165/2010', provider: 'Comgate', currency: 'CZK' }
  ]);
  ```

- [ ] Step 3: Remove or rewrite the endpoint URL assertion test. The test `'should call /api/bank-statements/accounts endpoint'` currently asserts `mockFetch` was called with a URL containing `/api/bank-statements/accounts`. After the refactor the hook calls a typed method, not a URL — so the URL is no longer observable at this layer. Replace that test with a method-call assertion:
  ```typescript
  it('should call bankStatements_GetAccounts on the api client', async () => {
    mockClient.bankStatements_GetAccounts.mockResolvedValue([]);

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useBankStatementAccounts(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockClient.bankStatements_GetAccounts).toHaveBeenCalledTimes(1);
    expect(mockClient.bankStatements_GetAccounts).toHaveBeenCalledWith();
  });
  ```

- [ ] Step 4: Remove the `createMockApiClient` and `mockAuthenticatedApiClient` imports from `testUtils` if they are no longer used by this test file. Check the import line:
  ```typescript
  import { createMockApiClient, mockAuthenticatedApiClient, createQueryClientWrapper } from '../../testUtils';
  ```
  After the rewrite, `createMockApiClient` is replaced by an inline object literal, but `mockAuthenticatedApiClient` is still used (it sets `(getAuthenticatedApiClient as jest.Mock).mockReturnValue(mockClient)`). Keep `mockAuthenticatedApiClient` and `createQueryClientWrapper`. Remove `createMockApiClient` from the import.

  Call `mockAuthenticatedApiClient(mockClient)` in `beforeEach` after constructing the inline mock object:
  ```typescript
  beforeEach(() => {
    jest.clearAllMocks();
    mockClient = {
      bankStatements_GetAccounts: jest.fn(),
      bankStatements_GetBankStatements: jest.fn(),
      bankStatements_ImportStatements: jest.fn(),
    };
    mockAuthenticatedApiClient(mockClient);
  });
  ```

- [ ] Step 5: Run the tests to confirm they pass:
  ```
  cd frontend && npx jest src/api/hooks/__tests__/useBankStatements.test.ts --no-coverage
  ```
  Fix any failures. If a test fails because `result.current.isSuccess` is false, check that the mock method is configured in `beforeEach` (not missing from the mock object) and that the mock returns a resolved promise.

---

