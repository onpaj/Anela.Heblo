# Code Review: move-di-registration-to-sharedragmodule

## Summary
The commit (`74dfd89`) does exactly what the spec and arch-review Decision 2/3 prescribe: `AddSharedRagModule` now takes `IConfiguration` and owns the 3 `IDocumentTextExtractor` registrations plus the Graph-vs-Mock `IOneDriveService` selection, moved verbatim (including `AddHttpClient("MicrosoftGraph")`/`AddMemoryCache()` and the deliberate `"KnowledgeBase"`-only config check); `KnowledgeBaseModule.cs` had the equivalent block removed while keeping its own `KnowledgeBaseOptions` binding and every other registration untouched; the single `ApplicationModule.cs` call site was updated to pass `configuration`. Independently re-running `dotnet build` and the DI smoke tests confirms the change is safe.

## Review Result: PASS

### task: move-di-registration-to-sharedragmodule
**Status:** PASS

## Overall Notes

**Verification performed independently (not just re-reading the impl summary):**
- `git show --stat HEAD` / `git log -1 -p`: exactly 4 files touched (`state.json` + `ApplicationModule.cs`, `KnowledgeBaseModule.cs`, `SharedRagModule.cs`), diff content matches the spec's exact-code blocks line for line, including the comment explaining Decision 3's deliberate scope (not fixed).
- `grep -rn "AddSharedRagModule(" backend/src backend/test` → exactly one call site (`ApplicationModule.cs:61`), now `services.AddSharedRagModule(configuration);` — matches FR expectations.
- `grep -rn "IOneDriveService|IDocumentTextExtractor" backend/src` → registrations for both types exist only in `SharedRagModule.cs`; no duplicate registrations anywhere else in `backend/src`.
- `dotnet build Anela.Heblo.sln` from the worktree root → **Build succeeded, 0 Errors, 254 Warnings** (matches the impl summary's claim; warnings are pre-existing nullable-reference warnings in unrelated test files, none referencing `KnowledgeBaseModule.cs` or `SharedRagModule.cs`).
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ApplicationStartupTests"` → **Passed! Failed: 0, Passed: 349, Total: 349** — confirms `IDocumentTextExtractor`/`IOneDriveService` still resolve via DI for every controller across both KnowledgeBase and Leaflet (FR-4 acceptance criterion).
- `dotnet test ... --filter "FullyQualifiedName~Shared.Rag|FullyQualifiedName~KnowledgeBase|FullyQualifiedName~Leaflet"` → **Failed: 26, Passed: 378, Skipped: 3, Total: 407**, exactly matching the impl summary's numbers. Pulled the complete (untruncated) failure list and confirmed all 26 failures are `System.ArgumentException: Docker is either not running or misconfigured` from Testcontainers PostgreSQL, split across `LeafletRepositoryIntegrationTests` (11 tests) and `KnowledgeBaseRepositoryIntegrationTests` (15 tests) — a sandbox limitation (no Docker daemon), unrelated to the relocated DI registrations. No other failure type or class is present. This confirms the impl summary's claim is accurate and the failures are correctly out of scope for this task.
- Confirmed the Graph-vs-Mock check still reads `configuration.GetSection("KnowledgeBase")` only (not generalized to also check `"Leaflet"`), per arch-review Decision 3 — this is the intended zero-behavior-change outcome, not a bug.
- Noted, separately during the build, an unrelated pre-existing tool crash: the `AccessMatrixGen` post-build step throws `System.Text.Json.JsonException: '/' is an invalid start of a value` (MSB3073, exit code 134) on every build in this sandbox. This is a pre-existing environment quirk in an unrelated code-gen tool (`backend/tools/Anela.Heblo.AccessMatrixGen`) and does not fail the overall build or affect this task's diff.

**Minor nit (does not block PASS):** `KnowledgeBaseModule.cs` retains `using Anela.Heblo.Domain.Shared;` (line 8), which was previously needed only for `InfrastructureConfigurationKeys.BYPASS_JWT_VALIDATION` inside the OneDrive selection block that just moved to `SharedRagModule`. Grepping the file for any other symbol from that namespace (`InfrastructureConfigurationKeys`, `Result<T>`, `Cooling`, `CurrencyCode`) turns up nothing, so this using is now dead. The task spec's Step 3 only asked to grep for the `Shared.Rag`/`.DocumentExtractors`/`.OneDrive` usings (which were correctly removed) and didn't call out this one — it's a small spec gap rather than an implementer error, and it produces no build warning (the project doesn't have unused-using analysis enabled) and no behavioral effect. Worth a one-line cleanup in a follow-up but not worth a revision cycle for this task.

## Docs to Update
None — this is a pure internal DI-wiring refactor with no external contract or documented-behavior change.
