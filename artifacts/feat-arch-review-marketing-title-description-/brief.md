## Module
Marketing

## Finding

`MarketingAction.Title` and `Description` are mutated in two independent places with different normalization rules:

**API mutation path (consistent):**
```csharp
// CreateMarketingActionHandler.cs:52
action.Title = request.Title.Trim();
action.Description = request.Description?.Trim();

// UpdateMarketingActionHandler.cs:61–62
action.Title = request.Title.Trim();
action.Description = request.Description?.Trim();
```

**Outlook import path (no `.Trim()`):**
```csharp
// OutlookEventImportMapper.cs:34 (BuildAction)
Title = ParseTitle(evt.Subject),        // truncates to 200 chars, does NOT trim
Description = ParseDescription(evt.BodyText),  // strips HTML, does NOT trim

// OutlookEventImportMapper.cs:66–67 (ApplyChanges)
existing.Title = ParseTitle(evt.Subject);
existing.Description = ParseDescription(evt.BodyText);
```

`ParseTitle` (line 78) just clips length; it never calls `.Trim()`. `ParseDescription` (line 83) strips HTML via regex and calls `WhitespaceRegex.Replace(...).Trim()` on the stripped text, so descriptions are incidentally trimmed — but titles are not.

Result: any `MarketingAction` whose title was set or updated via `ImportFromOutlook` can have leading or trailing whitespace. Actions updated through the regular API cannot. The same database column ends up with inconsistent casing/trimming depending on which code path last touched the record.

The root cause is that `MarketingAction` has no `Update(...)` domain method. Instead, every mutation path reaches directly into the entity's properties. The entity already shows intent for encapsulated mutation with `SoftDelete`, `MarkOutlookSynced`, `AssociateWithProduct`, and `LinkToFolder` — the scalar-field update is the only operation left exposed as raw property assignment.

## Why it matters

- **Data consistency**: title comparisons, deduplication, or search that relies on exact-match can silently differ between API-managed and Outlook-imported actions.
- **Invariant duplication**: adding a new normalization rule (e.g. strip control characters, normalize smart quotes from Outlook) requires finding and updating every mutation site rather than one place.
- **SRP / Tell-Don't-Ask**: two separate callers (`UpdateMarketingActionHandler`, `OutlookEventImportMapper`) are responsible for knowing and applying the entity's field invariants, instead of the entity itself.

## Suggested fix

Add an `UpdateDetails` domain method to `MarketingAction` that centralises all scalar-field normalization and replaces the direct property assignments in both handlers:

```csharp
// MarketingAction.cs
public void UpdateDetails(
    string title,
    string? description,
    MarketingActionType actionType,
    DateTime startDate,
    DateTime? endDate,
    string modifiedByUserId,
    string? modifiedByUsername,
    DateTime utcNow)
{
    Title = (title ?? string.Empty).Trim();
    Description = description?.Trim();
    ActionType = actionType;
    StartDate = startDate;
    EndDate = endDate;
    ModifiedAt = utcNow;
    ModifiedByUserId = modifiedByUserId;
    ModifiedByUsername = modifiedByUsername ?? "Unknown User";
}
```

Both `UpdateMarketingActionHandler` (lines 61–68) and `OutlookEventImportMapper.ApplyChanges` (lines 66–73) and `OutlookEventImportMapper.BuildAction` (lines 32–43) call this method instead of setting properties directly. `CreateMarketingActionHandler` initialises the object via constructor but can use the same method or an analogous constructor.

The change is behavioural only for the Outlook import path (adds `.Trim()` to titles); the API paths are unchanged.

---
_Filed by daily arch-review routine on 2026-05-29._