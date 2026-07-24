## Goal (from the overall plan)

Retype the four date filter fields on `GetBankStatementListRequest` (and the matching `BankStatementsController` query parameters) from `string?` to `DateTime?`, deleting the now-redundant `DateTime.TryParse` logic duplicated across the handler and validator, and adapting the frontend client/hook to match.

This task is task 5 of 5, the last task. Tasks 1–4 are already done on this branch: backend DTO/controller/handler/validator are retyped and simplified, and the full backend test suite passes. This task regenerates the OpenAPI TypeScript client (which will now type the four date params as `Date | null | undefined` instead of `string | null | undefined`) and adapts the one frontend caller.

Reference: `frontend/src/api/hooks/useBankStatements.ts` — `useBankStatementImport` in that same file already does `new Date(request.dateFrom)`; this is the pattern to mirror.

Sole caller of `bankStatements_GetBankStatements(` in the frontend (confirmed via grep during planning): `frontend/src/api/hooks/useBankStatements.ts` (plus the generated file itself). No other consumer is expected to be affected.

Regeneration command (per `docs/development/api-client-generation.md`):
```bash
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
```

---

### task: regenerate-client-and-update-frontend-hook

**Files:** `frontend/src/api/generated/api-client.ts` (regenerated only), `frontend/src/api/hooks/useBankStatements.ts`, `frontend/src/api/hooks/__tests__/useBankStatements.test.ts`

**Step 1 — regenerate the TypeScript client.**

```bash
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
```

This requires the `API` project to build successfully, which it now does (confirmed in the `simplify-validator-date-rules` task). Do not hand-edit `frontend/src/api/generated/api-client.ts`.

**Step 2 — verify the regenerated signature.**

```bash
grep -n "bankStatements_GetBankStatements(" frontend/src/api/generated/api-client.ts
```

Expected: the method signature's `statementDate`, `importDate`, `dateFrom`, `dateTo` parameters now read `Date | null | undefined` (previously `string | null | undefined`); `id`, `transferId`, `account`, `errorsOnly`, `skip`, `take`, `orderBy`, `ascending` parameter types are unchanged.

**Step 3 — confirm this is still the sole caller.**

```bash
grep -rn "bankStatements_GetBankStatements(" frontend/src --include=*.ts --include=*.tsx
```

Expected: two matches — the generated method definition itself (`frontend/src/api/generated/api-client.ts`) and the call site in `frontend/src/api/hooks/useBankStatements.ts`. If any other caller appears, stop and flag it rather than silently expanding scope.

**Step 4 — confirm the frontend currently fails to build.**

```bash
cd frontend && npm run build
```

Expected: TypeScript compile error in `useBankStatements.ts`, because `useBankStatementsList` passes `request?.dateFrom` (type `string | undefined`) where the regenerated client now expects `Date | null | undefined`.

**Step 5 — update `useBankStatementsList`.**

Open `frontend/src/api/hooks/useBankStatements.ts`. Find:

```typescript
export const useBankStatementsList = (
  request: GetBankStatementListRequest = {}
) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.bankStatements, 'list', request],
    queryFn: (): Promise<GetBankStatementListResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.bankStatements_GetBankStatements(
        request?.id ?? undefined,
        request?.transferId?.trim() ?? undefined,
        request?.account?.trim() ?? undefined,
        request?.statementDate ?? undefined,
        request?.importDate ?? undefined,
        request?.dateFrom ?? undefined,
        request?.dateTo ?? undefined,
        request?.errorsOnly ?? undefined,
        request?.skip,
        request?.take,
        request?.orderBy ?? undefined,
        request?.ascending
      );
    },
    staleTime: 2 * 60 * 1000, // 2 minutes
  });
};
```

