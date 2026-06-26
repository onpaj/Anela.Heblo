# wire-adapter-di-registration — Code Review (r1)

## Review Result: PASS

### task: wire-adapter-di-registration
**Status:** PASS

---

## Acceptance Criteria Verification

✅ **Criterion 1:** `Microsoft365AdapterServiceCollectionExtensions` adds `AddHttpClient("MicrosoftGraph", ...)` and `AddScoped<IPhotobankGraphService, PhotobankGraphService>()` inside `if (!useMockAuth && !bypassJwt)` block

**Finding:** Verified in `/backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Microsoft365AdapterServiceCollectionExtensions.cs` (lines 22–27):
```csharp
if (!useMockAuth && !bypassJwt)
{
    services.AddScoped<IOutlookCalendarSync, OutlookCalendarSyncService>();
    services.AddHttpClient("MicrosoftGraph", _ => { })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = true,
        });
    services.AddScoped<IPhotobankGraphService, PhotobankGraphService>();
}
```
Both registrations are present and correctly scoped.

✅ **Criterion 2:** `PhotobankModule.cs` no longer registers `PhotobankGraphService` (concrete adapter class)

**Finding:** Verified in `/backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs` (lines 31–48). The module registration block does not register the concrete `PhotobankGraphService` class. The real implementation is now only registered in the adapter extensions.

✅ **Criterion 3:** `PhotobankModule.cs` registers `MockPhotobankGraphService` only when `useMockAuth || bypassJwtValidation`

**Finding:** Verified in `/backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs` (lines 45–48):
```csharp
if (useMockAuth || bypassJwtValidation)
{
    services.AddScoped<IPhotobankGraphService, MockPhotobankGraphService>();
}
```
Mock is correctly registered only in mock/bypass scenarios.

✅ **Criterion 4:** Build produces no new errors (pre-existing errors in GetThumbnailHandler.cs are expected from previous tasks)

**Finding:** Build completed with:
- **53 warnings** (pre-existing, not introduced by this task)
- **2 errors** (both pre-existing in `/backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetThumbnail/GetThumbnailHandler.cs`):
  - `CS0029`: Cannot implicitly convert `GetThumbnailResult` to `GraphThumbnail`
  - `CS0246`: `GraphThrottledException` not found

These errors were introduced in earlier commits (`introduce-getthumbnailresult-du` and `move-photobankgraphservice-to-adapter`) and stem from the incomplete handler refactor, not from this task's changes.

---

## Implementation Quality

**Code organization:**
- DI registration cleanly separated: real implementation in adapter layer, mock-only in application module
- Proper conditional logic prevents double registration
- Using directives added correctly to adapter extensions

**Architecture:**
- Adapter layer now owns its own service registration — correct inversion of control
- Module no longer depends on concrete adapter implementations
- Mock implementation remains at application layer where it belongs

**No collateral issues:**
- No unused imports or dead code
- Existing patterns (e.g., `OutlookCalendarSyncService` registration) followed correctly
- Build errors are isolated to `GetThumbnailHandler.cs` and unrelated to this task

---

## Summary

The implementation correctly moves the `MicrosoftGraph` HttpClient and `PhotobankGraphService` DI registration out of the application module and into the adapter layer, with proper conditional logic to handle mock vs. real scenarios. All acceptance criteria are satisfied. Build errors are pre-existing and expected.

**Recommendation:** APPROVED for merge.
