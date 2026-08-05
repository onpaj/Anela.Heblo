# Plan: Fix PurchaseOrderNumberGenerator dual-clock + minute-resolution collision

## Summary
`PurchaseOrderNumberGenerator` (`backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrderNumberGenerator.cs:10-21`) builds auto-generated PO numbers from two different clocks (`orderDate` param for the date part, `DateTime.Now` for the time part) at minute resolution, violating the project's UTC-only rule and colliding with the `UNIQUE` index on `PurchaseOrder.OrderNumber` whenever two auto-numbered orders are created in the same wall-clock minute. This plan unifies the clock source and closes the collision window, mirroring how the equivalent Manufacture bug (#2680) was resolved.

## Context
- `CreatePurchaseOrderHandler.cs:52-54` calls the generator only when the client didn't supply an explicit `OrderNumber`, then saves unconditionally (`:100-101`) with no pre-check — a collision surfaces as an unhandled `DbUpdateException` (500).
- `PurchaseOrderRepository.OrderNumberExistsAsync` (`:83-86`) already exists for exactly this check but is dead code — never called.
- The sibling Manufacture module hit the identical `DateTime.Now` defect and fixed it (closed #2680) by removing the time dependency from the generator entirely: `ManufactureOrderRepository.GenerateOrderNumberAsync(int year, ...)` (`:148-169`) takes only the year (supplied by the caller from a single `TimeProvider` reading) and derives a monotonic per-year sequence — no clock read inside the generator at all.
- `TimeProvider` is already registered app-wide as a singleton (`ServiceCollectionExtensions.cs:130`, `services.AddSingleton(TimeProvider.System)`) and is the established pattern for testable time in this codebase (e.g. `CreateManufactureOrderHandler`, `TimePeriodResolver`, several `Manufacture` services/handlers all inject `TimeProvider` and read it once).
- `PurchaseOrderConstants.OrderNumberMaxLength = 50` — no length pressure on any of the candidate formats below.
- `PurchaseOrderNumberGenerator` is registered `Scoped` in `PurchaseModule.cs:19`; `IPurchaseOrderNumberGenerator`/`IPurchaseOrderRepository` both live in the Domain layer, so the generator depending on the repository interface (not its Persistence implementation) doesn't cross a layer boundary.

## Functional requirements

**FR-1 — Single UTC clock source.**
The generator must derive every time component it emits (date and time-of-day) from one `TimeProvider.GetUtcNow()` reading; `DateTime.Now` must not appear anywhere in the class.
- Acceptance: no `DateTime.Now`/`DateTime.Today` reference remains in `PurchaseOrderNumberGenerator.cs`; a unit test injecting a fake/fixed `TimeProvider` produces a deterministic, expected `OrderNumber`; a test that sets the fake `TimeProvider`'s instant to just before UTC midnight confirms the date part and time part agree (both derived from the same instant), unlike today where a local-time `DateTime.Now` read near a day boundary can disagree with the UTC `orderDate`.

**FR-2 — No duplicate `OrderNumber` on save.**
Auto-generating an order number must never produce a value that collides with an existing `PurchaseOrder.OrderNumber`, including when two auto-numbered orders are created within the same wall-clock minute (or second).
- Acceptance: an integration/handler test that freezes the `TimeProvider` to a fixed instant and creates two purchase orders back-to-back without an explicit `OrderNumber` yields two distinct `OrderNumber`s and no `DbUpdateException`; `OrderNumberExistsAsync` is exercised by this path (verifiable via a repository mock/spy in a unit test).

**FR-3 — Explicit `OrderNumber` path unchanged.**
When the client supplies `OrderNumber` explicitly (`CreatePurchaseOrderHandler.cs:52-54`), no auto-generation or existence-check logic runs — behavior stays exactly as today.
- Acceptance: existing tests covering explicit `OrderNumber` creation pass unmodified; no new validation is added to that branch.

**FR-4 — Graceful failure over unhandled 500.**
If, after a bounded number of attempts, no free `OrderNumber` can be produced (pathological case), the handler must return a normal error response through the existing `ErrorCodes` pattern rather than letting a `DbUpdateException` bubble up as a 500.
- Acceptance: a test that forces every generated candidate to collide (mocked `OrderNumberExistsAsync` always `true`) confirms the handler returns a typed error, not an unhandled exception.

## Non-functional requirements
- **Correctness over cleverness**: the fix must not introduce a TOCTOU race that looks safe but isn't — an existence-check-then-generate loop closes the common case (sequential requests, retries within one process) but two truly concurrent requests can still both pass the check before either inserts. Given PO creation is a low-frequency, largely manual action, this is likely an acceptable residual risk, but it must be a documented decision, not an oversight (see Open Questions).
- **No added latency of consequence**: the existence check is a single indexed lookup (`OrderNumber` has a `UNIQUE` index already); retries must be bounded (small constant, e.g. ≤5) so a pathological run fails fast via FR-4 instead of looping.
- **DateTime standard compliance**: follow `docs/architecture/DateTime_StandardizationGuide.md` — UTC only, no `DateTime.Now`/local `Kind`.

## Data model
No schema changes. `PurchaseOrder.OrderNumber` (string, `UNIQUE` index, max length 50) is unchanged. The generated value's format may change (e.g. adding seconds resolution), but it stays within the existing column constraints.

## Interfaces
- `IPurchaseOrderNumberGenerator.GenerateOrderNumberAsync(DateTime orderDate, CancellationToken)` — keep the public signature stable; internal implementation gains a `TimeProvider` dependency (for the "now" instant used in the suffix) and an `IPurchaseOrderRepository` dependency (for the existence check + retry). No change to `CreatePurchaseOrderHandler`'s call site required.
- No new HTTP endpoints, events, or UI changes — this is a backend-only correctness fix.

## Dependencies and scope
**In scope:**
- `PurchaseOrderNumberGenerator.cs` — remove `DateTime.Now`, inject `TimeProvider` + `IPurchaseOrderRepository`, add collision-avoidance (finer resolution and/or retry using `OrderNumberExistsAsync`).
- `PurchaseModule.cs` — DI registration update if the generator's constructor changes (no interface change expected; `TimeProvider` is already registered app-wide).
- Unit/integration tests for the generator and for `CreatePurchaseOrderHandler`'s auto-numbering path.

**Out of scope:**
- The explicit-`OrderNumber` branch of `CreatePurchaseOrderHandler` (FR-3).
- The Manufacture module (already fixed under #2680).
- `PurchaseOrder.AddLine`/`UpdatedBy` (#3254 — unrelated, already closed).
- True multi-instance/multi-replica race elimination via a DB sequence or advisory lock — flagged as an open question, not committed to by default.
- Any change to the `PO{yyyyMMdd}-...` prefix/date-part format or to reporting/search code that parses `OrderNumber` (`GetPaginatedAsync`'s `Contains`/`sortBy=ordernumber` in `PurchaseOrderRepository.cs:30,58` are format-agnostic string operations and are unaffected either way, but no code should assume the current exact suffix width).

## Rough plan
1. Confirm `TimeProvider` DI wiring is already sufficient (it is — singleton registered in `ServiceCollectionExtensions.cs:130`); no new registration needed.
2. Update `PurchaseOrderNumberGenerator`: inject `TimeProvider` and `IPurchaseOrderRepository` via constructor; replace `DateTime.Now.Hour`/`.Minute` with a single `_timeProvider.GetUtcNow()` reading used for the time-of-day suffix.
3. Close the collision window: raise the suffix resolution (e.g. `HHmmss`) as a cheap first line of defense, and wrap generation in a bounded retry loop that calls the existing `OrderNumberExistsAsync` and regenerates (re-reads the clock and/or appends a small counter) on collision, up to a small max attempt count.
4. Surface the "still colliding after max attempts" case as a typed `ErrorCodes` response from `CreatePurchaseOrderHandler` rather than letting the eventual unique-constraint violation reach `SaveChangesAsync` unhandled (FR-4).
5. Update `PurchaseModule.cs` DI registration for the generator's new constructor dependencies.
6. Add tests: generator unit tests (fixed `TimeProvider`, mocked repository forcing the collision/retry path), and a handler-level test creating two auto-numbered orders under a frozen clock to prove distinct numbers and no exception.
7. Run `dotnet build`, `dotnet format`, and the full Purchase-module backend test suite; confirm no regression in explicit-`OrderNumber` tests.

## Open questions
- **Where should the retry/collision-check logic live** — inside `PurchaseOrderNumberGenerator` (as planned above, keeping `CreatePurchaseOrderHandler` untouched) or lifted into the handler (which already holds `_repository`)? Default: keep it in the generator to preserve the handler's current shape and the public interface contract; revisit in the design step if the Domain layer depending on `IPurchaseOrderRepository` is judged undesirable.
- **Is optimistic check-then-generate an acceptable concurrency guarantee**, or does the finding's "unhandled 500" complaint require an additional defensive catch of `DbUpdateException` around `SaveChangesAsync` with one generate-and-retry as a backstop against true concurrent inserts? Default: add the existence-check retry (closes the practical case described in the finding) and treat a `DbUpdateException`-level backstop as a stretch goal, not a hard requirement, since PO creation is a low-concurrency, largely manual workflow.
- **Exact output format** after the fix — `PO{yyyyMMdd}-{HHmmss}` vs. keeping `HHmm` and appending a retry counter (`PO{yyyyMMdd}-{HHmm}-{n}`)? Default: seconds-resolution suffix (`HHmmss`) plus the existence-check retry loop as the backstop, since it's the smallest change to the existing human-facing identifier shape and stays well under `OrderNumberMaxLength = 50`.
