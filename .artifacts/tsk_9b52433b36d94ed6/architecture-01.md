# Architecture review: PurchaseOrderNumberGenerator dual-clock / collision fix

## Verdict

The plan and design correctly diagnose the bug and land on the right *external* contract
(interface signature stable, `ErrorCodes` conflict response instead of a 500, seconds
resolution + bounded retry). But the design's **placement** of the fix — injecting
`TimeProvider` and `IPurchaseOrderRepository` into the Domain-layer
`PurchaseOrderNumberGenerator` — breaks a layering convention this codebase follows
without exception, and it does so for the *same use case* that already has an
established precedent showing the correct placement. This needs to change before
implementation starts. A second, smaller issue: the new
`PurchaseOrderNumberGenerationFailedException` is unnecessary machinery once the fix is
placed correctly.

## 1. Layering: Domain must not get a clock or a repository dependency

**Grounded fact.** I grepped every `TimeProvider` usage in `backend/src/`. There are ~60
call sites — all in `Application`, `Persistence`, `Adapters`, or `API`. **Zero** are in
`Anela.Heblo.Domain`. `PurchaseOrderNumberGenerator` today is the *only*
service-shaped class in the entire Domain layer (`grep -rn "class.*Generator\|class.*Service" .../Anela.Heblo.Domain/` returns exactly this one hit) — every other Domain
type is an entity, value object, or a dependency-free interface implementation. The
Domain project's `.csproj` has no `ProjectReference` to `Anela.Heblo.Persistence` and no
runtime dependency graph that assumes a Domain class holds injected infrastructure.

**Grounded precedent.** The design's own justification — "mirrors #2680" — doesn't hold
up on inspection. The actual Manufacture fix (`ManufactureOrderRepository.cs:148-169`,
`IManufactureOrderRepository.cs:24`) does not put a generator in the Domain layer at
all:
- `CreateManufactureOrderHandler.cs:20-44` injects `TimeProvider` into the
  **Application-layer handler**, reads it once (`var now = _timeProvider.GetUtcNow()`),
  and passes only `now.Year` (a plain `int`) into the repository call.
- `GenerateOrderNumberAsync(int year, ...)` lives on `IManufactureOrderRepository` /
  `ManufactureOrderRepository` — a **Persistence-layer** method — and queries
  `_context.ManufactureOrders` directly for the last-used number. It takes no
  `TimeProvider` and reads no clock itself; a comment in the file even says so
  explicitly: *"year is supplied by the caller; do not introduce
  TimeProvider/DateTime.Now here."*

So the established pattern for "resolve the instant once, then generate a number that
needs to look at existing rows" is: **clock in the Application handler, data lookup in
the Persistence repository, nothing infrastructure-shaped in Domain.** The design
proposes the opposite for Purchase: clock and repository both pushed down into a Domain
class. That's a new pattern, introduced under a change that claims to be following the
old one.

**Why it matters, concretely**, beyond "it's inconsistent":
- It makes `PurchaseOrderNumberGenerator` untestable the way the rest of Domain is
  (pure, no mocks needed) — every test now needs a `Mock<IPurchaseOrderRepository>`
  and a fake `TimeProvider` to exercise a Domain type, which today is a project with
  zero test doubles in its dependency graph.
- `IPurchaseOrderNumberGenerator` has exactly one caller
  (`grep` confirms: only `CreatePurchaseOrderHandler`, its test, and `PurchaseModule.cs`
  reference it) — there is no abstraction benefit being preserved by keeping the retry
  logic behind a separate Domain interface; it's pure indirection.
- The Application-layer handler (`CreatePurchaseOrderHandler`) already holds
  `_repository` (`IPurchaseOrderRepository`) — the design is adding a *second* copy of
  the same dependency one layer down for no functional reason.

