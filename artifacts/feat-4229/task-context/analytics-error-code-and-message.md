### task: analytics-error-code-and-message

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` (after line 199, `InvalidReportPeriod = 1705,`)
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsConstants.cs`
- Test: `backend/test/Anela.Heblo.Tests/Shared/ErrorCodesTests.cs` if it exists — otherwise this task has no dedicated test (enum members and string constants are exercised indirectly by the validator test in the next task); skip Steps 1/2/4 for this task and go straight to the edit.

- [ ] **Step 1: Add the new error code**

In `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`, immediately after `InvalidReportPeriod = 1705,` (still inside the `// Analytics module errors (17XX)` block):

```csharp
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InvalidTimeWindow = 1706,
```

- [ ] **Step 2: Add the new validation message constant**

In `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsConstants.cs`, inside the existing `ValidationMessages` nested class, after `MAX_PRODUCTS_MINIMUM`:

```csharp
        public const string INVALID_TIME_WINDOW = "Invalid time window: '{0}'. Supported values: {1}";
```

- [ ] **Step 3: Build to confirm no compile errors**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsConstants.cs
git commit -m "feat(analytics): add InvalidTimeWindow error code and message"
```

---
