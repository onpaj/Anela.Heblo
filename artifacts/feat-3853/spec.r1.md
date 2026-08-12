# Specification: Extract shared shipment-creation logic from ScanPackingOrderHandler / ResetOrderShipmentHandler and fix missing Package persistence on reset

## Summary
`ScanPackingOrderHandler` and `ResetOrderShipmentHandler` (Packaging/Baleni module) both build a carrier shipment for an order and map the resulting labels onto `N` packages, but the two implementations are hand-copied and have already drifted: `ScanPackingOrderHandler` persists `Package` rows for the shipment it creates, `ResetOrderShipmentHandler` does not. This spec extracts the shared "resolve weight → resolve carrier → create shipment → fetch & pad labels → persist `Package` rows" logic into one collaborator used by both handlers, which both removes the duplication and closes the persistence gap as a direct, structural consequence (not a patched-on fix).

## Background
Both handlers exist to turn a Shoptet packing order into a carrier shipment during the physical packing workflow:

- `ScanPackingOrderHandler` (`backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs`) is invoked when a warehouse worker scans an order barcode. If no shipment exists yet, it creates one and persists `Package` rows via `IPackageRepository.ReplacePackagesForOrderAsync`.
- `ResetOrderShipmentHandler` (`backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ResetOrderShipment/ResetOrderShipmentHandler.cs`) is invoked when a worker invalidates an existing shipment (e.g. wrong package count) and asks for a fresh one. It cancels the old carrier shipment(s), creates a replacement, and returns it to the frontend — but never touches `IPackageRepository`. It has no reference to the interface at all.

Confirmed via `frontend/src/components/baleni/PackingShipmentCreator.tsx:69-79` (`handleInvalidateAndNew`): reset is a **terminal** step in the FE flow — the result is handed straight to the label printer, there is no follow-up re-scan that would otherwise persist the rows through `ScanPackingOrderHandler`.

**Impact of the bug today:** for any order that goes through reset, the old (now-cancelled) `Package` rows are left in the table and no rows are written for the replacement shipment. Every read keyed on `Package` then misreports for that order:
- `GetPackingStatisticsHandler` / `IPackageRepository.GetPackingStatisticsAsync` — per-packer, per-carrier, tracking-coverage figures
- `GetPackingDashboardHandler` — "packed today" counts
- `FillTrackingNumbersJob` (`backend/src/Anela.Heblo.Application/Features/Packaging/Infrastructure/Jobs/FillTrackingNumbersJob.cs`) — backfills `TrackingNumber` for rows with `TrackingNumber == null`; it never sees the replacement shipment's packages because no rows exist for them, and it keeps re-processing the stale cancelled-shipment rows instead.

