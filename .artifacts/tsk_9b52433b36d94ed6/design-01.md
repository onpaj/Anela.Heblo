# Design: Fix PurchaseOrderNumberGenerator dual-clock + minute-resolution collision

No UI surface — this is a backend-only correctness fix. `IPurchaseOrderNumberGenerator`'s
public signature and `CreatePurchaseOrderHandler`'s call site are unchanged, so nothing
downstream of the response DTO (frontend, OpenAPI contract) is affected. UX/UI section
omitted.

## Component design

### 1. `PurchaseOrderNumberGenerator` (Domain, `Features/Purchase/PurchaseOrderNumberGenerator.cs`)

Responsibility change: from "format a string from two clocks" to "produce a UTC-derived,
collision-free `OrderNumber`, bounded-retrying against the store."

**New constructor dependencies:**
- `TimeProvider` — already registered app-wide as a singleton
  (`ServiceCollectionExtensions.cs:130`). One reading per call, taken once at the top of
  `GenerateOrderNumberAsync`, feeds both the date and time-of-day parts of the number.
- `IPurchaseOrderRepository` — same interface `CreatePurchaseOrderHandler` already holds;
  it lives in `Anela.Heblo.Domain.Features.Purchase`, so injecting it into another Domain
  type crosses no layer boundary. Used solely to call the existing
  `OrderNumberExistsAsync(string, CancellationToken)` (`PurchaseOrderRepository.cs:83-86`),
  which becomes live code for the first time.

**Registration** (`PurchaseModule.cs:19`): unchanged line, `AddScoped<IPurchaseOrderNumberGenerator, PurchaseOrderNumberGenerator>()` — DI resolves the two new constructor parameters automatically since both are already registered (`TimeProvider` singleton, `IPurchaseOrderRepository` scoped in the same module, `PurchaseModule.cs:22`).

**Algorithm** (`GenerateOrderNumberAsync(DateTime orderDate, CancellationToken)`):

```
instant := timeProvider.GetUtcNow()      // single reading, used for date AND time
datePart := orderDate as today (Year/Month/Day — unchanged input contract)
timePart := instant.Hour/Minute/Second   // HHmmss, up from HHmm — closes most collisions outright

for attempt in 1..MaxAttempts (5):
    candidate := attempt == 1
        ? "PO{yyyyMMdd}-{HHmmss}"
        : "PO{yyyyMMdd}-{HHmmss}-{attempt}"       // e.g. "-2", "-3" ... on collision
    if not repository.OrderNumberExistsAsync(candidate):
        return candidate

throw PurchaseOrderNumberGenerationFailedException(orderDate, MaxAttempts)
```

Design choices, with reasoning:
- **`orderDate` still supplies the date part, `TimeProvider` supplies the time part** —
  this matches today's behavior (date comes from the domain input, not the clock) while
  fixing the actual bug, which is that the *time* part read a different, non-UTC clock.
  Using `instant`'s date instead of `orderDate`'s would silently change what the date
  prefix means (today it can legitimately differ from "now", e.g. backdated orders) —
  out of scope per the finding, which only flags the clock mismatch and collision risk,
  not the date-part semantics.
- **Seconds resolution as the first line of defense, numeric suffix as the retry
  disambiguator** — chosen over re-reading the clock on retry because two calls
  milliseconds apart would very likely still land in the same second, making a
  clock-only retry loop for practical purposes unbounded in the worst case; a suffix
  guarantees each attempt is a distinct string.
- **No new clock read per retry attempt** — the initial `instant` is reused for all
  attempts within one call; only the suffix changes. Keeps the method's temporal
  behavior deterministic and trivially testable with a fixed `TimeProvider`.
- **`MaxAttempts = 5`** — cheap, indexed (`UNIQUE` index already exists on
  `OrderNumber`) existence checks; five sequential auto-numbered orders in the exact
  same UTC second is already a pathological rate for a manual PO-creation workflow, so
  this is a fast-fail bound, not a throughput limiter.
- **Residual TOCTOU race is accepted, not solved**: two truly concurrent requests can
  both pass `OrderNumberExistsAsync` before either commits. Closing that fully would
  need a DB-level serialization (advisory lock / unique-retry-on-`DbUpdateException` at
  the handler) which is out of scope (plan's Open Questions, resolved as: low-concurrency
  manual workflow, not worth the added complexity now). The unique index remains the
  backstop that turns a race into a loud, typed failure (see §2) rather than silent data
  corruption.

