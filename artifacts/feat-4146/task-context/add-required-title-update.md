### task: add-required-title-update

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/UpdateJournalEntryRequest.cs:13`
- Test (existing, unmodified — confirms no regression): `backend/test/Anela.Heblo.Tests/Features/Journal/UpdateJournalEntryHandlerTests.cs`

- [ ] **Step 1: Confirm the existing handler test still describes the required-title behavior (no test changes needed)**

Run:
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdateJournalEntryHandlerTests" -v minimal
```
Expected: all tests in `UpdateJournalEntryHandlerTests` PASS (including its `null`/whitespace `Title` `[Theory]` cases returning `ErrorCodes.InvalidJournalTitle`). This establishes the "before" baseline, same reasoning as the Create task.

- [ ] **Step 2: Add `[Required]` to `Title` on `UpdateJournalEntryRequest`**

In `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/UpdateJournalEntryRequest.cs`, change:

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

This matches the existing `[Required]` + `[MaxLength(10000)]` ordering already used on `Content` two lines below it in the same class. No `using` change needed — `System.ComponentModel.DataAnnotations` is already imported at the top of the file (line 3). `Id` (line 11, no annotation) and every other property are untouched, as is `UpdateJournalEntryResponse`.

- [ ] **Step 3: Re-run the handler test to confirm no regression**

Run:
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdateJournalEntryHandlerTests" -v minimal
```
Expected: same PASS result as Step 1, unchanged.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Journal/Contracts/UpdateJournalEntryRequest.cs
git commit -m "fix(journal): add [Required] to Title on UpdateJournalEntryRequest"
```

---
