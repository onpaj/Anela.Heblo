This is a named branch in a `.worktrees/` workspace. Tests pass. Here are your options:

```
Implementation complete. What would you like to do?

1. Merge back to main locally
2. Push and create a Pull Request
3. Keep the branch as-is (I'll handle it later)
4. Discard this work

Which option?
```

Here's what was implemented (for your PR/commit summary):

- **BE**: Added `MarketingActionType? ActionType` to `GetMarketingActionsRequest` DTO and mapped it in `GetMarketingActionsHandler` — activates the existing repository filter branch and DB index
- **FE**: New `marketingActionTypeLabels.ts` module with canonical Czech labels/badge/options; extended `useMarketingCalendar` hook; added "Typ akce" `<select>` dropdown as first control in `MarketingActionFilters`; fixed `MarketingActionGrid` broken label maps; updated `MarketingCalendarPage` to forward the filter
- **Tests**: 109 BE tests pass (19 new), 45 FE tests pass (30 new/rewritten)