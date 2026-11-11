### task: extract-helpers

**Files:**
- Create: `frontend/src/pages/stockOperationsUtils.ts`
- Modify: `frontend/src/pages/StockOperationsPage.tsx` (lines ~147-157 `isOperationStuck`, ~203-210 `canRetry`)

- [ ] Create `stockOperationsUtils.ts` exporting `isOperationStuck(operation: { state?: StockUpOperationState; submittedAt?: Date | string; createdAt?: Date | string }, now: Date = new Date()): boolean` (body moved verbatim: Submitted && submittedAt -> `differenceInMinutes(now, new Date(submittedAt)) > 5`; Pending -> `differenceInMinutes(now, new Date(createdAt)) > 10`; else false) and `canRetry(state?: StockUpOperationState): boolean` (verbatim).
- [ ] In the page, delete both inline closures, add `import { isOperationStuck, canRetry } from "./stockOperationsUtils";`. Do not touch `getStuckMessage` or other code.
- [ ] Verify: `cd frontend && npm run build && npm run lint` pass.
- [ ] Commit: `refactor(frontend): extract stock operation stuck/retry helpers`

