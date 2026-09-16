# Specification: Align CreateMarketingActionHandler with UpdateMarketingActionHandler's bulk-assignment APIs

## Summary
`CreateMarketingActionHandler` currently sets a new `MarketingAction`'s product associations and folder links through per-item loops (`AssociateWithProduct`, `LinkToFolder`), while `UpdateMarketingActionHandler` uses the domain entity's canonical bulk-replace methods (`ReplaceProductAssociations`, `ReplaceFolderLinks`). This spec covers replacing the Create handler's loops with the same bulk-replace calls Update already uses, so both write paths share one entry point into the domain for setting these two associations.

## Background
`MarketingAction` (`backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs`) exposes two pairs of methods for attaching products and folder links:

- `AssociateWithProduct(productCode, utcNow)` / `LinkToFolder(folderKey, folderType, utcNow)` — add-one, idempotent-by-dedup-check methods intended for incremental single-item changes.
- `ReplaceProductAssociations(productCodes, utcNow)` / `ReplaceFolderLinks(links, utcNow)` — clear-and-rebuild bulk methods intended for setting the entire collection at once, with normalization and dedup performed internally.

`CreateMarketingActionHandler.Handle` (lines 59–65) sets the *initial* set of associations on a brand-new `MarketingAction` using the single-item methods in a loop, guarded by a manual, case-sensitive LINQ `.Distinct()` on the incoming product codes. `UpdateMarketingActionHandler.Handle` (lines 95–98) sets the *replacement* set of associations on an existing `MarketingAction` using the bulk methods directly. Both handlers are performing the same conceptual operation — "set the full initial/replacement set of associations from a request" — through two different domain entry points, which is the inconsistency this issue calls out.

## Functional Requirements

### FR-1: Create handler uses `ReplaceProductAssociations` for product associations
`CreateMarketingActionHandler.Handle` must set `action`'s product associations by calling `action.ReplaceProductAssociations(request.AssociatedProducts, now)` instead of the current `if (...) foreach (...) action.AssociateWithProduct(...)` loop with manual `.Distinct()`.

**Acceptance criteria:**
- Given `request.AssociatedProducts` is `null` or empty, the created action ends up with zero product associations (unchanged behavior).
- Given `request.AssociatedProducts` contains distinct codes (e.g. `["abc", "xyz"]`), the created action has one `MarketingActionProduct` per code, normalized to upper-invariant and trimmed (unchanged behavior).
- Given `request.AssociatedProducts` contains case-variant duplicates (e.g. `["abc", "ABC"]`), the created action has exactly one product association for the normalized code `"ABC"` — this is a **behavior fix**: today, `.Distinct()` is case-sensitive so both `"abc"` and `"ABC"` pass the initial filter and only get deduped inside `AssociateWithProduct`'s existing-associations check (which happens to still produce one entry per call sequence, but only because associations are added one at a time against a mutating collection — see Non-Functional/Risk notes below for why this is fragile, not why it's currently broken).
- Given `request.AssociatedProducts` contains an entry that is empty or whitespace-only, `Handle` propagates the `ArgumentException` thrown by `ReplaceProductAssociations` (this is a **behavior change** from today: `AssociateWithProduct` also throws `ArgumentException` for a blank code, so the exception-throwing behavior itself does not change, only which method throws it).
- No new validation, request DTO field, or MediatR contract changes are introduced by this requirement — request/response shapes are unchanged.

### FR-2: Create handler uses `ReplaceFolderLinks` for folder links
`CreateMarketingActionHandler.Handle` must set `action`'s folder links by calling:
```csharp
action.ReplaceFolderLinks(
    request.FolderLinks?.Select(l => (l.FolderKey, l.FolderType)),
    now);
```
instead of the current `if (...) foreach (...) action.LinkToFolder(link.FolderKey.Trim(), link.FolderType, now)` loop.

