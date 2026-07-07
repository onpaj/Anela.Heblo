# Code Review: CancellationToken Consistency for IBankStatementImportRepository

## Summary
The implementation matches the spec and task-context line-for-line: the interface, EF Core repository, both handlers, and the two Moq-based test files were all updated exactly as prescribed, with the `DbUpdateException` detach-and-rethrow logic preserved verbatim. I independently verified the committed diff (`git show 9a8f6fa`), rebuilt the full solution, ran `dotnet format --verify-no-changes`, and re-ran the two affected test classes — all green. The self-reported Moq callback-arity fix is correct.

## Review Result: PASS

### task: add-cancellationtoken-to-bank-repository-and-call-sites
**Status:** PASS

**Verification performed:**
- `git show 9a8f6fa` reviewed in full — diff touches exactly the 6 files listed in the task context, no unrelated changes.
- `IBankStatementImportRepository.cs`: `GetByIdAsync`, `AddAsync`, `UpdateAsync` all gained `CancellationToken cancellationToken = default` as the final parameter; no other signatures touched (FR-1 met).
- `BankStatementImportRepository.cs`: `GetByIdAsync` now calls `_context.BankStatements.FindAsync(new object[] { id }, cancellationToken)` (correct overload selection — EF Core has no single-key+token overload); `AddAsync`/`UpdateAsync` call `SaveChangesAsync(cancellationToken)`; the `try/catch (DbUpdateException)` → `entry.State = EntityState.Detached` → `throw` blocks are untouched (FR-2 met).
- `GetBankStatementByIdHandler.cs` line 29: `_repository.GetByIdAsync(request.Id, cancellationToken)` — only line changed in the file (FR-4 met).
- `ImportBankStatementHandler.cs`: `InsertNewAsync` gained a `CancellationToken cancellationToken` parameter (no default, per arch-review Decision 2/Implementation Guidance — internal method, always called with explicit token) and forwards it to `AddAsync`; `UpsertExistingAsync` forwards its existing token to both `UpdateAsync` and the fallback `InsertNewAsync` call; `ProcessStatementAsync`'s call site passes the token on both the `isRetry` and non-retry branches. Control flow, the "persist exactly once" comment, and `CancellationToken.None` in `Handle`'s catch block are all left untouched (FR-3 met).
- `GetBankStatementByIdHandlerTests.cs`: all 5 `Setup`/`Verify` calls for `GetByIdAsync` (lines 42, 63, 78, 85, 100 per spec) updated to two-argument form with `It.IsAny<CancellationToken>()`.
- `ImportBankStatementHandlerTests.cs`: all `AddAsync`/`UpdateAsync` `Setup`/`Verify` calls updated to two-argument form.
- `BankStatementImportRepositoryTests.cs` (the ~49-call-site direct/EF-integration test file) has zero diff, as required by FR-5 and the Out-of-Scope section — confirmed via `git diff 9a8f6fa~1 9a8f6fa -- .../BankStatementImportRepositoryTests.cs` (empty).
- **Moq callback-arity fix verification (specifically requested):** the impl notes claim a bug was found where `ReturnsAsync((BankStatementImport b) => b)` (single-parameter callback) no longer matched the two-argument `Setup` target and was fixed to `ReturnsAsync((BankStatementImport b, CancellationToken _) => b)`. I confirmed via `git show` and direct file read (lines 164, 221, 315 of `ImportBankStatementHandlerTests.cs`) that all three affected call sites now use the two-parameter callback form `(BankStatementImport b, CancellationToken _) => b`, which is the correct Moq 4.20 pattern — `ReturnsAsync` callback overloads must match the arity of the setup's target method signature. This is correct, not just claimed.
- Rebuilt full solution (`dotnet build Anela.Heblo.sln`): 0 errors (253 pre-existing nullable-reference warnings in unrelated files, unrelated to this change).
- `dotnet format Anela.Heblo.sln --verify-no-changes`: exit code 0, no diffs.
- Re-ran `dotnet test --filter "FullyQualifiedName~GetBankStatementByIdHandlerTests|FullyQualifiedName~ImportBankStatementHandlerTests"`: **Passed! Failed: 0, Passed: 15, Skipped: 0, Total: 15** — matches the impl summary's claimed 15/15.
- Did not re-run the full `dotnet test` suite (impl summary reports 5389 passed / 64 failed, all 64 pre-existing Testcontainers/Docker-dependent integration tests unrelated to Bank or this change — this claim is plausible and consistent with the sandbox's lack of a Docker daemon; the two directly affected test classes were independently re-verified above).

No functional requirement gaps, no architecture deviations, no correctness bugs found.

## Docs to Update
None. This is a pure internal plumbing change with no public API, contract, or behavior surface requiring documentation updates.

## Overall Notes
- The scope discipline is notable: no drive-by changes to other repository interfaces despite the arch-review explicitly flagging the same asymmetry likely exists elsewhere (correctly deferred to a backlog item, not folded into this diff).
- `InsertNewAsync`'s new `CancellationToken` parameter has no default value, consistent with the arch-review's guidance that internal always-explicitly-called methods should not mask a missed-wiring bug behind a default.
- The one pre-existing, unrelated build noise item (`Anela.Heblo.AccessMatrixGen` JSON parse exception during `dotnet build`/`dotnet test`, exit code 134 on a pre-build target) did not affect build success or test results and is outside this task's scope.
