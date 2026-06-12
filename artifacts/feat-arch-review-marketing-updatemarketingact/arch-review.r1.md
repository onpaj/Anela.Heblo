# Architecture Review: Encapsulate Collection Replacement in MarketingAction Domain Entity

## Skip Design: true

This change is a backend-only refactor inside the Domain and Application layers. No UI components, screens, layout, or visual design decisions are introduced. The `UpdateMarketingActionCommand` wire contract is unchanged (spec confirms no OpenAPI regeneration), so the FE is not affected.

## Architectural Fit Assessment

The proposal is a textbook DDD-encapsulation refinement that aligns cleanly with the existing layering already present in this repo:

- The codebase follows Clean Architecture with a clear Domain → Application → Persistence stack. `MarketingAction` already lives in `Anela.Heblo.Domain.Features.Marketing` and exposes encapsulated mutators (`UpdateDetails`, `AssociateWithProduct`, `LinkToFolder`, `SoftDelete`, `MarkOutlookSynced`, `ClearOutlookLink`). Adding `ReplaceProductAssociations` / `ReplaceFolderLinks` extends an established pattern; no new abstractions, conventions, or layering shifts.
- The time-handling convention is **caller-supplies-`utcNow`** for mutators that need a timestamp on entity-level fields. `MarketingAction` ctor and `UpdateDetails` already take `DateTime utcNow`; `MarkOutlookSynced` does too. The spec's signature aligns. There is **no `IDateTimeProvider` abstraction** anywhere in the project — `DateTime.UtcNow` is captured at the handler boundary (e.g. `UpdateMarketingActionHandler.cs:59`, `CreateMarketingActionHandler.cs:47`). The open question in the brief is therefore already resolved: pass `now` (already in scope at line 59) to the new methods.
- EF Core integration is unaffected. `MarketingActionRepository.GetByIdAsync` eagerly includes both child collections (`MarketingActionRepository.cs:22-23`), so when `Clear()` runs against tracked navigation properties from inside the entity, the change-tracker still emits the same `DELETE` + `INSERT` SQL it does today. No persistence-layer changes required.
- Main integration points: (1) `UpdateMarketingActionHandler.Handle` lines 95–110; (2) the domain entity itself; (3) the existing domain test folder `backend/test/Anela.Heblo.Tests/Domain/Marketing/`.

The proposal is well-scoped and does not introduce coupling, layering violations, or architectural drift.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────────┐
│ Application Layer                                               │
│   UpdateMarketingActionHandler.Handle                           │
│     ├─ var now = DateTime.UtcNow;                               │
│     ├─ action.UpdateDetails(..., utcNow: now)        (existing) │
│     ├─ action.ReplaceProductAssociations(            (NEW CALL) │
│     │     request.AssociatedProducts, now)                      │
│     ├─ action.ReplaceFolderLinks(                    (NEW CALL) │
│     │     request.FolderLinks?.Select(...), now)                │
│     └─ _repository.UpdateAsync + SaveChangesAsync               │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼ delegates to
┌─────────────────────────────────────────────────────────────────┐
│ Domain Layer — MarketingAction (aggregate root)                 │
│   existing: AssociateWithProduct, LinkToFolder, UpdateDetails…  │
│   NEW:      ReplaceProductAssociations(codes, utcNow)           │
│   NEW:      ReplaceFolderLinks(links, utcNow)                   │
│                                                                 │
│   Both new methods:                                             │
│     1. Treat null input as empty sequence                       │
│     2. Normalize each entry (Trim + ToUpperInvariant for codes; │
│        Trim for folderKey)                                      │
│     3. Deduplicate                                              │
│     4. Clear existing collection                                │
│     5. Add new child entities with CreatedAt = utcNow           │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼ EF change-tracker (unchanged)
┌─────────────────────────────────────────────────────────────────┐
│ Persistence — MarketingActionProduct / MarketingActionFolderLink│
│   DELETE old rows + INSERT new rows (identical SQL to today)    │
└─────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Method signature — caller passes `utcNow`
**Options considered:**
- A) `ReplaceProductAssociations(IEnumerable<string>? codes, DateTime utcNow)` — caller supplies time.
- B) `ReplaceProductAssociations(IEnumerable<string>? codes)` — domain calls `DateTime.UtcNow` internally (matches existing `AssociateWithProduct` / `LinkToFolder`).
- C) Inject `IDateTimeProvider` into the entity (would require a new abstraction).

