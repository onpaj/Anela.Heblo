# Architecture Review: Extract shared shipment-creation logic (ScanPackingOrder / ResetOrderShipment)

## Skip Design: true

Pure backend refactor + bug fix. `ScanPackingOrderResponse`, `ResetOrderShipmentResponse`, and every DTO field are explicitly unchanged (FR-2/FR-3 acceptance criteria), and `PackingShipmentCreator.tsx` needs no changes. No new/changed UI surface.

## Architectural Fit Assessment

This fits the codebase's existing conventions cleanly:

- **Vertical Slice / `Services/` folder**: `docs/architecture/filesystem.md` names `Features/{Feature}/Services/` as the designated home for "domain services and business logic" inside a feature. Extracting `IShipmentCreationService`/`ShipmentCreationService` into `Packaging/Services/` is exactly this pattern — no new architectural concept needed.
- **Module coupling direction is already decided, in Packaging's favor**: `Packaging` today imports `ShipmentLabels` types (`IShipmentClient`, `ShipmentLabel`, `CreateShipmentCommand`, `CreatedShipment`, `ShippingOption`, `ShipmentLabelsSettings`) directly, with no contract/adapter indirection. I verified this against `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`: there is a tightly-allowlisted `"Packaging -> ShoptetOrders"` rule (every legal reference enumerated by name) but **no `"Packaging -> ShipmentLabels"` rule exists at all** — that coupling direction is unguarded/accepted. `ShipmentLabels`, by contrast, has zero knowledge of `Packaging` or `IPackageRepository` anywhere in its code. This settles the spec's Open Question 2: the collaborator must live in **Packaging**, not `ShipmentLabels` — putting it in `ShipmentLabels` would force that module to newly depend on `IPackageRepository` (a `Packaging`-domain-owned interface), inverting an established, deliberate boundary and very likely tripping `ModuleBoundariesTests` in the wrong direction (`ShipmentLabels -> Packaging` has no precedent and none of `ShipmentLabels`' existing allowlists cover it).
- **DI binding placement**: per ADR-004, repository/service bindings live in the owning module's `{Feature}Module.cs`, never centrally. `PackagingModule.AddPackagingModule()` is the correct and only place to register `IShipmentCreationService`.
- **DTOs remain classes**: `ScanShipmentPackage`, `ResetShipmentPackage`, `ScanPackingOrderResponse`, etc. are already classes (not records), consistent with the project-wide DTO rule; `ShipmentCreationResult` (an internal collaborator-boundary type, not an OpenAPI-exposed DTO) should follow the same convention for consistency even though it's never serialized to the client.
- **User identity resolution (ADR-005)**: today `ICurrentUserService` is resolved inside `ScanPackingOrderHandler` (a MediatR handler), which is correct per ADR-005. Moving that resolution into `ShipmentCreationService` is **not** a handler, so it's worth being explicit: ADR-005's rule ("resolve inside the handler, never in a controller") is about controllers vs. handlers, not about a handler's application-service collaborators. `ShipmentCreationService` is invoked *from* handlers and is itself part of the application layer, so injecting `ICurrentUserService` into it does not violate ADR-005 — but it does mean `ICurrentUserService` moves one level down the call stack. No architectural objection; call this out explicitly in code review of the implementation PR so nobody mistakes it for a violation.

## Proposed Architecture

### Component Overview

```
ScanPackingOrderHandler                  ResetOrderShipmentHandler
   |  (own: eligibility check,              |  (own: cancel old shipment(s),
   |   existing-shipment reprint path,       |   NoShipmentToReset / ShipmentCancelFailed,
   |   PendingCompletion=true, backfill)     |   PendingCompletion = n>=2)
   |                                         |
   |     order = await orderClient.GetPackingOrderAsync(...)   (each handler fetches its own order)
   |                                         |
   +-------------------+--------------------+
                        |
                        v
            IShipmentCreationService.CreateAndPersistAsync(
                order, numberOfPackages, packingUserId, ct)
                        |
                        v
        ShipmentCreationService  (Packaging/Services/)
          1. validate package count (1..10)
          2. compute total/per-package weight (+ zero-weight fallback)
          3. IShipmentClient.GetShippingOptionsAsync -> carrier
          4. IShipmentClient.CreateShipmentAsync -> CreatedShipment
          5. IShipmentClient.GetLabelsByOrderCodeAsync -> filter to
             this shipment's Guid -> pad to n
          6. packer eligibility gate (if packingUserId given) +
             packer resolution (single IAuthorizationRepository call)
          7. IPackageRepository.ReplacePackagesForOrderAsync
             (n rows, swallow+log on failure)
                        |
                        v
            ShipmentCreationResult (ShipmentGuid, CarrierCode/Name,
                                     Labels[n], IsSuccess/ErrorCode)
                        |
        +---------------+----------------+
        v                                v
ScanPackingOrderHandler maps       ResetOrderShipmentHandler maps
-> ScanShipmentData/                -> ResetShipmentData/
   ScanShipmentPackage                 ResetShipmentPackage
```

`ShipmentCreationService` depends on `IShipmentClient` (ShipmentLabels), `IPackageRepository` (Packaging/Domain), `IAuthorizationRepository` (Domain/Authorization), `ICurrentUserService` (Domain/Users), `IOptions<ShipmentLabelsSettings>`, `ILogger<ShipmentCreationService>` — all already-existing interfaces; nothing new is introduced into the dependency graph, only re-wired.

### Key Design Decisions

#### Decision 1: Module placement — `Anela.Heblo.Application.Features.Packaging.Services`
**Options considered:** (a) `Packaging.Services` (spec's default assumption), (b) `Features.ShipmentLabels`.
**Chosen approach:** (a) `Packaging.Services`.
**Rationale:** Confirmed against `ModuleBoundariesTests.cs` — Packaging already couples to ShipmentLabels types unguarded; ShipmentLabels has no reverse dependency on Packaging today. The collaborator's defining responsibility (and the entire reason this feature exists) is correct `Package` persistence, which is Packaging-domain-owned. Placing it in ShipmentLabels would be adding a new, backwards cross-module dependency with no existing precedent to lean on. This closes Open Question 2 — no further architect sign-off needed before implementation.

#### Decision 2: Persistence-failure handling — swallow and log, for both callers, unchanged from current Scan behavior
**Options considered:** (a) keep swallow-and-log (spec's assumption), (b) fail the whole request (return an error code) when `ReplacePackagesForOrderAsync` throws, (c) swallow for Scan (as today) but hard-fail for Reset (since Reset's core purpose here is fixing exactly this gap).
**Chosen approach:** (a) swallow-and-log for both, with one addition: the warning log must carry `OrderCode`, `ShipmentGuid`, and `PackageCount` as structured fields (not just `OrderCode` as today), since this is now the single place both flows fail through and the log is the only signal an ops/on-call person has to catch recurrence.
**Rationale:** By the time `ReplacePackagesForOrderAsync` is reached, the carrier shipment has already been created and (for Reset) the old one already cancelled — the operation the warehouse worker cares about (a valid printable label) has already succeeded. Returning a hard error at that point would tell the packer "this failed" when it didn't; they'd retry, potentially creating a second carrier shipment for the same order. Option (c) was rejected for asymmetry: identical failure, different user-facing behavior between Scan and Reset, is exactly the kind of drift this feature exists to eliminate — and a swallowed-and-logged persistence failure on Reset is a **strict improvement** over today (today Reset doesn't even attempt persistence), not a regression to accept begrudgingly. This closes Open Question 1. Ops follow-up (not blocking, not in scope): if this log line ever fires, it's worth an alert — but that's an observability backlog item, not an architectural blocker.

#### Decision 3: Packer attribution on reset — no request/DTO change in this feature
**Options considered:** (a) leave `ResetOrderShipmentRequest` as-is, always resolving to `ICurrentUserService`'s email when persisting (spec's assumption), (b) add `PackingUserId` to `ResetOrderShipmentRequest` + FE now in this feature.
**Chosen approach:** (a).
**Rationale:** This is a product/UX decision (does resetting a shipment on behalf of a specific packer need attribution?), not an architectural one, and the spec already scoped it out (FR-4, Out of Scope). Architecturally, the collaborator's `packingUserId: Guid?` parameter already supports this cleanly for a future feature — `ResetOrderShipmentHandler` just needs to start passing a real value instead of always passing `null`, with zero changes to `ShipmentCreationService` itself. This closes Open Question 3: no architectural prerequisite work needed now; flag it as a candidate for a future, separately-briefed feature.

#### Decision 4 (new, not in spec's open questions): the collaborator must filter fetched labels by the *new* shipment's GUID before padding
**Finding from code, not from the spec:** `ScanPackingOrderHandler` (line 162) takes `GetLabelsByOrderCodeAsync`'s result directly and pads it — safe *only* because Scan's create-path runs solely when no shipment exists yet for the order, so every label returned necessarily belongs to the new shipment. `ResetOrderShipmentHandler` (lines 107-110) does the same fetch but explicitly filters first: `newLabels.Where(l => l.ShipmentGuid == createdShipment.ShipmentGuid)` — because Reset just cancelled one or more prior shipments for the same order, and `GetLabelsByOrderCodeAsync` can still return those old (now-cancelled) shipments' labels alongside the new one.
**Chosen approach:** `ShipmentCreationService` must always filter `GetLabelsByOrderCodeAsync`'s result to `label.ShipmentGuid == createdShipment.ShipmentGuid` before padding to `n`, i.e. adopt Reset's (correct, more defensive) version, not Scan's (currently-safe-by-accident) version.
**Rationale:** The spec's FR-1.6 describes only "re-fetching labels ... and padding to exactly `n` entries," without mentioning the filter — if an implementer models the extracted logic on Scan's literal code (the handler most people will read first, since it's the "primary" path), the collaborator will silently regress Reset: a cancelled shipment's stale label could get mapped into the *new* shipment's package list, corrupting `TrackingNumber`/`LabelUrl` on the very rows this feature is trying to make correct. This is a correctness prerequisite for FR-3, not an optional hardening — call it out explicitly to the implementer (see Specification Amendments).

#### Decision 5 (new, not in spec's open questions): persist exactly `n` rows (the padded list), not `labels.Count` rows
**Finding from code, not from the spec:** `ScanPackingOrderHandler.PersistPackagesAsync` (line 183-191) is called with `newLabels` — the **raw, unpadded** list fetched from `IShipmentClient` — not with `packages`, the padded `n`-length list built for the FE response two lines earlier (line 163-174). If Shoptet has generated fewer than `n` labels at scan time (an explicitly anticipated case — see the comment at line 157-161), `ReplacePackagesForOrderAsync` today persists only `newLabels.Count` rows, each numbered `1..count`. No row is ever created for package `count+1..n`.
**Chosen approach:** The collaborator's persistence step must build `Package` rows from the same padded (`n`-length, null-filled where no label exists yet) list it uses for `ShipmentCreationResult.Labels`, matching the spec's own FR-3 acceptance criterion ("one `Package` row per requested package (`request.NumberOfPackages`)") — and apply this uniformly to Scan too, since both callers now share one code path.
**Rationale:** This is a second, previously-undocumented instance of the same bug class the spec is fixing. `FillTrackingNumbersJob` backfills tracking numbers by querying `IPackageRepository.GetWithNullTrackingNumberAsync` — rows with `TrackingNumber == null`. A package that never got a row in the first place (today's Scan behavior when Shoptet lags) is invisible to that job forever; its tracking number, once Shoptet generates it, is never backfilled. Padding to `n` before persisting turns "row never created" into "row created with `TrackingNumber = null`," which is precisely the state `FillTrackingNumbersJob` already knows how to repair. This is a behavior change for Scan (more rows persisted in the label-lag case) but it is a strict correctness improvement consistent with the spec's own stated intent and acceptance criteria — flag it explicitly since FR-2's acceptance criteria says "all behavior outside the extracted block is unchanged," which could be misread to mean "even inside the block, only fix what's described." Make clear this row-count fix is in scope for Scan as well as Reset.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/Packaging/
├── Services/
│   ├── IShipmentCreationService.cs
│   ├── ShipmentCreationService.cs
│   └── ShipmentCreationResult.cs        # class, not record — see DTO rule
├── UseCases/
│   ├── ScanPackingOrder/
│   │   └── ScanPackingOrderHandler.cs   # refactored: calls IShipmentCreationService
│   └── ResetOrderShipment/
│       └── ResetOrderShipmentHandler.cs # refactored: calls IShipmentCreationService
└── PackagingModule.cs                   # + AddScoped<IShipmentCreationService, ShipmentCreationService>()

backend/test/Anela.Heblo.Tests/Application/Packaging/
├── ScanPackingOrderHandlerTests.cs      # trimmed to handler-only orchestration
├── ResetOrderShipmentHandlerTests.cs    # trimmed + new persistence-call assertion (FR-3)
└── ShipmentCreationServiceTests.cs      # new: full branch coverage of extracted logic
```

This matches the existing "Complex Features" pattern in `docs/architecture/filesystem.md` (`Services/` sits alongside `UseCases/` at the feature root) and mirrors how other Packaging-adjacent collaborators are already organized.

### Interfaces and Contracts

```csharp
namespace Anela.Heblo.Application.Features.Packaging.Services;

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
    public ErrorCodes? ErrorCode { get; init; }
    public Guid ShipmentGuid { get; init; }
    public string CarrierCode { get; init; } = null!;
    public string? CarrierName { get; init; }
    public IReadOnlyList<ShipmentLabel> Labels { get; init; } = [];  // exactly `numberOfPackages`, gap-padded with a null-fields entry, NOT skipped
}
```

This matches the spec's proposed shape exactly (spec's `API / Interface Design` section) — no amendment needed to the contract itself, only to what the implementation inside it must do (Decisions 4 and 5 above). Note `PackingOrder` is `ShoptetOrders`-owned; `Packaging` already legally references it (see `PackagingShoptetOrdersAllowlist` in `ModuleBoundariesTests.cs`, which explicitly names both `ScanPackingOrderHandler -> PackingOrder` and `ResetOrderShipmentHandler -> PackingOrder`). If `ShipmentCreationService`'s signature is added to the allowlist enforcement scope (it will be, since it lives under `Features.Packaging`), add `"Anela.Heblo.Application.Features.Packaging.Services.ShipmentCreationService -> Anela.Heblo.Application.Features.ShoptetOrders.PackingOrder"` to `PackagingShoptetOrdersAllowlist` or the boundary test will fail CI on the first build.

**Packer eligibility + resolution consolidation** (not required by the spec, but enabled by it and worth doing while the code is being touched): today, `ScanPackingOrderHandler.Handle` calls `_authRepo.GetUserByIdAsync(requestedPackerId, ct)` once for the `PackingUserNotEligible` gate (line 178), and `PersistPackagesAsync` → `ResolvePackerAsync` calls `_authRepo.GetUserByIdAsync(id, ct)` again for the *same* user ID to get `DisplayName` (line 241). Since both concerns move into `ShipmentCreationService` under FR-1.7/FR-4, do the lookup once and reuse the result for both the eligibility check and `PackedByUserId`/`PackedBy`. This is a reduction in `IAuthorizationRepository` calls for the packer-supplied case, not an addition — consistent with NFR-1's "no additional external calls" (fewer, not more).

### Data Flow

**Scan (create path, no existing shipment):**
`ScanPackingOrderHandler` fetches `order` once → eligibility/existing-shipment checks stay in the handler → on the "eligible, no shipment yet" branch, calls `_shipmentCreationService.CreateAndPersistAsync(order, request.NumberOfPackages, request.PackingUserId, ct)` → maps `ShipmentCreationResult` to `ScanShipmentData { AlreadyExisted = false, PendingCompletion = true }`.

**Reset:**
`ResetOrderShipmentHandler` fetches `existingLabels` → cancels each distinct prior `ShipmentGuid` → fetches `order` (unchanged) → calls `_shipmentCreationService.CreateAndPersistAsync(order, request.NumberOfPackages, null, ct)` (Reset never supplies a packer id) → maps result to `ResetShipmentData { PendingCompletion = n >= 2 }` (this per-caller flag stays outside the collaborator — it differs between the two handlers and is not part of the shared block per the spec).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Implementer models the extraction on Scan's code (unfiltered labels) and drops Reset's shipment-GUID filter | High | Decision 4 above — make the filter step explicit in the collaborator's own implementation, add a `ShipmentCreationServiceTests` case with two shipment GUIDs in the fetched labels to pin this down |
| Implementer persists `labels.Count` rows (mirroring Scan literally) instead of `n` padded rows | Medium | Decision 5 above — add a `ShipmentCreationServiceTests` case asserting `ReplacePackagesForOrderAsync` receives exactly `n` `Package` entries when fewer than `n` labels are returned |
| `ModuleBoundariesTests` breaks on first build because `ShipmentCreationService -> PackingOrder` isn't in the existing allowlist | Low | Add the one allowlist entry (see Interfaces and Contracts) in the same PR; this is a mechanical, known-pattern fix |
| Swallow-and-log on Reset's persistence failure reproduces a *milder* version of the original bug (shipment exists, no rows) and nobody notices | Low–Medium | Decision 2 above — require structured fields (`OrderCode`, `ShipmentGuid`, `PackageCount`) on the warning so it's at least query-able in logs; treat proactive alerting as a separate, non-blocking follow-up |
| `ScanPackingOrderHandlerTests`/`ResetOrderShipmentHandlerTests` (618/487 lines) have deep coverage of the now-extracted branches; naive mock-everything refactor could silently drop coverage of edge cases (zero-weight fallback, carrier-not-resolved, label padding) | Medium | Per NFR-3, those edge cases move to the new `ShipmentCreationServiceTests.cs` 1:1 — reviewer should diff removed assertions against what reappears in the new test file, not just check both files still pass |

## Specification Amendments

1. **FR-1.6 must specify the shipment-GUID filter** before padding (Decision 4). Add to FR-1's numbered list: *"6a. Filter the re-fetched labels to `label.ShipmentGuid == createdShipment.ShipmentGuid` before padding — required for correctness on Reset, where stale/cancelled-shipment labels can still be returned by `GetLabelsByOrderCodeAsync`."*
2. **FR-1.7 / FR-3's persistence step must build rows from the padded (`n`-length) list, not the raw fetched-labels list** (Decision 5). This is a Scan behavior change (more rows persisted when Shoptet lags on label generation) that the spec's FR-2 "all behavior outside the extracted block is unchanged" should not be read to prohibit — it's inside the extracted block and is required for FR-3's own acceptance criterion to hold consistently between the two callers.
3. **Packer eligibility + resolution should share one `IAuthorizationRepository.GetUserByIdAsync` call** inside the collaborator, rather than duplicating today's two separate calls (see Interfaces and Contracts). Non-blocking nice-to-have, but cheap to do while this code is already being moved.
4. **Add `PackagingShoptetOrdersAllowlist` entry** for `ShipmentCreationService -> PackingOrder` (see Interfaces and Contracts) — otherwise `ModuleBoundariesTests` fails on the first build after the refactor lands.
5. Open Questions 1–3 in the spec are resolved by Decisions 2, 1, and 3 respectively — implementation should proceed without further product/architecture sign-off on those three points.

## Prerequisites

None. No schema/migration changes, no new external dependencies, no configuration changes. This is a pure code-motion + two behavior-tightening fixes (label-GUID filtering, padded-row persistence) within already-registered, already-wired interfaces. The only mechanical prerequisite is the `ModuleBoundariesTests` allowlist addition called out above, which should land in the same PR, not as a separate step.
