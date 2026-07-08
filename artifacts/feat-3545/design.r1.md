# Design: Push MaxOrder computation into the database for InvoiceClassification rules

## Component Design

### `IClassificationRuleRepository` (Domain — `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IClassificationRuleRepository.cs`)
Gains one new member, placed near `GetAllAsync`/`GetActiveRulesOrderedAsync`:

```csharp
Task<int> GetMaxOrderAsync();
```

**Responsibility:** Return the current maximum `Order` value across all `ClassificationRule` rows, or `0` if no rules exist. No other member of the interface changes.

### `ClassificationRuleRepository` (Persistence — `backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationRuleRepository.cs`)
Implements the new member as a single EF Core scalar aggregate query, placed near `GetAllAsync`/`GetActiveRulesOrderedAsync`:

```csharp
public async Task<int> GetMaxOrderAsync()
{
    return await _context.ClassificationRules.MaxAsync(r => (int?)r.Order) ?? 0;
}
```

**Responsibility:** Translate to `SELECT MAX([Order]) FROM ClassificationRules` (or the InMemory-provider equivalent in tests) instead of loading full rows. The `(int?)` cast avoids `MaxAsync` throwing `InvalidOperationException` on an empty table; `?? 0` preserves existing "no rules yet" semantics. No other repository method changes.

### `CreateClassificationRuleHandler` (Application — `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/CreateClassificationRule/CreateClassificationRuleHandler.cs`)
Replaces the current full-table load + in-memory `Max`:

```csharp
var allRules = await _ruleRepository.GetAllAsync();
var maxOrder = allRules.Count > 0 ? allRules.Max(r => r.Order) : 0;
```

with a single call to the new repository method:

```csharp
var maxOrder = await _ruleRepository.GetMaxOrderAsync();
```

**Responsibility:** Unchanged apart from this swap — resolves current user, constructs the new `ClassificationRule`, calls `rule.SetOrder(maxOrder + 1)`, persists via `AddAsync`, maps to `ClassificationRuleDto`, returns the response. `GetAllAsync()` remains on the interface/implementation unchanged (still used elsewhere, e.g. rule listing).

### Component interaction

```
CreateClassificationRuleHandler (Application)
        │  await _ruleRepository.GetMaxOrderAsync()
        ▼
IClassificationRuleRepository (Domain)
        ▼
ClassificationRuleRepository (Persistence) — EF Core MaxAsync
        ▼
ApplicationDbContext.ClassificationRules
        ▼
SELECT MAX([Order]) FROM ClassificationRules
```

No new components, no new interfaces, no DI registration changes — the existing `IClassificationRuleRepository` → `ClassificationRuleRepository` binding is reused unchanged.

## Data Schemas

No schema or migration changes. No new/changed request or response DTOs, controller contracts, or event payloads — this is an internal repository-interface addition consumed entirely within the InvoiceClassification vertical slice.

**New internal method contract:**

| Method | Input | Output | Behavior |
|---|---|---|---|
| `GetMaxOrderAsync()` | none | `Task<int>` | Returns the maximum `ClassificationRule.Order` value in the `ClassificationRules` table, or `0` if the table is empty |

Existing table/entity used as-is: `ClassificationRule.Order` (`int`, non-nullable) on the `ClassificationRules` table via `ApplicationDbContext`.