**Chosen approach:** **A** — caller passes `utcNow`.

**Rationale:** `MarketingAction`'s constructor, `UpdateDetails`, and `MarkOutlookSynced` already follow this convention. Choosing B would propagate the same testability/clock-control friction the rest of the entity has already solved. C is over-engineering — no time abstraction exists in this codebase, and adding one solely for this refactor exceeds the spec's scope ("Out of Scope: replacing EF Core change tracking…"). Within the same Update transaction, the new child rows' `CreatedAt` and the action's `ModifiedAt` will now share a single timestamp, which is preferable.

#### Decision 2: Replace methods do NOT delegate to `AssociateWithProduct` / `LinkToFolder`
**Options considered:**
- A) Inline the Clear + normalize + dedupe + add logic inside each Replace method.
- B) Clear, then loop and call `AssociateWithProduct(code)` / `LinkToFolder(key, type)` per item.

**Chosen approach:** **A** — inline the logic (matches the spec's illustrative code).

**Rationale:** The single-add methods capture `DateTime.UtcNow` internally (`MarketingAction.cs:108, 125`), so reusing them would (1) ignore the caller-supplied `utcNow` parameter, (2) produce per-row timestamp drift, and (3) perform redundant per-item dedupe scans after a `Clear`. The brief's suggested implementation is correct on this point.

#### Decision 3: Folder-link dedup uses composite key `(folderKey, folderType)`
**Options considered:**
- A) Dedup by `folderKey` alone (matches existing `LinkToFolder` line 117).
- B) Dedup by `(folderKey, folderType)` composite (matches spec FR-2).

**Chosen approach:** **B** — composite key, per spec.

**Rationale:** The existing `LinkToFolder` deduplicates by `FolderKey` only and ignores `FolderType` — this is arguably a latent bug, but in practice the existing handler iterates a single request list with no duplicates of either kind, so the bug is not user-observable. The Replace method is a new surface and should be correct from day one. This is a deliberate, narrow improvement over the legacy add method (FR-2 acceptance criterion: "two pairs differing only in `folderType` keeps both"). Document this divergence in the method's XML doc-comment so future readers understand the asymmetry with `LinkToFolder`.

#### Decision 4: Invalid individual entries — silently skip vs throw
**Options considered:**
- A) Throw `ArgumentException` on null/whitespace entries (mirrors `AssociateWithProduct` / `LinkToFolder` per-item validation).
- B) Silently filter out null/whitespace entries.

**Chosen approach:** **A** — throw on per-entry whitespace, matching the existing single-add validation contract.

**Rationale:** The brief's "Open Questions" item is closed in spec FR-2 ("Rejects entries where `folderKey` is null, empty, or whitespace by throwing the same exception type the existing `LinkToFolder` uses"). Extend the same rule to `ReplaceProductAssociations` for symmetry. The Application layer should never send invalid codes in practice (the request DTO has `[Required]` annotations on `MarketingFolderLinkRequest.FolderKey`); fail-loud at the domain boundary is consistent with this aggregate's other invariants. A `null` *sequence* is still treated as empty (per FR-1/FR-2) — only invalid *entries within* the sequence throw.

#### Decision 5: Keep navigation collection setters as-is
**Options considered:**
- A) Convert `ProductAssociations` / `FolderLinks` to `IReadOnlyCollection<>` exposure with private `ICollection<>` backing field for EF.
- B) Leave the public `virtual ICollection<>` setters untouched.

**Chosen approach:** **B** — leave them.

**Rationale:** Out of scope per the spec. The architectural goal here is *Application-layer hygiene*, not full aggregate hardening. Locking down the collections would require also reviewing every read-site (handlers, repository projections at `MarketingActionRepository.cs:92, 99`, mapping code) — an unbounded change set. Flag it under Risks as a future opportunity.

## Implementation Guidance

### Directory / Module Structure

No new files, no new folders. Strictly modify-in-place:

| Action | Path |
|---|---|
| Modify | `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs` — add two methods |
| Modify | `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs` — replace lines 95–110 |
| Add | `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceProductAssociationsTests.cs` |
| Add | `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceFolderLinksTests.cs` |
| Optional | Augment `backend/test/Anela.Heblo.Tests/Application/Marketing/UpdateMarketingActionHandlerTests.cs` with one delta scenario (see "Specification Amendments") |

Follow the existing domain-file conventions exactly: block-scoped namespace (`namespace Anela.Heblo.Domain.Features.Marketing { ... }`), `using` directives inside the namespace block, `private` methods placed below public ones.

### Interfaces and Contracts

```csharp
// In Anela.Heblo.Domain.Features.Marketing.MarketingAction:

/// <summary>
/// Replaces the full set of product associations atomically.
/// Null input is treated as an empty sequence (clears all associations).
/// Inputs are trimmed, upper-cased (invariant), and deduplicated.
/// Throws ArgumentException on entries that are null/empty/whitespace.
/// </summary>
public void ReplaceProductAssociations(
    IEnumerable<string>? productCodes,
    DateTime utcNow);

/// <summary>
/// Replaces the full set of folder links atomically.
/// Null input is treated as an empty sequence (clears all links).
/// Deduplicates by the composite key (folderKey, folderType) — note this is
/// stricter than LinkToFolder which dedupes by folderKey alone.
/// Throws ArgumentException on entries with null/empty/whitespace folderKey.
/// </summary>
public void ReplaceFolderLinks(
    IEnumerable<(string folderKey, MarketingFolderType folderType)>? links,
    DateTime utcNow);
```

**Handler delegation (replaces `UpdateMarketingActionHandler.cs:95-111`):**

```csharp
action.ReplaceProductAssociations(request.AssociatedProducts, now);
action.ReplaceFolderLinks(
    request.FolderLinks?.Select(l => (l.FolderKey, l.FolderType)),
    now);
```

Note: `request.AssociatedProducts` is `List<string>?` (per `UpdateMarketingActionRequest.cs:29`) and assigns directly to `IEnumerable<string>?` — no `?? Enumerable.Empty<…>()` shim needed at the call site since the domain method handles `null` internally. This keeps the handler maximally terse and pushes the null-handling contract into the domain (which is the entire point of the refactor).

### Data Flow

**Update path (after refactor) — clearing all associations:**
```
PUT /api/marketing/actions/42  { AssociatedProducts: null, FolderLinks: null, ... }
   → UpdateMarketingActionHandler.Handle
   → repo.GetByIdAsync(42) → loads MarketingAction + ProductAssociations + FolderLinks
   → action.UpdateDetails(..., utcNow: now)
   → action.ReplaceProductAssociations(null, now)
        → null → empty; ProductAssociations.Clear(); no Adds
   → action.ReplaceFolderLinks(null, now)
        → null → empty; FolderLinks.Clear(); no Adds
   → repo.UpdateAsync + SaveChangesAsync
        → EF emits: UPDATE MarketingAction; DELETE MarketingActionProduct WHERE …;
                    DELETE MarketingActionFolderLink WHERE …
```

**Update path — delta scenario (some kept, some added, some removed):**
```
… AssociatedProducts = ["ABC", " def ", "abc"], FolderLinks = [(key1, General), (key1, Project)]
   → ReplaceProductAssociations: normalize → ["ABC", "DEF"]; Clear; Add 2 rows
   → ReplaceFolderLinks: dedup by composite → keeps both; Clear; Add 2 rows
   → EF emits: DELETE all 3 old product rows; INSERT 2 new; DELETE old folder rows; INSERT 2 new
```