This is the same class of finding already accepted once for this handler pair (#3194, closed/completed: duplicated packing-eligibility warning strings). `docs/architecture/development_guidelines.md` calls out DRY/module-independence as the governing rule.

## Functional Requirements

### FR-1: Extract a shared shipment-creation collaborator
Introduce a new collaborator, `IShipmentCreationService` (implementation `ShipmentCreationService`), owning the entire block that is currently duplicated:

1. Package-count validation (`1..10`, currently the `maxPackages = 10` / `ErrorCodes.InvalidPackageCount` guard duplicated at `ScanPackingOrderHandler.cs:48-50` and `ResetOrderShipmentHandler.cs:34-36`).
2. Total order weight computation with the zero-weight fallback + warning log (`ScanPackingOrderHandler.cs:120-127` / `ResetOrderShipmentHandler.cs:62-69`), using `ShipmentLabelsSettings.FallbackPackageWeightGrams`.
3. Per-package weight: `Math.Max(totalWeightGrams / n, MinPackageWeightGrams)` (`ScanPackingOrderHandler.cs:130` / `ResetOrderShipmentHandler.cs:72`).
4. Carrier resolution via `IShipmentClient.GetShippingOptionsAsync`, failing with `ErrorCodes.ShipmentCarrierNotResolved` when empty (`ScanPackingOrderHandler.cs:132-134` / `ResetOrderShipmentHandler.cs:74-76`).
5. `CreateShipmentCommand` construction and `IShipmentClient.CreateShipmentAsync` call, catching exceptions into `ErrorCodes.ShipmentCreationFailed` (`ScanPackingOrderHandler.cs:136-158` / `ResetOrderShipmentHandler.cs:78-100`).
6. Re-fetching labels via `IShipmentClient.GetLabelsByOrderCodeAsync` and padding to exactly `n` entries so the FE's "X/N" counter is always correct even when Shoptet has not generated every label yet (`ScanPackingOrderHandler.cs:165-180` / `ResetOrderShipmentHandler.cs:104-120`).
7. Packer resolution (`ResolvePackerAsync` equivalent: resolve `PackedByUserId`/`PackedBy` from an optional `packingUserId`, falling back to `ICurrentUserService.GetCurrentUser().Email` when absent) and persistence of `Package` rows via `IPackageRepository.ReplacePackagesForOrderAsync` (currently only in `ScanPackingOrderHandler.PersistPackagesAsync`, `ScanPackingOrderHandler.cs:297-344`).

**Acceptance criteria:**
- A single implementation of steps 1–7 exists; no order-weight/carrier/shipment-creation/label-padding/persistence logic is duplicated between the two handlers' source files.
- The collaborator's persistence call uses `ReplacePackagesForOrderAsync` (delete-then-insert for the order code), not `AddAsync`/`AddMissingAsync`, so that a reset correctly clears the stale rows from the cancelled shipment in the same operation that writes the replacement rows.
- `PackageNumber` continues to be assigned as a 1-based index within the order (`(index + 1).ToString(CultureInfo.InvariantCulture)`), matching the existing comment in `ScanPackingOrderHandler.cs:313-316` about carrier package names not being unique.

### FR-2: `ScanPackingOrderHandler` uses the shared collaborator for its create-shipment path
`ScanPackingOrderHandler.Handle` is refactored to call `IShipmentCreationService` for the "no existing shipment, eligible order" branch (current lines ~115–191), instead of its own inline logic.

**Acceptance criteria:**
- All behavior outside the extracted block is unchanged: eligibility check and non-eligible-order early return, existing-shipment reprint/backfill path (`BackfillExistingShipmentPackagesAsync`), packer-eligibility check (`ErrorCodes.PackingUserNotEligible` — see FR-4), deferred `TryMarkAsPackedAsync` semantics, and the `PendingCompletion = true` response flag.
- Existing `ScanPackingOrderHandlerTests.cs` behavior (all currently-passing cases) continues to pass after the refactor, updated only where the refactor changes internal collaborators, not observable behavior.
- `ScanPackingOrderResponse` and `ScanPackingOrderRequest` DTOs are unchanged — this is an internal refactor, not a contract change.

### FR-3: `ResetOrderShipmentHandler` uses the shared collaborator and now persists `Package` rows (bug fix)
`ResetOrderShipmentHandler.Handle` is refactored to call `IShipmentCreationService` after cancelling the prior shipment(s) and fetching the order, instead of its own inline duplicate of the create-shipment block (current lines 62–122).

**Acceptance criteria:**
- After a successful reset, `IPackageRepository.ReplacePackagesForOrderAsync` is called for the order with one `Package` row per requested package (`request.NumberOfPackages`), each row's `ShipmentGuid` equal to the **new** shipment's GUID (not any of the cancelled ones).
- The stale `Package` rows left over from the cancelled shipment(s) are gone after reset — verified by `ReplacePackagesForOrderAsync`'s replace-all-for-order-code semantics (no separate delete step needed).
- `GetPackingStatisticsAsync`, `GetPackingDashboardHandler`, and `FillTrackingNumbersJob.GetWithNullTrackingNumberAsync` observe the replacement shipment's packages for a reset order in the next run/query — i.e., there is no window where a reset order has zero `Package` rows for its current shipment (aside from the atomic replace itself).
- `ResetOrderShipmentResponse` / `ResetShipmentData` / `ResetShipmentPackage` DTOs are unchanged — this is an internal refactor plus a persistence side-effect, not a contract change. The frontend (`PackingShipmentCreator.tsx`) requires no changes.
- A new regression test in `ResetOrderShipmentHandlerTests.cs` asserts `IPackageRepository.ReplacePackagesForOrderAsync` is invoked with the correct order code, package count, and shipment GUID on a successful reset — this is the primary guard against the bug recurring.

### FR-4: Packer attribution on reset
`ResetOrderShipmentRequest` currently has no `PackingUserId` field (unlike `ScanPackingOrderRequest`, which does), and the FE's `handleInvalidateAndNew` never supplies one. The shared collaborator's packer-resolution step (FR-1.7) must handle `packingUserId: null` gracefully, exactly as `ScanPackingOrderHandler.ResolvePackerAsync` does today: `PackedByUserId` is left `null` and `PackedBy` is set to `ICurrentUserService.GetCurrentUser().Email`.

**Acceptance criteria:**
- Reset-created `Package` rows have `PackedBy` set to the current authenticated user's email and `PackedByUserId` set to `null`, matching Scan's behavior when no explicit packer is passed.
- No change is made to `ResetOrderShipmentRequest`, the reset REST endpoint, or the FE reset call in this feature (see Open Questions for whether a future feature should add explicit packer selection to reset).
- If the collaborator applies the packer-eligibility gate (`ErrorCodes.PackingUserNotEligible`) from `ScanPackingOrderHandler.cs:176-181`, it must be conditioned on `packingUserId` being non-null, so that reset (which always passes `null`) is unaffected by this gate today.

### FR-5: Persistence-failure handling stays non-blocking, applied consistently to both callers
`ScanPackingOrderHandler.PersistPackagesAsync` today catches exceptions from `ReplacePackagesForOrderAsync`, logs a warning, and still returns success to the caller (the shipment was created successfully with the carrier even if the local audit row failed to save). This behavior is preserved by the shared collaborator for both callers.

**Acceptance criteria:**
- A persistence exception inside the collaborator does not cause `ScanPackingOrderHandler` or `ResetOrderShipmentHandler` to return an error code to the caller; it is logged as a warning (structured, including order code and package count) and swallowed, matching current Scan behavior.
- Both handlers' tests cover this "shipment created, persistence throws" path independently (it did not previously exist for Reset since Reset never called the repository).

### FR-6: Dependency injection wiring
`ResetOrderShipmentHandler` currently depends only on `IShipmentClient`, `IPackingOrderClient`, `IOptions<ShipmentLabelsSettings>`, and `ILogger<ResetOrderShipmentHandler>`. After the refactor it depends additionally on `IShipmentCreationService` (and loses its own duplicated weight/carrier/creation/label logic). `ScanPackingOrderHandler` keeps its existing constructor dependencies plus `IShipmentCreationService`; it may drop direct usage of `IPackageRepository` if `PersistPackagesAsync` is fully absorbed by the new collaborator — `BackfillExistingShipmentPackagesAsync` (the reprint path) still needs `IPackageRepository` directly (via `AddMissingAsync`) unless that path is also folded into the shared service (out of scope — see Out of Scope).

**Acceptance criteria:**
- `IShipmentCreationService` is registered in `backend/src/Anela.Heblo.Application/Features/Packaging/PackagingModule.cs` alongside the existing `IPackageRepository` registration (`services.AddScoped<IShipmentCreationService, ShipmentCreationService>();`).
- `dotnet build` succeeds with no unresolved DI dependencies; the app starts and both `POST` endpoints backing these handlers (in `PackagingController`) function against a local/staging environment.

## Non-Functional Requirements

### NFR-1: Performance
- No additional external calls are introduced. Each handler must still make exactly the same number of calls to `IPackingOrderClient`, `IShipmentClient`, and `IAuthorizationRepository` as it does today (e.g. `ResetOrderShipmentHandler` fetches the order once, after cancelling; the collaborator must accept an already-fetched `PackingOrder` rather than re-fetching it, to avoid adding a second `GetPackingOrderAsync` round-trip).
- `IPackageRepository.ReplacePackagesForOrderAsync` for reset is a single additional local DB write per reset call (this is the intended fix, not overhead to avoid).

### NFR-2: Security
- No new authorization surface. The packer-eligibility check (`PackingUserNotEligible`) continues to gate only on an explicitly supplied `packingUserId`, consistent with current Scan behavior.
- No secrets, PII beyond what is already stored (`CustomerName`, `PackedBy` email) are newly introduced.

### NFR-3: Testability / Maintainability
- The shared collaborator must be independently unit-testable (constructed with mocked `IShipmentClient`, `IPackageRepository`, `IAuthorizationRepository`, `ICurrentUserService`, `IOptions<ShipmentLabelsSettings>`, `ILogger`), without requiring a MediatR handler in the test.
- Both `ScanPackingOrderHandlerTests.cs` (618 lines) and `ResetOrderShipmentHandlerTests.cs` (487 lines) are updated to mock `IShipmentCreationService` where the handler-level test is only asserting handler-level orchestration (eligibility, cancel-then-create ordering, response shaping), and a new test file (e.g. `ShipmentCreationServiceTests.cs`) covers the extracted logic's own branches (invalid package count, zero-weight fallback, carrier not resolved, creation failure, label padding short of `n`, persistence failure swallowed, packer resolution with/without explicit `packingUserId`).

## Data Model
No schema changes. `Package` (`backend/src/Anela.Heblo.Domain/Features/Packaging/Package.cs`) is unchanged:

```
Package { Id, OrderCode, CustomerName, PackageNumber, TrackingNumber?, ShippingProviderCode, ShippingProviderName?, ShipmentGuid, PackedAt, PackedBy?, PackedByUserId?, CreatedAt }
```

`IPackageRepository.ReplacePackagesForOrderAsync(orderCode, packages, ct)` (`backend/src/Anela.Heblo.Domain/Features/Packaging/IPackageRepository.cs:25-28`) is the persistence entry point used by the new collaborator for both callers — its documented semantics ("delete existing rows for the order, then insert the new ones, in a single save... makes re-scanning idempotent") are exactly what's needed to also make **reset** idempotent and to clear stale rows left by the cancelled shipment.

## API / Interface Design

New collaborator interface (exact placement suggested: `Anela.Heblo.Application.Features.Packaging.Services.IShipmentCreationService` / `ShipmentCreationService`, since it depends on both `IShipmentClient` (ShipmentLabels feature) and `IPackageRepository` (Packaging domain) and is only consumed within the Packaging module — see Open Questions for an alternative placement):

```csharp
public interface IShipmentCreationService
{
    Task<ShipmentCreationResult> CreateAndPersistAsync(
        PackingOrder order,
        int numberOfPackages,
        Guid? packingUserId,
        CancellationToken ct);
}

public class ShipmentCreationResult
{
    public bool IsSuccess { get; init; }
    public ErrorCodes? ErrorCode { get; init; }          // set when IsSuccess == false
    public Guid ShipmentGuid { get; init; }
    public string CarrierCode { get; init; } = null!;
    public string? CarrierName { get; init; }
    public IReadOnlyList<ShipmentLabel> Labels { get; init; } = []; // exactly `numberOfPackages` entries, padded with nulls
}
```

Each handler maps `ShipmentCreationResult.Labels` to its own response DTO (`ScanShipmentPackage` / `ResetShipmentPackage` — structurally identical: `TrackingNumber`, `LabelUrl`, `LabelZpl`) and its own top-level response envelope (`ScanShipmentData` / `ResetShipmentData`), preserving today's per-feature response shapes and error-code returns.

No REST endpoint signatures, request/response DTOs, or the OpenAPI-generated TypeScript client change as part of this feature — this is an internal collaborator extraction plus a persistence bug fix.

## Dependencies
- `IShipmentClient` (`Anela.Heblo.Application.Features.ShipmentLabels`) — existing, unchanged.
- `IPackageRepository` (`Anela.Heblo.Domain.Features.Packaging`) — existing, unchanged; now called from the shared collaborator instead of directly from `ScanPackingOrderHandler`.
- `IAuthorizationRepository`, `ICurrentUserService` — existing, moved from `ScanPackingOrderHandler`'s direct dependencies into the shared collaborator.
- `ShipmentLabelsSettings` (`IOptions<ShipmentLabelsSettings>`) — existing, unchanged.
- `IPackingOrderClient` — remains a direct dependency of both handlers (each still needs to fetch/have the order for its own pre-checks); the collaborator receives the already-fetched `PackingOrder`, it does not fetch it itself.

## Out of Scope
- Folding `BackfillExistingShipmentPackagesAsync` (Scan's reprint/backfill-existing-shipment path, `ScanPackingOrderHandler.cs:248-295`, using `IPackageRepository.AddMissingAsync`) into the new collaborator. It is a related but distinct concern (existing shipment, not a newly-created one) and has no counterpart or bug in `ResetOrderShipmentHandler`.
- Adding explicit packer selection (`PackingUserId`) to `ResetOrderShipmentRequest` and the reset UI — see FR-4 and Open Questions.
- A one-off data-repair script/migration to backfill `Package` rows for orders that were reset before this fix shipped (their historical stats remain wrong retroactively unless separately remediated).
- Any change to `GetPackingStatisticsHandler`, `GetPackingDashboardHandler`, or `FillTrackingNumbersJob` themselves — they are expected to self-correct once reset starts producing rows, with no code changes needed.
- Changes to `#3194`'s already-closed duplicated-warning-strings finding.

## Open Questions
1. **Should a persistence failure inside the shared collaborator ever fail the *reset* request outright**, rather than being logged and swallowed (current Scan behavior, FR-5)? Given that the entire point of this feature is to guarantee reset writes `Package` rows, silently swallowing a persistence error on reset reproduces a milder version of the same bug (a shipment exists with no matching rows) instead of eliminating it. Assumption for this spec: keep swallow-and-log for both callers, for consistency and because a partially-succeeded carrier shipment should not be reported as a hard failure to the warehouse worker — but this should be confirmed with the team before implementation.
2. **Should `IShipmentCreationService` live under `Features.Packaging.Services` or under `Features.ShipmentLabels`?** It composes `IShipmentClient` (a ShipmentLabels-feature client) with `IPackageRepository` (a Packaging-domain repository). This spec assumes `Features.Packaging.Services` since the collaborator's primary responsibility (and its bug fix) is about `Package` persistence, but the architect may prefer the other module boundary per `docs/architecture/development_guidelines.md`.
3. **Should reset gain explicit packer selection** (mirroring Scan's `PackingUserId` + eligibility gate) as a follow-up, so `PackedByUserId` is properly attributed for resets performed on behalf of a specific packer, rather than always falling back to the current logged-in user? Not required for this fix; flagged for product input.

## Status: HAS_QUESTIONS
