## Module / File
`backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/RetryStockUpOperation/RetryStockUpOperationHandler.cs`

## Coverage
Zero tests. Not referenced in any test file.

## What's not tested
Four branches:
1. **Operation not found** — returns `Success=false`, `Status=Failed`
2. **Operation already `Completed`** — returns `Success=false`, `Status=AlreadyCompleted` (cannot retry completed ops)
3. **Operation in `Failed` state** — calls `operation.Reset()`, then saves; returns `InProgress`
4. **Operation in any other stuck state** (e.g. `Submitted`) — calls `operation.ForceReset()` with a warning log; returns `InProgress`

The distinction between `Reset()` and `ForceReset()` is the operative business rule. Both converge on the same response shape, so swapping them or always calling one would produce no observable test failure without explicit assertions on which method was invoked.

## Why it matters
Stuck stock-up operations block warehouse processes. If `ForceReset` is never called for stuck `Submitted` operations (e.g. because the condition is wrong), those operations stay stuck indefinitely. Conversely, calling `ForceReset` on a `Failed` operation may bypass state guards that `Reset` enforces.

## Suggested approach
Unit-test with a mocked `IStockUpOperationRepository`. Four tests covering each branch; for the state-machine tests, use a spy/mock to assert which reset method is called. ~1.5 hours.

---
_Filed by weekly coverage-gap routine on 2026-06-08. Based on CI run #27104028537 (6568feba33640ae063b2cb6af3c81da31b3720e1)._