# Architecture Review: User Identity Resolution Fix for GiftPackageManufactureService

## Skip Design: true

## Architectural Fit Assessment
This is a pure architecture-compliance refactor with no new capability, no UI surface, and no data-shape change — it moves a single line of user resolution from one layer to the one immediately above it. It fits squarely into an already-established, well-documented convention (ADR-005, `docs/architecture/development_guidelines.md` §"User Identity Resolution"), not a new pattern being introduced.

Verified against the real code:
- `GiftPackageManufactureService.cs` (lines 18, 28, 155, 236) injects `ICurrentUserService` and calls `GetCurrentUser().Name ?? "System"` twice, exactly as the spec describes.
- `IGiftPackageManufactureService.cs` declares `CreateManufactureAsync(string, int, bool, CancellationToken)` and `DisassembleGiftPackageAsync(string, int, CancellationToken)` — no `userName` parameter today.
- `CreateGiftPackageManufactureHandler.cs` and `DisassembleGiftPackageHandler.cs` both take only `IGiftPackageManufactureService` in their constructors — identity resolution is completely absent at the handler layer today, confirming the spec's "handlers do nothing with identity" claim.
- `CreateGiftPackageManufactureRequest`/`DisassembleGiftPackageRequest` are plain classes (already compliant with this repo's "DTOs are classes, never records" rule) with no `UserId`/`ModifiedBy` field — no spoofing hole exists or is being introduced.
- The established correct pattern is `CreateNewTransportBoxHandler.cs`: it injects `ICurrentUserService`, calls `_currentUserService.GetCurrentUser()` once at the top of `Handle()`, and uses the resolved `CurrentUser` (`.Name`, `.Id` via `Guid.TryParse`) to build the domain/log data — never passing the service interface itself downstream. This is the exact shape the spec proposes for both `GiftPackageManufacture` handlers, so no new integration pattern needs to be invented.
- `ModuleBoundariesTests.cs` contains no rule today that would have caught `ICurrentUserService` inside `Anela.Heblo.Application`'s services — confirming the spec's Out-of-Scope note that this went undetected until the daily arch-review flagged it manually.
- `GiftPackageManufactureModule.cs` registers only the repository and `IGiftPackageManufactureService` — `ICurrentUserService` DI wiring is entirely `UsersModule`'s concern and needs zero changes.

There is exactly one reasonable design here (resolve-in-handler, pass-string-into-service) and it's already the codebase's dominant convention (60+ handlers per ADR-005's own changelog). No alternative is worth presenting.

## Proposed Architecture

### Component Overview
```
Before:
  CreateGiftPackageManufactureHandler ──(no identity)──▶ IGiftPackageManufactureService
  DisassembleGiftPackageHandler       ──(no identity)──▶       │
                                                                ▼
                                          GiftPackageManufactureService
                                          ├─ ICurrentUserService.GetCurrentUser()  ⚠ ADR-005 violation
                                          └─ builds GiftPackageManufactureLog(..., userName, ...)

After:
  CreateGiftPackageManufactureHandler ──ICurrentUserService.GetCurrentUser()──┐
                                                                               ▼
                                       CreateManufactureAsync(..., userName, ct) ──▶ GiftPackageManufactureService
                                                                                       └─ builds Log(..., userName, ...)

  DisassembleGiftPackageHandler ──ICurrentUserService.GetCurrentUser()──┐
                                                                          ▼
                                  DisassembleGiftPackageAsync(..., userName, ct) ──▶ GiftPackageManufactureService
                                                                                       └─ builds Log(..., userName, ...)
```
`GiftPackageManufactureService` becomes identity-agnostic: it receives a plain `string` and has no knowledge of `IHttpContextAccessor`, HTTP requests, or `ICurrentUserService` at all — restoring its testability and its eligibility for non-HTTP callers (e.g. a future Hangfire job) without any further change.

### Key Design Decisions

#### Decision 1: Resolve-in-handler, pass-primitive-into-service (vs. alternatives)
**Options considered:**
1. Resolve `ICurrentUserService.GetCurrentUser()` in each handler, pass the resulting `string userName` into the service methods (spec's proposal).
2. Pass the full `CurrentUser` record into the service instead of just the name.
3. Leave the service as-is and instead have `ICurrentUserService` be safely no-op outside HTTP context (e.g. return `"System"` when no `HttpContext` exists), so the service could stay untouched.

**Chosen approach:** Option 1, exactly as specified.

**Rationale:**
- ADR-005 is explicit and already has 60+ handler-side precedents (e.g. `CreateNewTransportBoxHandler`) — this is not a judgment call, it's applying an existing rule to a class that slipped through.
- Option 2 (pass `CurrentUser`) is rejected: the service only ever needs `.Name` (for `CreatedBy`), and every other ADR-005-compliant handler in the codebase resolves `.Name`/`.Id` locally and passes primitives downstream, not the `CurrentUser` object itself. Passing the whole record would leak an identity type into the Application service's public contract for no benefit and slightly widen its surface area unnecessarily.
- Option 3 is rejected outright: it treats the symptom (service breaks outside HTTP context) rather than the cause (a service should never own identity resolution), and it directly contradicts ADR-005's stated rationale ("hides a web-context dependency behind an Application layer abstraction").

#### Decision 2: Parameter position — `userName` before `cancellationToken`, after the last business parameter
**Options considered:** Append `userName` as the very last parameter (before `CancellationToken`, which is idiomatic to keep last) vs. insert it earlier in the parameter list vs. wrap parameters into a request object.

**Chosen approach:** Insert `userName` immediately after the last existing business parameter (`allowStockOverride` / `quantity`) and immediately before `cancellationToken`, per the spec's signatures.

**Rationale:** Matches the codebase-wide convention of `CancellationToken cancellationToken = default` always being the trailing parameter. No existing convention in this codebase wraps 3-4 scalar parameters into a request object for internal Application services (that pattern is reserved for MediatR `IRequest` DTOs at the handler boundary), so introducing one here would be inconsistent with `IGiftPackageManufactureService`'s own existing style (`GetAvailableGiftPackagesAsync`, `GetGiftPackageDetailAsync` already use flat scalar parameter lists).

#### Decision 3: `[DisplayName(...)]` attributes are left untouched
**Options considered:** Update the positional placeholders in `[DisplayName("GiftPackageManufacture-{0}-{1}")]` / `"GiftPackageManufacture-{0}-{1}x"` to account for the new parameter, vs. leave them as-is.

**Chosen approach:** Leave as-is, per spec.

**Rationale:** `{0}`/`{1}` are positional and refer to `giftPackageCode`/`quantity`, the first two parameters — inserting `userName` after `allowStockOverride` (3rd parameter) does not shift the indices these placeholders reference. Confirmed by reading both the interface and implementation attribute lines directly.

## Implementation Guidance

### Directory / Module Structure
No new files, no new folders, no `GiftPackageManufactureModule.cs` changes. All edits are in-place in existing files:

```
backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/
├── Services/
│   ├── GiftPackageManufactureService.cs        (edit: remove ICurrentUserService field/ctor param/import; add userName params)
│   └── IGiftPackageManufactureService.cs       (edit: add userName params to both method signatures)
├── UseCases/
│   ├── CreateGiftPackageManufacture/
│   │   └── CreateGiftPackageManufactureHandler.cs   (edit: inject ICurrentUserService, resolve user, pass userName)
│   └── DisassembleGiftPackage/
│       └── DisassembleGiftPackageHandler.cs         (edit: inject ICurrentUserService, resolve user, pass userName)

backend/test/Anela.Heblo.Tests/
├── Features/Logistics/GiftPackageManufactureServiceTests.cs           (edit: drop ICurrentUserService mock/ctor arg; pass literal userName to Act calls)
├── Application/GiftPackageManufacture/
│   ├── DisassembleGiftPackageHandlerTests.cs                          (edit: add ICurrentUserService mock to CreateSut(); update Setup/Verify signatures)
│   └── CreateGiftPackageManufactureHandlerTests.cs                    (NEW — none exists today; mirror DisassembleGiftPackageHandlerTests.cs's structure)
```

### Interfaces and Contracts
`IGiftPackageManufactureService` — the only interface contract change:

```csharp
Task<GiftPackageManufactureDto> CreateManufactureAsync(
    string giftPackageCode,
    int quantity,
    bool allowStockOverride,
    string userName,
    CancellationToken cancellationToken = default);

Task<GiftPackageDisassemblyDto> DisassembleGiftPackageAsync(
    string giftPackageCode,
    int quantity,
    string userName,
    CancellationToken cancellationToken = default);
```

Both handler constructors gain one parameter, following the exact `CreateNewTransportBoxHandler` shape:

```csharp
public CreateGiftPackageManufactureHandler(
    IGiftPackageManufactureService giftPackageService,
    ICurrentUserService currentUserService)
{
    _giftPackageService = giftPackageService;
    _currentUserService = currentUserService;
}
```

No change to `CreateGiftPackageManufactureRequest`/`Response`, `DisassembleGiftPackageRequest`/`Response`, any controller, or the generated OpenAPI/TypeScript client — this is entirely internal to the Application layer.

### Data Flow
1. HTTP request arrives at the (unchanged) controller and is dispatched via MediatR to the handler.
2. Handler calls `_currentUserService.GetCurrentUser()` exactly once, at the top of `Handle()` — before any service call, matching `CreateNewTransportBoxHandler`'s placement.
3. Handler passes `user.Name ?? "System"` as the new trailing-before-`cancellationToken` `userName` argument into the service call.
4. `GiftPackageManufactureService` uses the received `userName` directly when constructing `GiftPackageManufactureLog` — no fallback logic remains inside the service (the `?? "System"` null-coalescing moves to the handler, per FR-2's acceptance criteria).
5. `DisassembleGiftPackageHandler` keeps its existing `try`/`catch` (`InvalidOperationException`, `ArgumentException`) unchanged; resolving identity before the `try` block (or inside it — either is safe since `GetCurrentUser()` cannot itself throw these types) is fine, but resolve it before the service call regardless.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Interface signature change breaks any untracked caller of `IGiftPackageManufactureService` | Low | Spec's NFR-3 confirms (and this review's grep against `backend/src` corroborates) only the two target handlers consume this interface; both are updated in the same PR. |
| Test suite drift — three existing test files reference old signatures and will fail to compile until updated | Medium | FR-5 already scopes exactly which three files need edits; treat "does the solution build and `dotnet test` pass" as the acceptance gate, not just the production code. |
| `[DisplayName]` positional placeholders silently become wrong if a future change reorders parameters | Low | No action needed now (indices are unaffected by this change) — flag as a latent fragility for whoever next touches this signature, not something to fix here (Out of Scope per spec). |
| Missing handler-level test for `CreateGiftPackageManufactureHandler` (none exists today) leaves the new identity-resolution wiring unverified | Medium | FR-5 already calls for adding one, mirroring `DisassembleGiftPackageHandlerTests.cs`'s structure — treat this as a hard requirement of the PR, not optional cleanup. |
| `ModuleBoundariesTests.cs` still has no automated rule to catch a recurrence of this violation | Low (accepted) | Explicitly out of scope per spec; note it in Prerequisites/follow-ups below so it isn't lost. |

## Specification Amendments
None. The spec (`spec.r1.md`) is architecturally sound, already grounded in the real code (line numbers and signatures verified accurate), and correctly scopes the fix to exactly the layer boundary ADR-005 defines. No changes are needed before implementation.

One clarification worth stating explicitly for the implementer: resolve `_currentUserService.GetCurrentUser()` **once** per `Handle()` call in each handler (not per-service-call) — both handlers only call the service once, so this is naturally satisfied, but it's worth calling out since FR-4's acceptance criteria specifically require "exactly once per `Handle()` invocation."

## Prerequisites
None. No migrations, no config, no new infrastructure, no DI registration changes — `ICurrentUserService` is already registered in `UsersModule.AddUsersModule()` and available to any handler via standard constructor injection. Implementation can start immediately.

Recommended (not blocking) follow-up, consistent with the spec's Out of Scope: extend `ModuleBoundariesTests.cs` with a rule that fails CI if any type under `Anela.Heblo.Application/**/Services/` takes an `ICurrentUserService` constructor dependency, so this class of violation is caught by CI instead of the manual daily arch-review routine. Not required for this PR.
