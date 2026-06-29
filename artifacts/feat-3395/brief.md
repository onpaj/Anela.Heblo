## Module
Bank

## Finding
`frontend/src/api/hooks/useBankStatements.ts` manually defines three TypeScript interfaces that duplicate backend contract types (lines 13–43):

```ts
export interface BankStatementImportDto { ... }   // line 13
export interface GetBankStatementListResponse { ... }  // line 25
export interface GetBankStatementListRequest { ... }   // line 30
```

Additionally, `BankAccountDto` is defined again locally at line 179.

All three query hooks (`useBankStatementsList`, `useBankStatementImport`, `useBankStatementAccounts`) bypass the generated API client entirely and use:
```ts
const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
await (apiClient as any).http.fetch(fullUrl, ...);
```
(lines 111–118, 150–157, 193–196)

The `as any` casts are the workaround — because these endpoints are not exposed as typed methods on the generated client, the hooks reach into private internals to construct requests.

## Why it matters
- The manually-defined interfaces can silently drift from the backend: a renamed field or added property won't cause a TypeScript error, only a runtime failure.
- The `as any` casts bypass the TypeScript type system entirely at the call site, removing compile-time safety for the HTTP layer.
- The statistics endpoint (`analytics_GetBankStatementImportStatistics`) is consumed via the generated client correctly in the same file (line 53) — the inconsistency shows the pattern is known but not applied to Bank's own endpoints.

## Suggested fix
Verify that the `BankStatementsController` endpoints (`GET /api/bank-statements`, `POST /api/bank-statements/import`, `GET /api/bank-statements/accounts`) appear in the generated OpenAPI spec and re-run the TypeScript client generation. Replace the manually-defined interfaces with imports from `api-client.ts`, and replace the raw `fetch` + `as any` calls with the generated typed methods. This removes the drift risk and brings the hooks in line with the pattern used for analytics in the same file.

---
_Filed by daily arch-review routine on 2026-06-27._
