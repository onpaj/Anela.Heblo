### task: write-dto-settability-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs`

This is the RED step: write a test that requires `ErrorType` to have a public setter. Against the current (unmodified) DTO this fails to **compile**, which is the expected "failing" signal for this particular refactor (there is no runtime-failing equivalent since the member does not exist yet in settable form).

- [ ] **Step 1: Add the new test method**

Add the following `[Fact]` to `BankMappingProfileTests.cs`, immediately after the existing `Map_BankStatementImport_To_Dto_When_ImportResult_Is_Not_OK_Sets_ErrorType_To_ImportResult` method (before the closing `}` of the class):

```csharp
    [Fact]
    public void BankStatementImportDto_ErrorType_Is_Settable_Independently_Of_The_Mapper()
    {
        var dto = new BankStatementImportDto
        {
            ErrorType = "SOME_ERROR",
        };

        dto.ErrorType.Should().Be("SOME_ERROR");
    }
```

- [ ] **Step 2: Build the test project and confirm it fails to compile**

Run from the repo root:

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: build **fails** with a compiler error on the new test's object-initializer line, of the form `CS0200: Property or indexer 'BankStatementImportDto.ErrorType' cannot be assigned to -- it is read only`. This confirms the test correctly exercises the not-yet-implemented behavior (FR-1). If the build succeeds, stop — the DTO was already settable and something upstream of this plan is inconsistent with the Verified Codebase Facts above; investigate before continuing.

- [ ] **Step 3: Commit the failing test**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Bank/BankMappingProfileTests.cs
git commit -m "test(bank): add failing test proving BankStatementImportDto.ErrorType is not yet settable"
```

---

