## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs:47` — The implementation omits the spec's suggested `g.Where(x => x.LastSyncTime.HasValue).Max(...)` guard and instead does `g.Max(x => (DateTime?)x.LastSyncTime)` directly. This is behaviorally equivalent (both LINQ's nullable `Max` and SQL `MAX()` ignore `NULL`s and return `null` when all values are null or the group is empty), and is simpler than the spec's suggestion — worth noting only so a future reader doesn't assume the extra filter was dropped by mistake.
- `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs:120` — Spec's FR-1 acceptance criteria call out a zero-matching-invoices case (`TotalInvoices = 0`, `LastSyncTime = null`, no exception from the empty `GroupBy` result). The `stats == null` branch in `GetSyncStatsAsync` handles this correctly, but no test in this file exercises a date range with zero rows in it (all three new/edited tests insert at least one in-range invoice). Consider adding a quick case to lock in that branch.
