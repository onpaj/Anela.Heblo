# Code Review: move-identity-resolution-to-classify-invoices-handler

## Summary
The implementation exactly matches the task spec: `ICurrentUserService` resolution was removed from `InvoiceClassificationService` and moved into `ClassifyInvoicesHandler`, with `processedBy` now an explicit parameter threaded through all four `RecordClassificationHistory` call sites. Verified directly against the diff (commit `d4eef9f`), the current file contents, and a live `dotnet build` + scoped `dotnet test` run — build succeeds with 0 errors and all 88 InvoiceClassification tests pass.

## Review Result: PASS

### task: move-identity-resolution-to-classify-invoices-handler
**Status:** PASS

## Docs to Update
No documentation changes required. This is an internal bugfix to an existing, undocumented-in-detail identity-resolution flow (ADR-005 already codifies the "resolve identity in handlers, not services" pattern per `docs/architecture/development_guidelines.md`); no new public behavior, endpoint, or operational concept was introduced, and `InvoiceClassificationModule.cs` DI registration is unchanged.

## Overall Notes
Verification performed beyond reading the summaries:
- `IInvoiceClassificationService.cs`: signature is now `Task<InvoiceClassificationResult> ClassifyInvoiceAsync(ReceivedInvoice invoice, string processedBy)` — matches FR-1.
- `InvoiceClassificationService.cs`: `ICurrentUserService` field, constructor parameter, `using Anela.Heblo.Domain.Features.Users;`, and the internal `GetCurrentUser()` call are all gone (confirmed via `grep`); all four `RecordClassificationHistory` calls (no-match, success, ABRA-failure, catch block) now pass the `processedBy` parameter — matches FR-2.
- `ClassifyInvoicesHandler.cs`: `ICurrentUserService` injected in the correct parameter position (after `IClassificationRuleRepository`, before `logger`); `Handle` resolves `currentUser`/`processedBy` exactly once, immediately after `response.TotalInvoicesProcessed = invoicesToClassify.Count;` and before the `foreach` loop, using the exact fallback expression from the task context (`IsAuthenticated ? (string.IsNullOrEmpty(Name) ? "system" : Name) : "system"`); the same `processedBy` value is passed into every `ClassifyInvoiceAsync` call in the batch — matches FR-3.
- Test coverage: `InvoiceClassificationServiceTests.cs` updated across all 4 tests to pass an explicit `processedBy` literal and assert `ProcessedBy` equals it (no more `ICurrentUserService` mock). `ClassifyInvoicesHandlerTests.cs` adds `Handle_WhenCurrentUserIsUnauthenticated_PassesSystemAsProcessedBy` (verifies `"system"` fallback for `IsAuthenticated == false`) and `Handle_WhenCurrentUserIsAuthenticated_PassesUserNameAsProcessedBy` (verifies `currentUser.Name` pass-through, e.g. `"jane.doe"`) — both the authenticated and unauthenticated/background-job paths are covered, satisfying FR-4.
- `InvoiceClassificationModule.cs` was confirmed unchanged and still has no `ICurrentUserService` registration, as expected (it's registered at the composition root).
- Ran `dotnet build Anela.Heblo.sln` from repo root: 0 errors, 250 warnings (pre-existing, unrelated to this change — consistent with the impl summary's claim).
- Ran `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceClassification"`: 88/88 passed, confirming the impl summary's scoped test-run claim.
- No stray references to `currentUser`/`_currentUserService`/`ICurrentUserService` remain in `InvoiceClassificationService.cs`.
