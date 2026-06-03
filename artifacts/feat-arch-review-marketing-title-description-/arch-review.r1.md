# Architecture Review: Centralize MarketingAction scalar field updates via domain method

## Skip Design: true

## Architectural Fit Assessment

The change is a textbook "Tell, Don't Ask" refinement and aligns cleanly with the codebase's stated principle "Don't create anemic domain models — put behavior in entities" (`docs/architecture/development_guidelines.md`, line 240). `MarketingAction` already encapsulates `AssociateWithProduct`, `LinkToFolder`, `SoftDelete`, `MarkOutlookSynced`, and `ClearOutlookLink` — `UpdateDetails` is the missing scalar-mutation peer that completes the entity's behavioral surface.

Integration points are **scoped to a single module** (`Anela.Heblo.Domain/Features/Marketing/`, `Anela.Heblo.Application/Features/Marketing/UseCases/`) and a single persistence configuration (`Anela.Heblo.Persistence/Marketing/MarketingActionConfiguration.cs`). No module boundaries are crossed, no contracts (`Anela.Heblo.Application/Features/Marketing/Contracts`) change, and the OpenAPI surface is identical. Per the project's coding-style rule "use `class` for entities with identity and lifecycle" and "do not mutate input models in-place," tightening the setters and adding a behavior method matches the prescribed pattern.

The Vertical Slice organization is preserved — all touched files live within the existing Marketing slice.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────┐
│  Application Layer  (Features/Marketing/UseCases)            │
│                                                              │
│  CreateMarketingActionHandler ─┐                             │
│  UpdateMarketingActionHandler ─┼──► MarketingAction          │
│  OutlookEventImportMapper      │     (Domain Layer)          │
│    .BuildAction               ─┤                             │
│    .ApplyChanges              ─┘                             │
│    .HasChanges  ──► uses shared normalizer helpers           │
└──────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌──────────────────────────────────────────────────────────────┐
│  Domain Layer  (Domain/Features/Marketing/MarketingAction)   │
│                                                              │
│  ctor(title, description, type, start, end, createdByUserId, │
│       createdByUsername, utcNow)         ── normalizes ──┐   │
│  UpdateDetails(title, description, type, start, end,     │   │
│                modifiedByUserId, modifiedByUsername,     │   │
│                utcNow)                   ── normalizes ──┤   │
│                                                          ▼   │
│  private static NormalizeTitle(string?)   ──► trim, default  │
│  private static NormalizeDescription(string?) ──► trim, null │
│                                                              │
│  Property setters: private set (EF backing-field compatible) │
│  Parameterless ctor: private (for EF materialization)        │
└──────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Constructor + UpdateDetails (not "construct-then-update")

**Options considered:**
- **A. Parameterized constructor for creation, `UpdateDetails` for mutation.** Both delegate to private static normalizers.
- **B. Parameterless constructor with defaults, immediately followed by `UpdateDetails` everywhere.**
- **C. Single static factory `Create(...)` plus `UpdateDetails`.**

**Chosen approach:** **A.** A `public MarketingAction(string title, string? description, MarketingActionType actionType, DateTime startDate, DateTime? endDate, string createdByUserId, string? createdByUsername, DateTime utcNow)` constructor for the create paths (`CreateMarketingActionHandler`, `OutlookEventImportMapper.BuildAction`); `UpdateDetails(...)` for mutation paths (`UpdateMarketingActionHandler`, `OutlookEventImportMapper.ApplyChanges`). Both call the same private `NormalizeTitle` / `NormalizeDescription` helpers so trimming rules live in exactly one place.

**Rationale:**
- Approach B leaves a transient invalid state where `Title` is `null!` between construction and `UpdateDetails` — an anti-pattern that the spec's NFR-4 (encapsulation) is meant to prevent.
- Approach C is functionally equivalent to A but adds a non-idiomatic factory layer; the codebase has no precedent for static `Create` factories on entities (`Article`, other Domain entities use ctors or property init).
- Approach A matches the C# coding-style rule "prefer init setters, constructor parameters … for shared state." The created entity is valid from the first reference.

#### Decision 2: `private set` on tightened properties, private parameterless ctor for EF

**Options considered:** `private set` vs `internal set`; expose protected parameterless ctor vs rely on EF's backing-field discovery.