Replace with (only the four date arguments to `bankStatements_GetBankStatements` change; the `GetBankStatementListRequest` TypeScript interface a few lines above this function — with its `statementDate?: string; importDate?: string; dateFrom?: string; dateTo?: string;` fields — is **not** touched, preserving the hook's public string-based contract to `ImportTab.tsx`):

```typescript
export const useBankStatementsList = (
  request: GetBankStatementListRequest = {}
) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.bankStatements, 'list', request],
    queryFn: (): Promise<GetBankStatementListResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.bankStatements_GetBankStatements(
        request?.id ?? undefined,
        request?.transferId?.trim() ?? undefined,
        request?.account?.trim() ?? undefined,
        request?.statementDate ? new Date(request.statementDate) : undefined,
        request?.importDate ? new Date(request.importDate) : undefined,
        request?.dateFrom ? new Date(request.dateFrom) : undefined,
        request?.dateTo ? new Date(request.dateTo) : undefined,
        request?.errorsOnly ?? undefined,
        request?.skip,
        request?.take,
        request?.orderBy ?? undefined,
        request?.ascending
      );
    },
    staleTime: 2 * 60 * 1000, // 2 minutes
  });
};
```

**Step 6 — add hook-level test coverage for the new conversion.**

`frontend/src/api/hooks/__tests__/useBankStatements.test.ts` currently only covers `useBankStatementAccounts`. Add coverage for the new `Date` conversion behavior in `useBankStatementsList`, using the same `mockAuthenticatedApiClient`/`createQueryClientWrapper` pattern already in the file. Add the import and a new `describe` block.

Find the import line:

```typescript
import { useBankStatementAccounts } from '../useBankStatements';
```

Replace with:

```typescript
import { useBankStatementAccounts, useBankStatementsList } from '../useBankStatements';
```

Add a new `describe` block after the closing `});` of the existing `describe('useBankStatements - Account Listing', ...)` block (i.e., at the end of the file, as a sibling top-level `describe`):

```typescript
describe('useBankStatements - List Query', () => {
    let mockClient: {
        bankStatements_GetAccounts: jest.Mock;
        bankStatements_GetBankStatements: jest.Mock;
        bankStatements_ImportStatements: jest.Mock;
    };

    beforeEach(() => {
        jest.clearAllMocks();
        mockClient = {
            bankStatements_GetAccounts: jest.fn(),
            bankStatements_GetBankStatements: jest.fn(),
            bankStatements_ImportStatements: jest.fn(),
        };
        mockAuthenticatedApiClient(mockClient);
    });

    it('converts dateFrom/dateTo strings to Date objects before calling the generated client', async () => {
        mockClient.bankStatements_GetBankStatements.mockResolvedValue({ items: [], totalCount: 0 });

        const { wrapper } = createQueryClientWrapper();
        const { result } = renderHook(
            () => useBankStatementsList({ dateFrom: '2026-01-01', dateTo: '2026-01-31' }),
            { wrapper }
        );

        await waitFor(() => expect(result.current.isSuccess).toBe(true));

        expect(mockClient.bankStatements_GetBankStatements).toHaveBeenCalledTimes(1);
        const call = mockClient.bankStatements_GetBankStatements.mock.calls[0];
        expect(call[5]).toEqual(new Date('2026-01-01'));
        expect(call[6]).toEqual(new Date('2026-01-31'));
    });

    it('passes undefined for dateFrom/dateTo/statementDate/importDate when absent', async () => {
        mockClient.bankStatements_GetBankStatements.mockResolvedValue({ items: [], totalCount: 0 });

        const { wrapper } = createQueryClientWrapper();
        const { result } = renderHook(() => useBankStatementsList({}), { wrapper });

        await waitFor(() => expect(result.current.isSuccess).toBe(true));

        const call = mockClient.bankStatements_GetBankStatements.mock.calls[0];
        expect(call[3]).toBeUndefined(); // statementDate
        expect(call[4]).toBeUndefined(); // importDate
        expect(call[5]).toBeUndefined(); // dateFrom
        expect(call[6]).toBeUndefined(); // dateTo
    });
});
```

Argument indices `call[3]`..`call[6]` correspond to the `bankStatements_GetBankStatements` positional parameters `statementDate, importDate, dateFrom, dateTo` (indices 0–2 are `id, transferId, account`), matching the call site written in Step 5.

**Step 7 — run the new frontend tests.**

```bash
cd frontend && npx react-scripts test src/api/hooks/__tests__/useBankStatements.test.ts --watchAll=false
```

Expected: all tests pass, including the two new ones in `useBankStatements - List Query`.

**Step 8 — full frontend verification.**

```bash
cd frontend && npm run build && npm run lint
```

Expected: both succeed with no new TypeScript or lint errors. `frontend/src/components/customer/tabs/ImportTab.tsx` requires no changes (its calls into `useBankStatementsList` still pass strings, matching the unchanged `GetBankStatementListRequest` TS interface).

**Step 9 — commit.**

```bash
git add frontend/src/api/generated/api-client.ts frontend/src/api/hooks/useBankStatements.ts frontend/src/api/hooks/__tests__/useBankStatements.test.ts
git commit -m "Regenerate OpenAPI client and convert useBankStatementsList date strings to Date objects"
```

## Final verification (run once, after this last task is done — the whole feature should now be complete)

```bash
dotnet build
dotnet format --verify-no-changes
dotnet test
cd frontend && npm run build && npm run lint
```

All four commands must succeed with no errors before considering this change complete. No E2E changes are required (no existing E2E spec targets `/api/bank-statements`, per spec Out of Scope).
