[arch-review] FinancialOverview: redundant combined Task.WhenAll in CalculateMonthlyStockChangeAsync

## Module
FinancialOverview

## Finding
`FinancialOverviewStockValueAdapter.CalculateMonthlyStockChangeAsync` (lines 141–144) runs a combined `Task.WhenAll` over all six warehouse queries, then immediately re-awaits the same tasks a second time to extract results:

```csharp
await Task.WhenAll(startStockTasks.Concat(endStockTasks)); // line 141 — all 6 run; results discarded

var startValues = await Task.WhenAll(startStockTasks);    // line 143 — already completed
var endValues   = await Task.WhenAll(endStockTasks);      // line 144 — already completed
```

The first `Task.WhenAll` is dead code: it drives the concurrent execution but its results are thrown away. The actual `decimal[]` results come from the two separate awaits on tasks that are already finished. The sibling method `GetStockValueChangeForPeriodAsync` (lines 103–104 in the same file) uses the correct two-await pattern without the spurious combined call.

File: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/FinancialOverviewStockValueAdapter.cs:141-144`

## Why it matters
The dead first `WhenAll` misleads readers into believing the sequential awaits behave differently from the combined run (e.g. that results are somehow merged or that parallelism depends on the combined call). In reality all six tasks are already started concurrently before the first `await` is reached, so the combined call adds zero throughput benefit. It is confusion without benefit, and a maintenance risk if someone removes the "real" awaits thinking the combined one captures the results.

## Suggested fix
Remove the redundant combined await; keep the two individual awaits that capture results — identical to the pattern in `GetStockValueChangeForPeriodAsync`:

```csharp
// Remove this line:
// await Task.WhenAll(startStockTasks.Concat(endStockTasks));

var startValues = await Task.WhenAll(startStockTasks);
var endValues   = await Task.WhenAll(endStockTasks);
```

All six tasks still run concurrently because they are created (and thus started) before the first `await`.

---
_Filed by daily arch-review routine on 2026-09-01._
