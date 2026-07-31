# Merge review — PR #3807

**Title:** [arch-review] Manufacture: useManufactureOrders list & protocol hooks bypass the typed generated client via `(apiClient as any).http.fetch`
**Base:** main · **Head:** harness/tsk_b8f2386a67e8459f · **Closes:** #3797

## What the PR does

Two hooks in `frontend/src/api/hooks/useManufactureOrders.ts` bypassed the generated NSwag client
by reaching into private fields (`(apiClient as any).baseUrl`, `(apiClient as any).http.fetch`) and
hand-building URLs/query strings. This PR routes both through the already-generated typed methods:

- `useManufactureOrdersQuery` → `manufactureOrder_GetOrders(...)` (11 positional filter params)
- `useOpenManufactureProtocol` → `manufactureOrder_GetProtocolPdf(orderId)` (returns `FileResponse`)

Net code change is small: **2 files** (`useManufactureOrders.ts` +19/−42, coupled test +20/−33).
The remaining ~1150 additions are process-artifact markdown under `.artifacts/tsk_b8f2386a67e8459f/`,
which is an established, already-tracked convention in this repo (dozens of prior `tsk_*` dirs are
committed on main).

## Verification performed

Read the full diff, the surrounding hook source, the generated client, and both consumers.

- **Signatures match the calls exactly.**
  - `manufactureOrder_GetOrders(state, dateFrom, dateTo, responsiblePerson, orderNumber, productCode, erpDocumentNumber, manualActionRequired, lotNumber, pageNumber, pageSize)` at `api-client.ts:6917` — same positional order the code passes. `pageNumber`/`pageSize` are `number | undefined` (not `null`), and the code correctly normalizes every arg with `?? undefined`.
  - `manufactureOrder_GetProtocolPdf(id): Promise<FileResponse>` at `api-client.ts:7336`; `FileResponse.data: Blob` (`api-client.ts:43171`) is a drop-in for the old `response.blob()`.
- **Anti-pattern fully removed.** No `(apiClient as any).http`/`.baseUrl` remains in either hook; the only surviving `as any` is the pre-existing `getManufactureOrdersClient()` cast used by all sibling hooks.
- **Consumers compile & read unchanged fields.** `ManufactureOrderList.tsx:90-92` destructures `data.orders/totalCount/totalPages` (all on `GetManufactureOrdersResponse`); `ManufactureOrderDetail.tsx:124` uses only `{ openProtocol, isLoading }` — it does **not** read `error`, so the one accepted behavior change (error string `"HTTP error! status: 404"` → SwaggerException's `"An unexpected server error occurred."`) is not user-visible; it's only logged to console.
- **Tests pass.** Ran the coupled `useOpenManufactureProtocol.test.ts` via `react-scripts test` → **6/6 green**.
- **Build clean.** `react-scripts build` → "Compiled successfully" (project's pinned TS 4.9.5). (`npx tsc` surfaced errors only inside `node_modules/react-i18next` — a lib/TS-version mismatch pre-existing in the repo and suppressed by CRA's skipLibCheck; unrelated to this diff.)
- **Lint clean.** `eslint` on both changed files → 0 problems.

Note: I installed dependencies with `--legacy-peer-deps` (a react-i18next↔typescript peer conflict blocked plain `npm ci`); this is a local tooling detail and does not reflect a code issue.

## Assessment against the review questions

1. **Scope** — matches the issue exactly (kill the private-field bypass in these two hooks); no unrelated changes. The identical anti-pattern in `useSemiproductRecipePdf.ts` was correctly left out of scope and flagged for a follow-up.
2. **Correctness** — behavior is equivalent request-for-request; the dropped `response.ok` branch is unreachable now that the typed method throws on non-2xx (caught by the existing `try/catch`). Date/bool/number serialization is handled by the generated client.
3. **Blast radius** — none. No auth, secrets, migrations, deletion, payments, or CI/release config. Client-side call routing only; no API contract change.
4. **Conventions** — brings these two holdouts in line with the nine sibling hooks and `docs/development/api-client-generation.md` Enforcement Rule 3.

## Verdict

Small, well-scoped, fully verified refactor. Build, lint, and the affected tests pass in-tree; the sole behavior delta is provably not user-visible. I would merge this myself.

```json
{"confidence": 0.92, "reasoning": "Small, in-scope typed-client refactor of two hooks; signatures/return shapes verified against the generated client, build+lint+the affected test suite (6/6) pass in-tree, sole consumer compiles and the one error-string change is not user-visible. No blast-radius surfaces touched.", "risks": ["The 'not ok' error message changes from 'HTTP error! status: N' to the generic SwaggerException text, but no consumer reads that error state (verified), so it is inert", "Full frontend test suite not run end-to-end (only the coupled file); build+lint+targeted tests were green and the change is narrow"]}
```
