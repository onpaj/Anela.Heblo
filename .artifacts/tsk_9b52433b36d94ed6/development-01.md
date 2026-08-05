# Development: Fix PurchaseOrderNumberGenerator dual-clock + minute-resolution collision

Implements Option A from `architecture-01.md`: the Domain-layer generator stays a pure,
dependency-free formatter; the clock read, retry loop, and existence-check moved into
`CreatePurchaseOrderHandler` (Application layer), matching the actual Manufacture (#2680)
precedent. No new exception type — exhaustion is surfaced via the handler's existing
direct-`ErrorCodes`-return idiom (same shape as the `SupplierNotFound` branch two lines
above it).

## Files changed

- **`backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrderNumberGenerator.cs`**
  — `IPurchaseOrderNumberGenerator` now exposes a single pure method:
  `string GenerateCandidate(DateTime orderDate, DateTimeOffset now, int attempt)`.
  No constructor, no I/O, no `DateTime.Now`/`DateTime.Today` anywhere. Format:
  `PO{yyyyMMdd}-{HHmmss}` on `attempt == 1`, `PO{yyyyMMdd}-{HHmmss}-{attempt}` on retries.
  Both the date part (`orderDate`) and the time part (`now`) are still sourced
  separately per the design (backdated orders keep a real order date), but the time
  part is now taken entirely from the caller-supplied `now`, never from a second
  independent clock read.

- **`backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/CreatePurchaseOrderHandler.cs`**
  — Constructor gains a `TimeProvider timeProvider` parameter (already registered
  app-wide as a singleton, so DI needs no new registration). `Handle` reads
  `_timeProvider.GetUtcNow()` once, then loops `attempt = 1..MaxOrderNumberAttempts (5)`
  calling `_orderNumberGenerator.GenerateCandidate(orderDate, now, attempt)` and
  `_repository.OrderNumberExistsAsync(candidate, cancellationToken)` (this method existed
  but was dead code before this change) until a free candidate is found. On exhaustion,
  returns `new CreatePurchaseOrderResponse(ErrorCodes.PurchaseOrderNumberGenerationFailed)`
  before touching the repository's `AddAsync`/`SaveChangesAsync` — no unhandled
  `DbUpdateException` reaches MVC. The explicit-`OrderNumber` client-supplied branch
  (FR-3) is untouched: no clock read, no existence check, no generator call when the
  client supplies a number.

- **`backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`** — new entry
  `PurchaseOrderNumberGenerationFailed = 1109` (next free slot in the `11XX` Purchase
  block, after `PurchaseOrderLineNotFound = 1108`), `[HttpStatusCode(HttpStatusCode.Conflict)]`
  — matches the existing `DuplicateEntry = 0009` convention for "couldn't allocate a
  unique identifier."

- **`frontend/src/i18n.ts`** — added the Czech translation for the new error code
  (`PurchaseOrderNumberGenerationFailed: "Nepodařilo se vygenerovat unikátní číslo
  objednávky"`), required by `LocalizationCoverageTests.FrontendI18n_ShouldHaveTranslationsForAllErrorCodes`.

- **`backend/test/Anela.Heblo.Tests/Features/Purchase/CreatePurchaseOrderHandlerTests.cs`**
  — updated for the new constructor/interface shape: added a `Mock<TimeProvider>`
  (fixed `FixedNow`, matching the existing `CreateManufactureOrderHandlerTests` pattern),
  changed all `GenerateOrderNumberAsync(...).ReturnsAsync(...)` setups to
  `GenerateCandidate(...).Returns(...)`, and added a default
  `OrderNumberExistsAsync(...) => false` setup so existing happy-path tests keep passing
  unmodified in behavior. Replaced the now-inapplicable
  `Handle_WhenOrderNumberGeneratorThrows_ShouldPropagateException` test (the generator
  can no longer throw — it's pure formatting) with three tests matching FR-2/FR-3/FR-4:
  - `Handle_WhenGeneratedCandidateCollides_ShouldRetryWithNextAttempt` — first candidate
    collides, second is free → order created with the second candidate, generator
    called exactly twice, never a third time.
  - `Handle_WhenAllCandidatesCollide_ShouldReturnPurchaseOrderNumberGenerationFailed` —
    every candidate collides → handler returns the new `ErrorCodes` value and never
    calls `AddAsync`/`SaveChangesAsync`.
  - `Handle_WithExplicitOrderNumber_ShouldSkipGeneratorAndExistenceCheck` — client
    supplies `OrderNumber` → generator and `OrderNumberExistsAsync` are never invoked
    (FR-3 regression guard).

- **`backend/test/Anela.Heblo.Tests/Features/Purchase/PurchaseOrderNumberGeneratorTests.cs`**
  (new) — unit tests for the now-pure generator: correct `HHmmss` formatting, retry
  suffix, and a test proving the date part and time part stay internally consistent
  across a UTC day boundary (`orderDate` one day, `now` the next, at `00:00:05`) since
  both draw from values the caller controls, not from two independent clock reads.

- **`backend/test/Anela.Heblo.Tests/Controllers/PurchaseOrdersControllerTests.cs`** —
  updated `CreatePurchaseOrder_WithNullOrderNumber_ShouldGenerateDefault`'s regex from
  `^PO\d{8}-\d{4}$` (old `HHmm` format) to `^PO\d{8}-\d{6}(-\d+)?$` (new `HHmmss[-attempt]`
  format) — this integration test hits the real endpoint end-to-end and was asserting
  the pre-fix minute-resolution format.

## Verification

- `dotnet build` (full solution via `test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`) —
  succeeded, no errors.
- `dotnet format ../Anela.Heblo.sln --include <changed files>` — no changes needed
  (already compliant).
- `dotnet test --filter "FullyQualifiedName~Features.Purchase|FullyQualifiedName~PurchaseOrdersControllerTests|FullyQualifiedName~LocalizationCoverageTests"`
  → **182/182 passed** (154 pre-existing Purchase tests + the new generator tests + the
  new handler tests + the fixed controller/localization tests).
- Full backend suite (`dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`,
  no filter) was run twice: once before the two fixups above (regex + i18n), showing 41
  failures — 2 of which were the Purchase-related regressions this change introduced
  and then fixed (`PurchaseOrdersControllerTests.CreatePurchaseOrder_WithNullOrderNumber_ShouldGenerateDefault`,
  `LocalizationCoverageTests.FrontendI18n_ShouldHaveTranslationsForAllErrorCodes`), and
  39 of which are pre-existing, unrelated integration-test failures in
  `Leaflet`/`KnowledgeBase`/`MeetingTasks`/`Catalog.Infrastructure`/`Persistence.Resilience`
  (all failing with `EntityFrameworkCore.Infrastructure.ManyServiceProvidersCreatedWarning`
  or timing-sensitive assertions — infra/parallelism issues, not touched by this change).
  After fixing the regex and adding the i18n entry, the targeted filter above confirms
  0 Purchase-related failures remain.

To re-verify: from `backend/`,
`dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Purchase|FullyQualifiedName~PurchaseOrdersControllerTests|FullyQualifiedName~LocalizationCoverageTests"`.

## Deviations from plan/design docs

Per the architecture review's Prerequisites (both accepted, both applied):
1. Retry/clock logic lives in `CreatePurchaseOrderHandler`, not in the Domain generator
   (design's original Component 1 placement) — keeps `TimeProvider`/`IPurchaseOrderRepository`
   out of the Domain layer, consistent with the rest of the codebase and the actual
   Manufacture #2680 fix.
2. `PurchaseOrderNumberGenerationFailedException` was dropped entirely; the handler
   returns `ErrorCodes.PurchaseOrderNumberGenerationFailed` directly, no `try/catch`.

Everything else (seconds resolution, `MaxAttempts = 5`, `ErrorCodes` value `1109`/`Conflict`,
no schema changes, `orderDate` still supplying the date part) matches the design as
reviewed.