**Recommended direction (pick one, both are better than the design's current shape):**

- **Option A — recommended, smallest diff.** Keep `PurchaseOrderNumberGenerator` in
  Domain exactly as dependency-free as it is today (no constructor changes at all).
  Change its responsibility to pure formatting: `GenerateCandidate(DateTime orderDate,
  DateTimeOffset now, int attempt)` (can even drop `Task`/`async` — there's no I/O left,
  it's string formatting). Move the retry loop, the `OrderNumberExistsAsync` calls, and
  the `TimeProvider.GetUtcNow()` read into `CreatePurchaseOrderHandler`, which gains a
  `TimeProvider` constructor parameter exactly like `CreateManufactureOrderHandler`
  already does. The handler already has `_repository` for the existence check.
- **Option B — closer to the literal #2680 precedent.** Delete
  `IPurchaseOrderNumberGenerator`/`PurchaseOrderNumberGenerator` and add a
  `GenerateUniqueOrderNumberAsync(DateTime orderDate, DateTimeOffset now, CancellationToken)`
  method directly to `IPurchaseOrderRepository`/`PurchaseOrderRepository`
  (`Persistence/Purchase/PurchaseOrders/PurchaseOrderRepository.cs`), doing the
  retry-with-`AnyAsync` loop against `DbSet` there. `CreatePurchaseOrderHandler` still
  resolves `now` via an injected `TimeProvider` and passes it in. This is a slightly
  bigger diff (touches the repository interface) but is the most literal match to how
  Manufacture actually solved this.

Either option keeps `TimeProvider` usage at zero inside Domain and keeps the
existence-check where every other "does this value already exist" repository method in
this codebase already lives. I lean **Option A**: it changes fewer files, and the
"surgical changes" project rule favors not touching `IPurchaseOrderRepository`'s public
contract when the fix doesn't require it.

## 2. Drop the new exception type — the handler already has the idiom for this

`CreatePurchaseOrderHandler.cs:39-44` already handles an analogous failure
(`SupplierNotFound`) by checking a condition and returning
`new CreatePurchaseOrderResponse(ErrorCodes.SupplierNotFound, ...)` directly — no
exception, no catch block. If the retry loop moves into the handler per Option A above,
the "exhausted all attempts" case is just the same shape: the loop finishes, no free
number was found, `return new CreatePurchaseOrderResponse(ErrorCodes.PurchaseOrderNumberGenerationFailed)`.

That removes `PurchaseOrderNumberGenerationFailedException` from scope entirely — one
fewer new type, no `try/catch`, and the method reads the same way its neighbor
(`SupplierNotFound`) does two lines above. The `GridLayoutPersistenceException`
precedent the design cites for the exception's *shape* is a fine reference if an
exception were needed, but it isn't: that exception is never actually caught anywhere in
this codebase today (`grep -rn "GridLayoutPersistenceException"` outside its own
definition returns nothing) — it's a "let it 500, at least with a named type in the
logs" pattern, which is exactly what this finding is asking us to move *away* from. Don't
reach for that pattern when the simpler, already-proven direct-return pattern fits.

If Option B is chosen instead (logic in the repository), the repository method can
either return `null`/a bool "found" flag on exhaustion (handler still does the direct
`ErrorCodes` return, no exception crosses the Persistence→Application boundary) — same
conclusion, no new exception type needed either way.

## 3. What the design got right — keep these

- **Single clock source for date+time.** Reading one `TimeProvider.GetUtcNow()` and
  deriving both parts of the suffix from it is exactly the right fix for problem (a) in
  the finding, wherever the read ends up living.
- **`orderDate` still supplies the date part.** Correctly scoped — not conflating the
  domain-meaningful order date with "now," which can legitimately differ (backdated
  orders). No change needed here.
- **`ErrorCodes.PurchaseOrderNumberGenerationFailed = 1109`** — verified against
  `ErrorCodes.cs`: `1108` (`PurchaseOrderLineNotFound`) is indeed the last used slot in
  the `11XX` Purchase block, so `1109` is correctly the next free value.
  `[HttpStatusCode(HttpStatusCode.Conflict)]` matches the existing `DuplicateEntry`
  (`0009`) convention for "couldn't allocate a unique identifier." No change needed.
- **`IPurchaseOrderNumberGenerator`'s public signature staying stable** is right in
  spirit — `CreatePurchaseOrderHandler`'s call site shouldn't need to change shape. (Under
  Option A the signature does change to accept `now`/`attempt`, which is a small,
  contained change to the one caller — acceptable and still far smaller than the
  alternative of leaving `TimeProvider` inside Domain.)
- **Seconds resolution + numeric-suffix retry, `MaxAttempts = 5`.** Reasonable, cheap
  (indexed lookup), bounded. No objection.
- **Accepting the residual TOCTOU race as a documented decision, not fixing it with a
  DB-level lock.** Correct call for this workflow's concurrency profile. Worth noting for
  the record: the actual Manufacture precedent (#2680) doesn't even have a
  check-then-generate retry loop — it just computes next-in-sequence from the last row
  and accepts the same race with no typed failure path at all. This design's addition of
  a bounded retry + typed conflict response is *more* rigorous than what #2680 shipped,
  which is fine (the finding's part (b) explicitly asks for it) — just don't describe it
  as "mirroring" #2680's collision handling, since #2680 has none. Only the
  single-clock-source part is actually shared with #2680.
- **No schema changes, format stays within `OrderNumberMaxLength = 50`.** Confirmed
  against `PurchaseOrderConfiguration.cs:18-23` — no objection.

## Prerequisites before implementation starts

1. Decide Option A vs. B above (recommend A) and update the design doc's Component 1
   section accordingly before coding — this changes which file `TimeProvider`/the
   retry loop lands in and removes the exception type from Component 1's "New type."
2. Drop `PurchaseOrderNumberGenerationFailedException` from scope; replace the
   handler's `try/catch` (Component 2 in the design) with a direct
   `ErrorCodes.PurchaseOrderNumberGenerationFailed` return, matching the
   `SupplierNotFound` branch immediately above it in the same method.
3. No other prerequisite — `TimeProvider` is already a registered singleton
   (`ServiceCollectionExtensions.cs:130`), `OrderNumberExistsAsync` already exists on
   `IPurchaseOrderRepository`/`PurchaseOrderRepository`, and `PurchaseModule.cs`'s DI
   registrations need no change under Option A (constructor of
   `CreatePurchaseOrderHandler` gains one parameter, resolved automatically; Option B
   would need no `PurchaseModule.cs` change either, since
   `IPurchaseOrderRepository`/`PurchaseOrderRepository` is already registered).

## Risks

- **Test churn**: existing `CreatePurchaseOrderHandlerTests.cs` mocks
  `IPurchaseOrderNumberGenerator` directly (`GenerateOrderNumberAsync(DateTime, CancellationToken)` → `string`). Under Option A the mocked interface shape changes
  (adds `now`/`attempt`, or the whole retry moves out of the generator and into the
  handler, changing what the test needs to set up). This is expected, contained churn —
  flag it so the implementer doesn't treat failing pre-existing tests as a regression
  signal to revert, but as the expected fallout of moving the seam.
- **If Option B is chosen**, any other test doubles implementing the full
  `IPurchaseOrderRepository` interface (not just `Mock<T>`, which is additive-safe) would
  need the new method; a quick grep for hand-written fakes of that interface (as
  opposed to Moq-based mocks) should happen before committing to B.
