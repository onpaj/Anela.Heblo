### task: full-suite-verification

**Files:**
- Modify: none (verification only)

- [ ] **Step 1: Run the full test class**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~AzureBlobStorageServiceTests"`
Expected: PASS (all tests in the class, including the pre-existing ones)

- [ ] **Step 2: Build and format the backend**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj && dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --verify-no-changes`
Expected: build PASS. If `dotnet format --verify-no-changes` reports changes, run `dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`, then re-commit.

- [ ] **Step 3: Final commit (only if format made changes)**

Run: `git add -A && git commit -m "style(filestorage): apply dotnet format to AzureBlobStorageService tests"`
(Skip if there is nothing to commit.)
