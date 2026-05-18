# Specification: Fix `MarketingAction.AssociateWithProduct` Duplicate Detection

## Summary
The domain method `MarketingAction.AssociateWithProduct` compares the raw input against stored values when checking for duplicates, but persists a normalized (trimmed, uppercased) value. This casing mismatch lets the guard pass when an equivalent association already exists, then trips the DB composite key constraint at save time. Normalize the input once, up front, and use the normalized value for both the guard and persistence.

## Background
`MarketingAction.AssociateWithProduct` (`backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs:76–84`) is the intended boundary for adding product associations to a marketing action. Two handlers call it:

- `UpdateMarketingActionHandler` — applies user-supplied product code lists from the UI/API.
- `ImportFromOutlookHandler` — imports product codes parsed from Outlook content.

Both call sites can supply product codes in mixed casing (e.g. `"abc"`, `"Abc"`, `" ABC "`). The current implementation:

```csharp
if (ProductAssociations.Any(pa => pa.ProductCodePrefix == productCode))   // raw, case-sensitive
    return;

ProductAssociations.Add(new MarketingActionProduct
{
    ProductCodePrefix = productCode.Trim().ToUpperInvariant(),            // stored uppercase
    ...
});
```

When `"abc"` is supplied while `"ABC"` is already stored, `"ABC" != "abc"` so the guard does not short-circuit, the entity adds a second `MarketingActionProduct` with `ProductCodePrefix = "ABC"`, and the DB composite primary key on `(MarketingActionId, ProductCodePrefix)` raises a `DbUpdateException` on `SaveChanges`. The domain guard is the intended defence; the DB constraint is meant to be a safety net, not the primary deduplication mechanism. Today, the safety net carries the load and a clean no-op turns into an unhandled exception that bubbles up through the handler.

This was flagged by the daily arch-review routine on 2026-05-17.

## Functional Requirements

### FR-1: Normalize product code before duplicate check
`AssociateWithProduct` MUST compute a single normalized form of the input — `Trim()` followed by `ToUpperInvariant()` — and use that normalized value for both the duplicate-detection comparison and the value persisted on the new `MarketingActionProduct`.

**Acceptance criteria:**
- Calling `AssociateWithProduct("abc")` when an association with `ProductCodePrefix == "ABC"` already exists returns without modifying `ProductAssociations` and without throwing.
- Calling `AssociateWithProduct(" abc ")` when an association with `ProductCodePrefix == "ABC"` already exists returns without modifying `ProductAssociations` and without throwing.
- Calling `AssociateWithProduct("xyz")` when no matching association exists adds exactly one `MarketingActionProduct` with `ProductCodePrefix == "XYZ"`.
- A unit test asserts that two consecutive calls `AssociateWithProduct("abc")` then `AssociateWithProduct("ABC")` result in exactly one entry in `ProductAssociations`.

### FR-2: Reject empty/whitespace input explicitly
`AssociateWithProduct` MUST validate the input and throw `ArgumentException` for `null`, empty, or whitespace-only product codes, with `paramName = "productCode"`. This makes the contract explicit instead of silently storing an empty or whitespace string.

**Acceptance criteria:**
- `AssociateWithProduct(null)` throws `ArgumentException` with `ParamName == "productCode"`.
- `AssociateWithProduct("")` throws `ArgumentException` with `ParamName == "productCode"`.
- `AssociateWithProduct("   ")` throws `ArgumentException` with `ParamName == "productCode"`.
- The existing handlers (`UpdateMarketingActionHandler`, `ImportFromOutlookHandler`) are reviewed to confirm they either pre-filter empty values or are acceptable to surface the `ArgumentException` (see Open Questions).

### FR-3: Behaviour preserved for all other input
For any input that is non-empty after trimming, the resulting `MarketingActionProduct` MUST have `ProductCodePrefix == productCode.Trim().ToUpperInvariant()`, `MarketingActionId == this.Id`, and `CreatedAt == DateTime.UtcNow` — matching today's stored shape. The only behavioural changes are (a) case-insensitive deduplication and (b) explicit rejection of empty input.

**Acceptance criteria:**
- All existing tests that exercise `AssociateWithProduct` continue to pass without modification (other than tests that intentionally relied on the buggy duplicate behaviour, if any exist).
- No other public method on `MarketingAction` is modified.

## Non-Functional Requirements

### NFR-1: Performance
No measurable change. The fix moves a single `Trim().ToUpperInvariant()` call earlier in the method; complexity remains O(n) over `ProductAssociations` which is small per marketing action.

### NFR-2: Security
None. No new external input surface, no authorization changes, no logging of sensitive data.

### NFR-3: Backwards compatibility
- Existing rows in `MarketingActionProducts` are already stored uppercase by the current code path, so no data migration is required.
- The fix only changes the in-memory guard; the DB schema and constraints are unchanged.
- Callers that previously triggered a `DbUpdateException` on duplicate will now see a silent no-op, matching the documented intent of the domain method. This is the desired behaviour change.

## Data Model
No schema changes.

Affected types (unchanged shape):
- `MarketingAction` (`backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs`) — owns `ProductAssociations`.
- `MarketingActionProduct` — composite primary key `(MarketingActionId, ProductCodePrefix)`. The `ProductCodePrefix` invariant remains "trimmed, uppercase, non-empty".

## API / Interface Design
No public API, controller, MediatR request, or DTO changes. The fix is internal to the domain entity.

Method signature (unchanged):
```csharp
public void AssociateWithProduct(string productCode)
```

Reference implementation (from the brief, accepted as the target):
```csharp
public void AssociateWithProduct(string productCode)
{
    if (string.IsNullOrWhiteSpace(productCode))
        throw new ArgumentException("Product code cannot be empty", nameof(productCode));

    var normalized = productCode.Trim().ToUpperInvariant();

    if (ProductAssociations.Any(pa => pa.ProductCodePrefix == normalized))
        return;

    ProductAssociations.Add(new MarketingActionProduct
    {
        MarketingActionId = Id,
        ProductCodePrefix = normalized,
        CreatedAt = DateTime.UtcNow,
    });
}
```

## Dependencies
None. Self-contained domain change. No external service, library, or feature flag involved.

## Out of Scope
- Refactoring `UpdateMarketingActionHandler` or `ImportFromOutlookHandler` beyond what FR-2 requires for handling `ArgumentException`.
- Adding a corresponding `DisassociateFromProduct` normalization fix (file a follow-up if the same bug exists on the inverse path; not in scope here).
- Changing the `MarketingActionProduct` composite key or adding a case-insensitive collation at the DB level.
- Bulk-normalizing existing rows (already uppercase by construction).
- Any UI/frontend changes — the API contract is unchanged.

## Open Questions
None.

## Status: COMPLETE