**Chosen approach:** All eight properties enumerated in FR-1 (`Title`, `Description`, `ActionType`, `StartDate`, `EndDate`, `ModifiedAt`, `ModifiedByUserId`, `ModifiedByUsername`) become `{ get; private set; }`. Add a `private MarketingAction() { }` parameterless constructor for EF Core materialization.

**Rationale:**
- `private set` is the strongest narrowing that EF Core 8 still supports via its private-setter discovery, requiring no entity-configuration changes in `MarketingActionConfiguration.cs`.
- `internal` would leak mutation back to the Application layer, defeating the purpose.
- A private parameterless ctor is the safest pattern — EF Core can rehydrate, the application layer cannot accidentally bypass invariants by calling `new MarketingAction()` and then mutating.
- The `[Required]` data annotation on `Title` (currently `null!`) becomes redundant — keep it for documentation, but constructor enforcement is now primary.

#### Decision 3: Update `HasChanges` to apply normalization before comparison

**Options considered:** Leave `HasChanges` untouched; or update it to compare normalized values.

**Chosen approach:** Update `OutlookEventImportMapper.HasChanges` (lines 50–57) to compare normalized values — either route through a private `NormalizedTitle(evt)` helper or trim inline before comparison.

**Rationale:**
- **This is a correctness requirement the spec missed.** After FR-1, persisted `existing.Title` is trimmed. `ParseTitle(evt.Subject)` returns the raw (possibly-whitespace) value. The current comparison `existing.Title != ParseTitle(evt.Subject)` would then report changes on every re-import of any whitespace-bearing event, causing infinite "Updated" loops on every sync. Without this fix, the change introduces a regression that the existing test `Handle_WhenEventAlreadyImportedAndUnchanged_SkipsIt` would not catch (it uses a no-whitespace title).
- Trim-then-compare is the smallest fix; routing through `MarketingAction`'s normalizer would require exposing the normalizer as `internal static`, which is acceptable but heavier.

#### Decision 4: Handle `currentUser.Id` nullability at the call site, not in `UpdateDetails`

**Options considered:** Make `modifiedByUserId` nullable in `UpdateDetails`; or guard at the call site.

**Chosen approach:** Keep `UpdateDetails`'s `modifiedByUserId` as `string` (non-null) per FR-1. Each handler is already responsible for resolving the current user — `UpdateMarketingActionHandler` short-circuits with `UnauthorizedMarketingAccess` before reaching the entity (line 46–50), and `OutlookEventImportMapper.ApplyChanges` should pass a guaranteed non-null id (use `currentUser.Id ?? throw` or the system-sync user id as the existing convention demands).

**Rationale:** Pushing nullability into the domain method weakens the invariant. The entity should refuse to record a modification by an unidentified actor; the application layer owns user resolution.

## Implementation Guidance

### Directory / Module Structure

No new files in production code. Edits only:

```
backend/src/
├── Anela.Heblo.Domain/Features/Marketing/
│   └── MarketingAction.cs                                       (modify)
└── Anela.Heblo.Application/Features/Marketing/UseCases/
    ├── CreateMarketingAction/CreateMarketingActionHandler.cs    (modify)
    ├── UpdateMarketingAction/UpdateMarketingActionHandler.cs    (modify)
    └── ImportFromOutlook/OutlookEventImportMapper.cs            (modify — BuildAction, ApplyChanges, AND HasChanges)

backend/test/Anela.Heblo.Tests/
├── Domain/Marketing/
│   └── MarketingActionUpdateDetailsTests.cs                     (new — FR-6)
└── Features/Marketing/
    └── ImportFromOutlookHandlerTests.cs                         (extend — FR-7)
```

**Test fixture refactor (mandatory side-effect)**: Eight test files currently construct `new MarketingAction { Title = ..., ... }` via object initializer. They must be updated to the new constructor. Recommend introducing a single test helper to absorb the change:

```
backend/test/Anela.Heblo.Tests/
└── Domain/Marketing/MarketingActionTestBuilder.cs               (new — internal test helper)
```

This avoids touching ~15 test setup sites individually whenever the constructor signature evolves.

### Interfaces and Contracts

