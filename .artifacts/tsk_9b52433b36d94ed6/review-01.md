# Review: PurchaseOrderNumberGenerator dual-clock / collision fix

## Verdict: done

## Conformance to architecture review (architecture-01.md)

Both prerequisites from the architecture review are implemented exactly as recommended (Option A):

1. **Layering.** `PurchaseOrderNumberGenerator` (Domain) is now a pure, dependency-free
   formatter — `GenerateCandidate(DateTime orderDate, DateTimeOffset now, int attempt)`,
   no constructor, no I/O, synchronous (`Task`/`async` correctly dropped). Confirmed zero
   `TimeProvider`/`IPurchaseOrderRepository` references in the Domain project after the
   change. The clock read (`_timeProvider.GetUtcNow()`, called once) and the retry loop
   against `IPurchaseOrderRepository.OrderNumberExistsAsync` both live in
   `CreatePurchaseOrderHandler` (Application layer), matching the actual Manufacture
   (#2680) precedent the review verified.
2. **No new exception type.** `PurchaseOrderNumberGenerationFailedException` was dropped;
   the handler returns `new CreatePurchaseOrderResponse(ErrorCodes.PurchaseOrderNumberGenerationFailed)`
   directly on retry exhaustion, in the same shape as the neighboring `SupplierNotFound`
   branch — exactly the idiom the review pointed at.

## Conformance to spec / functional requirements

- **Single clock source.** Both the date prefix (`orderDate`, still handler-supplied) and
  time suffix (`now.Hour/Minute/Second`) now derive from values passed into the pure
  formatter — no second independent `DateTime.Now` read. Resolves finding (a).
- **Collision closure.** The previously-dead `OrderNumberExistsAsync` is now called in a
  bounded 5-attempt retry loop before ever touching `AddAsync`/`SaveChangesAsync`,
  closing the minute-resolution collision window from finding (b) and avoiding the
  unhandled `DbUpdateException` → 500 path. Seconds resolution plus a numeric attempt
  suffix (`PO{yyyyMMdd}-{HHmmss}[-{attempt}]`) makes same-second collisions unlikely and
  recoverable up to 5 tries; exhaustion now returns a typed `1109`/`Conflict` response
  instead of crashing.
- **Explicit-`OrderNumber` path untouched.** Client-supplied order numbers still skip the
  generator and the existence check entirely (verified by a dedicated regression test).
- **No schema changes**, format stays within `OrderNumberMaxLength`. Confirmed.
- **`ErrorCodes.PurchaseOrderNumberGenerationFailed = 1109`**, `[HttpStatusCode(Conflict)]`,
  next free slot after `1108` — correct. i18n entry added, satisfying
  `LocalizationCoverageTests`.

## Completeness

- New `PurchaseOrderNumberGeneratorTests.cs` covers pure formatting, retry suffix, and the
  date/time-source independence across a UTC day boundary — directly targets finding (a).
- `CreatePurchaseOrderHandlerTests.cs` updated for the new interface shape and gained three
  tests matching the FRs: retry-on-collision, exhaustion → typed error (and no
  `AddAsync`/`SaveChangesAsync` call), and explicit-order-number skip path. The old
  "generator throws → propagate exception" test was correctly removed since the pure
  formatter can no longer throw.
- `PurchaseOrdersControllerTests.cs` regex updated from the stale `HHmm` format to
  `HHmmss[-attempt]`, catching what would otherwise have been a silent integration-test
  false-negative.

## Independent verification (this review, not just the dev step's claims)

- `dotnet build Anela.Heblo.sln` — succeeded, 0 errors (251 pre-existing warnings,
  none in changed files).
- `dotnet test --filter "FullyQualifiedName~Features.Purchase|FullyQualifiedName~PurchaseOrdersControllerTests|FullyQualifiedName~LocalizationCoverageTests" --no-build`
  → **Passed! Failed: 0, Passed: 182, Skipped: 0, Total: 182**, matching the development
  step's claim exactly.
- `dotnet format Anela.Heblo.sln --verify-no-changes --include <changed files>` → exit 0,
  no output — confirms no formatting drift.

## Non-blocking observations (not grounds for request_changes)

- A TOCTOU race remains between `OrderNumberExistsAsync` and `SaveChangesAsync`: two
  concurrent requests could both pass the existence check for the same candidate and one
  would still hit the unique-index `DbUpdateException` on save. The architecture review
  explicitly accepted this as a documented, out-of-scope decision consistent with this
  workflow's concurrency profile (and noted the actual #2680 precedent has no retry loop
  at all, so this fix is already more rigorous). Not a regression and not part of the
  finding's ask.
- Retry candidates within the same second differ only by an explicit `-{attempt}` suffix
  rather than finer time resolution — an intentional, reviewed design choice, not a defect.

Both the diagnosis and the fix are correctly scoped to the two problems named in the
finding, the architecture review's prerequisites were fully applied, and all
verification claims from `development-01.md` check out independently.
