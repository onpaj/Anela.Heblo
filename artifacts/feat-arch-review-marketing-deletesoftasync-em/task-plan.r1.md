Plan saved. Running a quick self-review pass against the spec and the actual code I read.

Spec coverage — all FRs and NFRs have a task row in the coverage map at the bottom. ✅

Placeholder scan — no `TBD`/`TODO`/"add error handling"/"similar to" stubs. Every code step shows the exact replacement. ✅

Type consistency — verified against the real signatures I read:
- `UpdateAsync` returns `Task` → mocks use `.Returns(Task.CompletedTask)` everywhere. ✅
- `SaveChangesAsync` returns `Task<int>` → mocks use `.ReturnsAsync(1)` everywhere, and `.ThrowsAsync(...)` in Step 1.8 (works for both `Task` and `Task<int>`). ✅
- `MarketingAction.SoftDelete(userId, username)` is 2-arg throughout — flagged the spec's 3-arg typo per arch-review Amendment 1. ✅
- `MarketingAction` type already imported in both test files I edit. ✅

Plan saved to `docs/superpowers/plans/2026-06-09-marketing-delete-soft-async-handler-owns-save.md`. Five tasks, TDD-ordered (RED tests in Tasks 1–2, GREEN handler in Task 3, dead-code removal in Task 4, validation gate in Task 5), each step is ≤5 minutes with exact paths, code blocks, and commit messages. The arch-review's spec amendments (drop the `, now` arg; cover `MarketingActionHandlerSyncTests.cs`; wrap both `UpdateAsync` + `SaveChangesAsync` in the existing try/catch; keep the `UpdateAsync` call) are all materialized as concrete steps.