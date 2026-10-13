### task: add-required-title-create

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/CreateJournalEntryRequest.cs:11`
- Test (existing, unmodified — confirms no regression): `backend/test/Anela.Heblo.Tests/Features/Journal/CreateJournalEntryHandlerTests.cs`

- [ ] **Step 1: Confirm the existing handler test still describes the required-title behavior (no test changes needed)**

Run:
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CreateJournalEntryHandlerTests" -v minimal
```
Expected: all tests in `CreateJournalEntryHandlerTests` PASS (including `Handle_WhenTitleIsNullOrWhitespace_ShouldReturnInvalidJournalTitleError`). This test calls the handler directly, bypassing MVC model binding, so it is unaffected by the DTO annotation change and must keep passing before and after Step 2 — this step establishes the "before" baseline.

- [ ] **Step 2: Add `[Required]` to `Title` on `CreateJournalEntryRequest`**

In `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/CreateJournalEntryRequest.cs`, change:

```csharp
        [MaxLength(200)]
        public string Title { get; set; } = null!;
```

to:

```csharp
        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = null!;
```

This matches the existing `[Required]` + `[MaxLength(10000)]` ordering already used on `Content` two lines below it in the same class. No `using` change needed — `System.ComponentModel.DataAnnotations` is already imported at the top of the file (line 3). No other property, constructor, or the paired `CreateJournalEntryResponse` class changes.

- [ ] **Step 3: Re-run the handler test to confirm no regression**

Run:
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CreateJournalEntryHandlerTests" -v minimal
```
Expected: same PASS result as Step 1, unchanged — this test bypasses `[ApiController]` model binding entirely, so adding `[Required]` to the DTO has no effect on it.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Journal/Contracts/CreateJournalEntryRequest.cs
git commit -m "fix(journal): add [Required] to Title on CreateJournalEntryRequest"
```

---
