# Task Plan: Fix DateTime.Now to DateTime.UtcNow in Catalog Background Refresh Task

## File Map

| Action | File | Purpose |
|--------|------|---------|
| Modify | `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` | Replace two `DateTime.Now` calls with `DateTime.UtcNow` at lines 310 and 313 |

No files are created. No files are deleted.

---

### task: fix-datetime-utcnow

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs:310,313`

- [ ] **Step 1: Apply the change on line 310**

  Open `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`.

  Find line 310 (inside `RegisterBackgroundRefreshTasks`):

  ```csharp
  // Before
  var twoYearsAgo = DateOnly.FromDateTime(DateTime.Now.AddYears(-2));
  ```

  Replace with:

  ```csharp
  // After
  var twoYearsAgo = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-2));
  ```

- [ ] **Step 2: Apply the change on line 313**

  On line 313, immediately below:

  ```csharp
  // Before
  var dateTo = DateOnly.FromDateTime(DateTime.Now).AddMonths(-1); // Current month is not accurate
  ```

  Replace with:

  ```csharp
  // After
  var dateTo = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1); // Current month is not accurate
  ```

  The trailing inline comment `// Current month is not accurate` must be preserved exactly as-is.

- [ ] **Step 3: Verify the diff is surgical**

  Run from the project root:

  ```bash
  git diff
  ```

  Expected: exactly two changed tokens (`DateTime.Now` → `DateTime.UtcNow`), both in `CatalogModule.cs`, nothing else. If any other file or line appears in the diff, something went wrong — revert and start again.

- [ ] **Step 4: Build**

  From the project root:

  ```bash
  dotnet build backend/Anela.Heblo.sln
  ```

  Expected: build succeeds with zero errors and zero new warnings. If any warning is introduced, investigate before proceeding.

- [ ] **Step 5: Commit**

  Stage only the modified file:

  ```bash
  git add backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs
  ```

  Commit:

  ```bash
  git commit -m "fix: use DateTime.UtcNow in catalog background refresh task

  Replace DateTime.Now with DateTime.UtcNow on lines 310 and 313 of
  CatalogModule.cs to avoid timezone-dependent date boundaries that
  could corrupt margin calculation windows by one calendar day."
  ```