```csharp
namespace Anela.Heblo.Domain.Features.Marketing;

public class MarketingAction : IEntity<int>
{
    private MarketingAction() { } // EF Core

    public MarketingAction(
        string title,
        string? description,
        MarketingActionType actionType,
        DateTime startDate,
        DateTime? endDate,
        string createdByUserId,
        string? createdByUsername,
        DateTime utcNow);

    public void UpdateDetails(
        string title,
        string? description,
        MarketingActionType actionType,
        DateTime startDate,
        DateTime? endDate,
        string modifiedByUserId,
        string? modifiedByUsername,
        DateTime utcNow);

    // Tightened: get; private set;
    public string Title { get; private set; }
    public string? Description { get; private set; }
    public MarketingActionType ActionType { get; private set; }
    public DateTime StartDate { get; private set; }
    public DateTime? EndDate { get; private set; }
    public DateTime ModifiedAt { get; private set; }
    public string? ModifiedByUserId { get; private set; }
    public string? ModifiedByUsername { get; private set; }

    // Unchanged accessibility (out of FR-1 scope):
    // Id, CreatedAt, CreatedByUserId, CreatedByUsername, IsDeleted,
    // DeletedAt/By*, Outlook* fields — keep existing setters
}
```

**Private static normalizers** (single source of truth — used by both ctor and `UpdateDetails`):

```csharp
private static string NormalizeTitle(string? raw) => (raw ?? string.Empty).Trim();
private static string? NormalizeDescription(string? raw) => raw?.Trim();
```

**Updated `HasChanges` contract** (spec amendment — see below):

```csharp
internal static bool HasChanges(MarketingAction existing, OutlookEventDto evt, MarketingActionType actionType)
{
    // Compare normalized values to match what UpdateDetails will persist.
    var normalizedTitle = ParseTitle(evt.Subject).Trim();
    var normalizedDescription = ParseDescription(evt.BodyText)?.Trim();
    return existing.Title != normalizedTitle
        || existing.Description != normalizedDescription
        || existing.StartDate != evt.StartUtc
        || existing.EndDate != ParseEndDate(evt)
        || existing.ActionType != actionType;
}
```

### Data Flow

**Create path (API):**
```
HTTP POST → CreateMarketingActionRequest → CreateMarketingActionHandler.Handle
  → new MarketingAction(request.Title, request.Description, …, currentUser.Id, currentUser.Name, now)
    └─ ctor normalizes Title/Description via NormalizeTitle/NormalizeDescription
  → action.AssociateWithProduct(...)  (unchanged)
  → action.LinkToFolder(...)          (unchanged)
  → _outlookSync.CreateEventAsync     (unchanged)
  → action.MarkOutlookSynced(...)     (unchanged)
  → repository.AddAsync + SaveChangesAsync
```

**Update path (API):**
```
HTTP PUT → UpdateMarketingActionRequest → UpdateMarketingActionHandler.Handle
  → repository.GetByIdAsync
  → action.UpdateDetails(request.Title, request.Description, …, currentUser.Id, currentUser.Name, now)
    └─ method normalizes Title/Description
  → outlook sync (unchanged)
  → action.ProductAssociations.Clear() + AssociateWithProduct (unchanged)
  → action.FolderLinks.Clear() + LinkToFolder (unchanged)
  → repository.UpdateAsync + SaveChangesAsync
```

**Outlook import (new event):**
```
ImportFromOutlookHandler → OutlookEventImportMapper.BuildAction
  → new MarketingAction(ParseTitle(evt.Subject), ParseDescription(evt.BodyText), …,
                        currentUser.Id, currentUser.Name, utcNow)
    └─ ctor trims title (intentional behavior change)
  → action.MarkOutlookSynced(evt.Id, utcNow)
```

