## Module
Marketing

## Finding
`backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/GetMarketingAction/GetMarketingActionHandler.cs`, line 36, calls into a sibling handler to reuse its mapping method:

```csharp
return new GetMarketingActionResponse
{
    Action = GetMarketingActionsHandler.MapToDto(action),  // ← cross-handler dependency
};
```

`MapToDto` is declared as `internal static` on `GetMarketingActionsHandler` (`GetMarketingActions/GetMarketingActionsHandler.cs`, line 52). It is a pure `MarketingAction → MarketingActionDto` projection with no list-query behaviour — it has no business being owned by the list handler.

## Why it matters
- **SRP**: `GetMarketingActionsHandler` now owns shared mapping logic that is not part of its own use case. Any future need to change the single-item view's shape (e.g. include extra fields not present in the list DTO) forces either diverging the handlers or adding conditional logic inside `GetMarketingActionsHandler.MapToDto`.
- **Coupling**: A handler for one use case (`GetMarketingAction`) directly references the class of another use case handler (`GetMarketingActionsHandler`). Renaming, moving, or splitting `GetMarketingActionsHandler` silently breaks `GetMarketingActionHandler`.
- **Discoverability**: The mapping logic is not where a reader expects it. Finding "how is `MarketingActionDto` built?" requires knowing to look inside the list handler, not the DTO class or a mapping file.

## Suggested fix
Extract `MapToDto` out of `GetMarketingActionsHandler` and place it as a static factory method on `MarketingActionDto` itself (or a `MarketingActionMappingExtensions` class in the `Contracts/` folder):

```csharp
// Contracts/MarketingActionDto.cs
public class MarketingActionDto
{
    // ... existing properties ...

    public static MarketingActionDto FromEntity(MarketingAction action) => new()
    {
        Id = action.Id,
        // ... same projection as today's MapToDto ...
    };
}
```

Both handlers then call `MarketingActionDto.FromEntity(action)` with no handler-to-handler coupling. No behaviour changes; pure structural move.

---
_Filed by daily arch-review routine on 2026-05-26._