**New type** — `PurchaseOrderNumberGenerationFailedException` (Domain,
`Features/Purchase/PurchaseOrderNumberGenerationFailedException.cs`), following the
existing `GridLayoutPersistenceException` pattern (plain `Exception` subclass, no custom
serialization):

```csharp
public class PurchaseOrderNumberGenerationFailedException : Exception
{
    public PurchaseOrderNumberGenerationFailedException(DateTime orderDate, int attempts)
        : base($"Could not generate a unique purchase order number for {orderDate:yyyy-MM-dd} after {attempts} attempts.")
    {
    }
}
```

### 2. `CreatePurchaseOrderHandler` (Application, unchanged call site + new catch)

The call at `CreatePurchaseOrderHandler.cs:52-54` is untouched syntactically — same
`await _orderNumberGenerator.GenerateOrderNumberAsync(orderDate, cancellationToken)`,
still only invoked on the auto-numbering branch (FR-3: explicit `OrderNumber` supplied
by the client bypasses the generator entirely, exactly as today).

New behavior: `Handle` wraps the call in a `try/catch` for the new exception type and
returns a typed error instead of letting it propagate (there is nothing to save yet at
that point, so no partial state to roll back):

```csharp
string orderNumber;
if (!string.IsNullOrEmpty(request.OrderNumber))
{
    orderNumber = request.OrderNumber;
}
else
{
    try
    {
        orderNumber = await _orderNumberGenerator.GenerateOrderNumberAsync(orderDate, cancellationToken);
    }
    catch (PurchaseOrderNumberGenerationFailedException ex)
    {
        _logger.LogError(ex, "Failed to generate purchase order number for order date {OrderDate}", orderDate);
        return new CreatePurchaseOrderResponse(ErrorCodes.PurchaseOrderNumberGenerationFailed);
    }
}
```

This follows the same shape already used for `SupplierNotFound` earlier in the same
method (`CreatePurchaseOrderHandler.cs:40-44`) — log, return a `BaseResponse`-derived
error DTO with an `ErrorCodes` value, no exception reaches MVC's default handler.

### 3. `ErrorCodes` (Application, `Shared/ErrorCodes.cs`)

New entry in the Purchase module block (`11XX`, next free slot after `PurchaseOrderLineNotFound = 1108`):

```csharp
[HttpStatusCode(HttpStatusCode.Conflict)]
PurchaseOrderNumberGenerationFailed = 1109,
```

`Conflict` (409) matches the semantics — the server could not allocate a unique
identifier, the existing `DuplicateEntry = 0009` general code uses the same status —
and matches what the finding's "surface as a typed error instead of an unhandled 500"
asks for.

## Data schemas

No schema/migration changes. `PurchaseOrder.OrderNumber` stays `string`, `UNIQUE`
index, `HasMaxLength(PurchaseOrderConstants.OrderNumberMaxLength)` = 50
(`PurchaseOrderConfiguration.cs:18-23`). The new format's longest realistic value,
`PO20260803-143022-5`, is 20 characters — well inside the limit; no constant change
needed.

**Request/response shapes** — unchanged:
- `CreatePurchaseOrderRequest.OrderNumber` (optional, client-supplied) — untouched.
- `CreatePurchaseOrderResponse` — untouched shape; on generation failure it now carries
  `ErrorCode = PurchaseOrderNumberGenerationFailed` instead of a 500 with no body, via
  the existing `BaseResponse(ErrorCodes, Dictionary<string,string>?)` constructor
  already used for `SupplierNotFound` in the same handler.

**`OrderNumber` format change** (self-consistency, not a schema change):
- Before: `PO{yyyyMMdd}-{HHmm}` (dual clock, minute resolution).
- After: `PO{yyyyMMdd}-{HHmmss}` on the first attempt, `PO{yyyyMMdd}-{HHmmss}-{n}`
  (n = 2..5) on a collision retry. Single UTC clock source for both parts.
- Consumers that treat `OrderNumber` as an opaque, format-agnostic string are
  unaffected: `PurchaseOrderRepository.GetPaginatedAsync`'s `Contains`/`sortBy` on
  `OrderNumber` (`:30`, `:58`) do plain string operations and don't parse the suffix.

No event payloads exist for this flow (no domain events / message bus involved in PO
creation).