**Outlook import (update):**
```
ImportFromOutlookHandler
  → HasChanges(existing, evt, actionType)   ◄── now compares normalized values
  → if changed: OutlookEventImportMapper.ApplyChanges
       → existing.UpdateDetails(ParseTitle(evt.Subject), ParseDescription(evt.BodyText), …,
                                currentUser.Id, currentUser.Name, utcNow)
         └─ method trims title (intentional behavior change)
       → existing.MarkOutlookSynced(evt.Id, utcNow)
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `HasChanges` returns true on every re-import of whitespace-bearing events, creating sync churn & noisy "Updated" counts | **CRITICAL** | Spec amendment SA-1: update `HasChanges` to compare normalized values. Add regression test: import same whitespace-titled event twice, second pass must be `Skipped`. |
| `currentUser.Id` is `string?`; passing null to a non-null `UpdateDetails` parameter throws NRE in Outlook path | **HIGH** | At call sites, guard with `?? throw` or fall back to the established sync-user id. Document in mapper that a non-null id is required before calling `ApplyChanges`. |
| EF Core fails to materialize entities after `private set` tightening if backing-field convention is misconfigured | **MEDIUM** | Add an integration test (or `dotnet ef migrations` smoke run) that round-trips a `MarketingAction` through the real `ApplicationDbContext`. Private setters are EF-supported, but verify. |
| ~8 test files use object-initializer construction; signature change ripples broadly | **MEDIUM** | Introduce `MarketingActionTestBuilder` once; migrate test setups to it. Limits future churn when the entity ctor evolves. |
| `SoftDelete` (lines 106–115 in entity) directly assigns `ModifiedAt`, `ModifiedByUserId`, `ModifiedByUsername` — within the entity, so it still compiles, but the audit-stamp logic now lives in two places | **LOW** | Out of scope per spec, but flag in code comment or follow-up: `SoftDelete` should delegate to a private `StampModification(...)` helper to keep audit assignment DRY. |
| `Title`'s `[Required]` and `[MaxLength(200)]` annotations no longer match new ctor reality (no length enforcement in ctor) | **LOW** | Behavior unchanged from current code — neither current nor proposed code length-validates `Title`; only EF/database enforces. Leave annotations; the `Outlook` path's `ParseTitle` clips to 200 and the API path is bounded by `MaxLength` on the request DTO. |

## Specification Amendments

**SA-1 (CRITICAL — add to FR-3):** `OutlookEventImportMapper.HasChanges` (lines 50–57) **must** be updated in the same change to compare normalized values, otherwise re-syncing any whitespace-bearing event reports it as changed and triggers a no-op write loop. Add an acceptance criterion to FR-7:
> Test: importing the same whitespace-titled Outlook event a second time results in `Skipped`, not `Updated`.

**SA-2 (add to FR-3, FR-4):** The Outlook path must pass a non-null `modifiedByUserId` / `createdByUserId` to the entity. Specify the resolution strategy: either `currentUser.Id ?? throw new InvalidOperationException("Outlook import requires an authenticated user context")` or fall back to a documented sync-user constant. Pick one and document in the mapper.

**SA-3 (clarify FR-1):** The new `MarketingAction` constructor signature (Decision 1) should be added explicitly to the spec — currently it is implied by FR-4 / FR-5 but never stated. Recommended signature documented in "Interfaces and Contracts" above.

**SA-4 (clarify FR-1):** The spec lists `CreatedAt`, `CreatedByUserId`, `CreatedByUsername` as *not* in the tightened-setter set. Confirm intent: they should also become `{ get; private set; }` and be assigned only via the new constructor (otherwise creation-time invariants remain exposed). Recommendation: tighten them too, since they fall under the same encapsulation principle, but mark this as a small scope expansion.

**SA-5 (clarify FR-1 null-title handling):** FR-1 says title is `(title ?? string.Empty).Trim()` but the FR-6 acceptance criterion says "null title throws **or** is replaced with empty string (per implementation choice)." Pick one in the spec — recommend the brief's behavior (replace with empty, do not throw) for consistency with current behavior (the property is declared `string` non-null but DB enforces `NOT NULL`, and an empty title would fail EF validation downstream where it's caught with a clear error).

## Prerequisites

None. No database migration, no configuration change, no new packages, no infrastructure work, no feature flag, no Azure Key Vault secret. The change is a pure refactor confined to the Marketing module's Domain and Application layers, with a single behavioral change (titles trimmed on Outlook import) that is the intentional fix.

Validation gates per `CLAUDE.md`:
- `dotnet build` clean
- `dotnet format` clean
- All existing tests in `backend/test/Anela.Heblo.Tests/{Domain,Application,Features}/Marketing/` pass after fixture updates
- New `MarketingActionUpdateDetailsTests` (FR-6) and extended `ImportFromOutlookHandlerTests` (FR-7 + SA-1 regression case) pass