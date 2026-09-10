### task: fix-download-from-url-log-labels

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs:118,141,145`
- Verify (no changes expected): `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs`

- [ ] **Step 1: Confirm no test currently asserts on the old log text**

Run:
```bash
grep -n "ProductExportDownload" backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs
```
Expected: no output (exit code 1, no match). This confirms the fix needs no test edits. If this unexpectedly finds a match, update that test's expected string alongside Step 2 below instead of skipping it.

- [ ] **Step 2: Edit the three log statements in `DownloadFromUrlHandler.cs`**

In `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs`, inside `Handle(...)`, change the `catch (Exception ex)` block's log call:

```csharp
// Before
_logger.LogError(ex, "Unexpected failure during ProductExportDownload for URL: {RedactedUrl}", redactedUrl);

// After
_logger.LogError(ex, "Unexpected failure during DownloadFromUrl for URL: {RedactedUrl}", redactedUrl);
```

Inside `ProbeContentLengthAsync(...)`, change the `catch (OperationCanceledException)` block's log call:

```csharp
// Before
_logger.LogDebug("HEAD probe timed out for ProductExportDownload");

// After
_logger.LogDebug("HEAD probe timed out for DownloadFromUrl");
```

And the `catch (Exception ex)` block's log call directly below it:

```csharp
// Before
_logger.LogDebug(ex, "HEAD probe failed for ProductExportDownload");

// After
_logger.LogDebug(ex, "HEAD probe failed for DownloadFromUrl");
```

Do not touch any other line in the file — no other string, log level, exception argument, placeholder name, or control-flow statement changes.

- [ ] **Step 3: Verify the old string is fully gone and the new string is in place**

Run:
```bash
grep -n "ProductExportDownload" backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs
```
Expected: no output (exit code 1, no match).

Run:
```bash
grep -n "DownloadFromUrl\"" backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs
```
Expected: three matching lines — the `LogError` line and the two `LogDebug` lines edited in Step 2.

- [ ] **Step 4: Build the backend**

Run:
```bash
cd backend && dotnet build
```
Expected: `Build succeeded.` with no new warnings or errors attributable to this file.

- [ ] **Step 5: Run the existing DownloadFromUrl test suite**

Run:
```bash
cd backend && dotnet test --filter "FullyQualifiedName~DownloadFromUrlHandlerTests"
```
Expected: all tests pass (this is a text-only change; no test behavior should be affected). If any test fails, read its assertion — if it asserts the literal old string, update the assertion to the new string `"DownloadFromUrl"` and re-run; otherwise investigate as a genuine regression before proceeding.

- [ ] **Step 6: Run `dotnet format` to match repository formatting conventions**

Run:
```bash
cd backend && dotnet format --verify-no-changes
```
Expected: no formatting violations reported for the edited file. If violations are reported, run `dotnet format` (without `--verify-no-changes`) to apply them, then re-run `--verify-no-changes` to confirm clean.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs
git commit -m "fix(filestorage): correct stale ProductExportDownload log label in DownloadFromUrlHandler"
```
