## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/RunHydrationTierHandlerTests.cs:29` and `:46` — `Handle_ReturnsNotFound_WhenNoEnabledTasksInTier` and `Handle_ReturnsNotFound_WhenTasksInTierAreAllDisabled` have identical bodies apart from the seeded task list. Could be collapsed into a single `[Theory]`/`MemberData` case (empty list vs. all-disabled list) to remove the duplication while keeping both scenarios covered.