The DML pattern is identical to today (Clear+Add already produces full DELETE+INSERT churn). No round-trip change.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `LinkToFolder` (still in use by `CreateMarketingActionHandler.cs:65`) dedupes by `folderKey` alone, while `ReplaceFolderLinks` dedupes by composite key. Two methods on the same aggregate now have asymmetric invariants. | Medium | Document the asymmetry in the new method's XML doc comment. Flag as a follow-up to harmonize `LinkToFolder` (out of scope per spec "Out of Scope: Renaming or restructuring `AssociateWithProduct` / `LinkToFolder`"). |
| `ProductAssociations` / `FolderLinks` setters remain publicly settable (`public ... { get; set; }`), so any future Application code could still bypass encapsulation. | Low | Add a code-review checklist item; consider follow-up PR to switch to `IReadOnlyCollection<>` projections with private backing fields. Out of scope here. |
| Existing per-item `CreatedAt` values are silently regenerated to `utcNow` even for child rows whose logical content is unchanged. EF cannot tell "kept" from "replaced" because we Clear+Add. | Low | Matches current behavior — the existing Clear+`AssociateWithProduct` flow does the same. No regression. Note for future: if `CreatedAt` semantics ever become audit-grade, switch to a true diff implementation. |
| Per-entry `ArgumentException` on whitespace product codes is a new throw site reachable from the Update handler (the existing handler also reached it via `AssociateWithProduct`, but only when the request DTO contained such values). | Low | DTO validation is already in place (`UpdateMarketingActionRequest` doesn't currently validate individual list entries). Add a defensive `if (string.IsNullOrWhiteSpace(code)) continue;` filter inside the Replace method **only if** the spec is explicitly clarified to silently skip. Default to throwing per Decision 4. |
| Test fixture currently writes assertions like `a.ProductAssociations.Count == 2` (line 202 of `UpdateMarketingActionHandlerTests.cs`) — these continue to pass, but they do not assert the *Clear* semantics. | Low | Add one negative-path handler test: pre-existing action has product associations, request sets `AssociatedProducts = null`, assert `a.ProductAssociations.Count == 0`. |

## Specification Amendments

1. **Resolve the "Open Questions" section explicitly in spec.** The spec already says "Status: COMPLETE" but the body of FR-3 mentions "if no `IDateTimeProvider` is currently injected, fall through to `DateTime.UtcNow` at the call site — see Open Questions." Update FR-3 to a definitive statement: *"Use `var now = DateTime.UtcNow;` already declared at `UpdateMarketingActionHandler.cs:59`. No new abstraction will be introduced; this matches the existing codebase convention."* Same for FR-2's per-entry validation — settle on **throw**, as decided above.

2. **Tighten the method signatures to nullable types.** Spec body shows `IEnumerable<string>` but the "API / Interface Design" section shows `IEnumerable<string>?`. The nullable form is correct (the entity treats `null` as empty). Align the FR-1 / FR-2 illustrative signatures to use `?`.

3. **Clarify "integration test" wording in FR-3.** The repo has no DB-backed handler integration test for `UpdateMarketingActionHandler` — existing tests at `UpdateMarketingActionHandlerTests.cs` mock `IMarketingActionRepository`. The new test for "actually clears them in the database" should be reworded to "verifies that after the handler runs, the in-memory `MarketingAction` passed to `UpdateAsync` has the expected final collection state" — assertable via `Mock.Verify(It.Is<MarketingAction>(a => …))` exactly like the existing test at line 200.

4. **Document the dedup-key asymmetry between `LinkToFolder` and `ReplaceFolderLinks`** in FR-2's behaviour list (it is currently only implicit). Future maintainers should not "fix" the asymmetry by silently aligning one to the other.

5. **Add a Non-Functional Requirement on documentation:** XML doc-comments on both new methods explicitly call out (a) `null` ⇒ empty, (b) normalization rules, (c) dedup key, (d) the dedup-key divergence from `LinkToFolder` for the folder-link method.

## Prerequisites

None. This change requires:

- No database schema changes / migrations
- No EF configuration changes (`MarketingActionConfiguration.cs`, `MarketingActionProductConfiguration.cs`, `MarketingActionFolderLinkConfiguration.cs` are unaffected)
- No new NuGet packages
- No new DI registrations
- No new configuration / `appsettings` entries
- No new secrets / Key Vault entries
- No OpenAPI regeneration; no FE client regeneration
- No feature flag

Implementation can start immediately against the current `main` branch. Validation gates per the project's `CLAUDE.md`: `dotnet build`, `dotnet format`, new + existing tests in `Anela.Heblo.Tests` pass.