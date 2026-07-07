## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs:109` — `_emailSender.SendEmailAsync(message)` still doesn't pass the `cancellationToken` that's now threaded through `SendEmailCopy`. Pre-existing gap (unchanged by this diff, `IEmailSender.SendEmailAsync` already accepts an optional token), but now that the token is available in scope it would be a trivial, free improvement to wire it through — out of scope for this surgical refactor, just flagging for a follow-up.

### Verification performed beyond reading the diff
- Confirmed `IExpeditionListService` is untouched — only the concrete `ExpeditionListService` constructor changed; all three callers (`PrintExpeditionOrderHandler`, `RunExpeditionListPrintFixHandler`, `PrintPickingListJob`) go through the interface and are unaffected.
- Confirmed `Cleanup`/`SendEmailCopy` are private with no other callers.
- Confirmed `ITemporaryFileAccessor` is registered exactly once (`AddFileSystemTemporaryFileAccessor()`), unconditionally, before the `PrintSink` switch — matching the spec's "works regardless of configured PrintSink" requirement, mirroring `IPrintQueueSink`'s adapter split, with no duplicate/conflicting registrations in any switch branch.
- Confirmed the `ConfigurationConstants.APP_VERSION` → `InfrastructureConfigurationKeys.APP_VERSION` change in `GetConfigurationHandlerTests.cs` is not a stray/unrelated edit: `ConfigurationConstants` no longer has an `APP_VERSION` member (moved to `InfrastructureConfigurationKeys` in already-merged main commit `bbedc9f`), so this is a required fix to keep the branch compiling, not a bug.
- Confirmed `EmailAttachment`/`EmailMessage` field usage in the new `PrintPickingListAsync_EmailAttachments_BuiltFromAccessorBytes` test matches the actual model shapes.
- Full solution build (`dotnet build` on `Anela.Heblo.API.csproj`, which pulls in Application/Domain/Adapters): 0 errors.
- Targeted test run (`--filter "FullyQualifiedName~ExpeditionList|FullyQualifiedName~GetConfigurationHandlerTests"`): 170/170 passed, including the new `FileSystemTemporaryFileAccessorTests` and the updated `ExpeditionListServiceOrderStateTests`/`ExpeditionListServicePrintSinkTests`.
- Implementation matches spec `spec.r1.md` FR-1 through FR-5 exactly (interface shape, adapter placement, DI registration, service refactor, and test coverage requirements all satisfied).
