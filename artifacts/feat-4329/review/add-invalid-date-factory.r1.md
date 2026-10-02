# Code Review: add-invalid-date-factory

## Summary
The implementation adds `GetExpeditionListsByDateResponse.InvalidDate()` exactly as specified in the task context (FR-1), matching the sibling `Fail()`-style factories in the same module. A new unit test asserts the exact failure shape. The full `GetExpeditionListsByDateHandlerTests` suite passes (8/8, 0 failed).

## Review Result: PASS

### task: add-invalid-date-factory
**Status:** PASS

## Docs to Update
(none — internal C# refactor, no public API, CLI, or operational behavior change)

## Overall Notes
- Method signature, body, and field values match the task-context's Step 3 code block verbatim.
- Test method matches the task-context's Step 1 code block verbatim, and was confirmed passing (`dotnet test ... --filter FullyQualifiedName~GetExpeditionListsByDateHandlerTests` → 8 passed, 0 failed).
- FR-2 (updating the handler to call `InvalidDate()`) is explicitly out of scope for this task — it is task 2 (`update-handler-to-use-factory`) of the plan and is not evaluated here.
- Unrelated finding (not a blocker for this task, not touched by this diff): `RecurringJobSeeder.cs:51` has a pre-existing compile error (`HasSeededFieldsChanged(existing, config)` should be `HasSeededFieldsChanged(existingConfig, config)`) present on `origin/main` itself (PR #4324, commit 882659fe). This currently breaks `dotnet build`/`dotnet test` for the whole `Anela.Heblo.Application` project repo-wide. Recommend a separate issue/fix — out of scope here.
