# Gotcha: E2E Auth — navigateToApp() vs createE2EAuthSession()

**Problem:** Using `createE2EAuthSession()` alone leaves the frontend session missing, causing the Microsoft Entra ID login screen to appear mid-test and failing the test silently.

**Root cause:** `createE2EAuthSession()` only creates the backend service principal token. The frontend also needs session cookies and sessionStorage set (including the E2E flag). `navigateToApp()` does all of this.

**Fix:**
```typescript
// Always do this:
await navigateToApp(page);

// Never rely solely on this:
await createE2EAuthSession(page); // missing frontend session
```

**Where this burned us:** Every new E2E test written before this rule was documented would hit the Microsoft login screen on CI.