**Acceptance criteria:**
- Given `request.FolderLinks` is `null` or empty, the created action ends up with zero folder links (unchanged behavior).
- Given `request.FolderLinks` contains links with distinct `(FolderKey, FolderType)` pairs, the created action has one `MarketingActionFolderLink` per pair, with `FolderKey` trimmed (unchanged behavior — see `Handle_PersistsFolderLinks_WhenProvided` in `CreateMarketingActionHandlerTests.cs`, which must keep passing).
- Given `request.FolderLinks` contains two entries with the **same `FolderKey` but different `FolderType`** (e.g. `("key-1", General)` and `("key-1", Campaign)`), the created action ends up with **both** links. This is a **behavior change**: today's `LinkToFolder` loop dedupes by `FolderKey` alone (see `MarketingAction.LinkToFolder`, which checks `FolderLinks.Any(fl => fl.FolderKey == folderKey)`), so the second call would be silently dropped; `ReplaceFolderLinks` dedupes by the composite key `(FolderKey, FolderType)` (documented explicitly in the XML doc on `ReplaceFolderLinks`: *"this is stricter than `LinkToFolder` ... The asymmetry is intentional; new code should use this method when replacing the full set."*). This is the intended behavior per the domain method's own documentation and per the suggested fix in the issue; it is flagged here as an observable behavior change during Create, not a defect.
- Given `request.FolderLinks` contains an entry whose `FolderKey` is empty or whitespace-only, `Handle` propagates the `ArgumentException` thrown by `ReplaceFolderLinks` (behavior unchanged: `LinkToFolder` already throws `ArgumentException` for a blank key).
- No new validation, request DTO field, or MediatR contract changes are introduced by this requirement — request/response shapes are unchanged.

## Non-Functional Requirements

### NFR-1: Behavioral equivalence outside the documented folder-link dedup change
Aside from the intentional folder-link dedup-key change described in FR-2, and the case-insensitive product-dedup fix described in FR-1, the change must not alter any other observable behavior of `CreateMarketingActionHandler`: authorization check, Outlook sync call ordering (Outlook create still happens before `SaveChangesAsync`, per `Handle_CallsOutlookBeforeDb_WhenPushEnabled`), Outlook error-code mapping, DB-save-failure compensation (Outlook event delete), and success-path logging/response shape must all remain unchanged.

### NFR-2: No new dependencies or interfaces
This is a same-file, same-method-body refactor inside `CreateMarketingActionHandler.Handle`. No new services, repository methods, or domain methods are introduced — `ReplaceProductAssociations` and `ReplaceFolderLinks` already exist and are already exercised (by `UpdateMarketingActionHandler` and its tests).

## Data Model
No schema changes. The relevant existing entities:
- `MarketingAction` (aggregate root) — `ProductAssociations: ICollection<MarketingActionProduct>`, `FolderLinks: ICollection<MarketingActionFolderLink>`.
- `MarketingActionProduct` — `ProductCodePrefix` (normalized upper-invariant, trimmed).
- `MarketingActionFolderLink` — `FolderKey` (trimmed), `FolderType` (`MarketingFolderType` enum).

Domain methods in scope (unchanged, reused as-is):
- `MarketingAction.ReplaceProductAssociations(IEnumerable<string>? productCodes, DateTime utcNow)`
- `MarketingAction.ReplaceFolderLinks(IEnumerable<(string folderKey, MarketingFolderType folderType)>? links, DateTime utcNow)`

## API / Interface Design
No public API surface changes. `CreateMarketingActionRequest` / `CreateMarketingActionResponse` (MediatR contract, HTTP endpoint) are unchanged. This is purely an internal implementation change inside the handler's `Handle` method.

## Dependencies
- `MarketingAction.ReplaceProductAssociations` and `MarketingAction.ReplaceFolderLinks` — already implemented in the domain layer, already used by `UpdateMarketingActionHandler`; no changes needed to these methods.
- Existing test suites `backend/test/Anela.Heblo.Tests/Application/Marketing/CreateMarketingActionHandlerTests.cs` and `backend/test/Anela.Heblo.Tests/Features/Marketing/CreateMarketingActionHandlerTests.cs` (note: two differently-namespaced test files with overlapping names currently exist — see Open Questions).

## Out of Scope
- Changing `AssociateWithProduct` or `LinkToFolder` themselves, or removing them from the domain entity (they may still be used elsewhere, e.g. for true single-item incremental updates if such a use case exists).
- Changing `UpdateMarketingActionHandler` (already uses the bulk methods; not touched by this change).
- Any change to the Outlook sync flow, authorization flow, or DB-save/compensation flow in `CreateMarketingActionHandler`.
- Any change to `CreateMarketingActionRequest`/`Response` DTOs or the MediatR contract.
- Deduplicating or consolidating the two differently-namespaced `CreateMarketingActionHandlerTests.cs` files (flagged, not addressed, by this spec).

## Open Questions
None. The two differently-namespaced test files (`Application/Marketing/CreateMarketingActionHandlerTests.cs` and `Features/Marketing/CreateMarketingActionHandlerTests.cs`) are noted for the architect/planner to confirm during implementation which one (or both) needs a new case for the folder-link dedup behavior change, but this does not block starting the work — treated as an assumption: both files should be checked and updated for the FR-2 dedup-behavior change and the FR-1 case-insensitive-dedup fix.

## Status: COMPLETE
