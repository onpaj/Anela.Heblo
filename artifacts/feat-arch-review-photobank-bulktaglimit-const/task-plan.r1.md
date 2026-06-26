Plan saved to `artifacts/feat-arch-review-photobank-bulktaglimit-const/task-plan.r1.md`.

**Summary of the plan:**

7 tasks across 5 files implementing the DRY refactor:

1. **Task 0 (Pre-flight)** — 5 read-only checks: verify file doesn't exist, both private consts are at expected lines, both tests still assert `"5000"`, baseline tests green, baseline build clean.
2. **Task 1** — Add `PhotobankConstants.BulkTagLimit.ToString()` assertion to `BulkAddPhotoTagHandlerTests.cs`. Confirms RED (compile error).
3. **Task 2** — Create `PhotobankConstants.cs` (file-scoped namespace, `public static class`, single `public const int BulkTagLimit = 5_000`). Confirms GREEN.
4. **Task 3** — Refactor `BulkAddPhotoTagHandler.cs`: delete private const + blank line, rewrite the two `BulkTagLimit` references.
5. **Task 4** — Mirror Task 1 for `BulkAddPhotoTagByIdsHandlerTests.cs`.
6. **Task 5** — Mirror Task 3 for `BulkAddPhotoTagByIdsHandler.cs`.
7. **Task 6** — Full validation: targeted `dotnet format` (idempotency check), full-solution `dotnet build` (zero new warnings), full Photobank test suite, `git diff --stat` review (must show exactly 5 files).
8. **Task 7** — Stage by exact path (no `git add -A`), verify staged set, commit with `refactor:` conventional message.

Coverage table at the bottom maps every FR/NFR and both architecture amendments to specific task steps.