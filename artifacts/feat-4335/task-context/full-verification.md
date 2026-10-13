### task: full-verification

**Files:**
- None (verification only, no new files)

- [ ] **Step 1: Repo-wide grep — confirm the three properties are fully removed**

Run: `git grep -n 'PrintPickingListOptions' backend/ | grep -E '\.PrintSink\b|\.BlobConnectionString\b|\.BlobContainerName\b'`
Expected: empty output. (This grep intentionally scopes to lines mentioning `PrintPickingListOptions` to avoid false-positive matches against the unrelated `ExpeditionListArchiveOptions.BlobContainerName` property, which has the same name but is a different class in a different module — see spec.r1.md "Out of Scope".)

- [ ] **Step 2: Repo-wide grep — confirm `PrintSink` dispatch is untouched**

Run: `git grep -n 'configuration\["ExpeditionList:PrintSink"\]' backend/`
Expected: exactly one match, in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`, unchanged from before this plan.

- [ ] **Step 3: Repo-wide grep — confirm no config file changes were needed**

Run: `git diff --stat HEAD -- 'backend/src/Anela.Heblo.API/appsettings*.json'`
Expected: empty output (no changes to any `appsettings*.json` file — confirms NFR-2 / arch-review Decision 2).

- [ ] **Step 4: Format**

Run: `dotnet format backend/Anela.Heblo.sln`
Expected: no diffs reported, or only whitespace touch-ups inside the files modified by this plan.

- [ ] **Step 5: Full build**

Run: `dotnet build`
Expected: PASS, zero errors, zero new warnings.

- [ ] **Step 6: Full backend test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: PASS — no regressions anywhere in the suite, including the new `AzureBlobPrintSinkOptionsBindingTests` and the untouched `CombinedPrintQueueSinkRegistrationTests`.

- [ ] **Step 7: Commit any formatting changes**

If `dotnet format` produced edits:

```bash
git add -u
git commit -m "chore: dotnet format"
```

Otherwise: skip this step — the previous task's commit already covers the full fix.

---

## Spec Coverage Map

| Requirement | Covered by |
|-------------|------------|
| FR-1: `PrintPickingListOptions` trimmed to Application-layer fields only | task: extract-azure-blob-print-sink-options, Step 7 |
| FR-1: No remaining reference to the three removed properties | task: full-verification, Step 1 |
| FR-1: Solution builds clean | task: extract-azure-blob-print-sink-options, Step 8; task: full-verification, Step 5 |
| FR-2: `AzureBlobPrintSinkOptions` class created in Adapters.Azure, correct namespace/defaults | task: extract-azure-blob-print-sink-options, Step 3 |
| FR-2: Bound from the same `"ExpeditionList"` section, no new config/secret | task: extract-azure-blob-print-sink-options, Step 5; task: full-verification, Step 3 |
| FR-2: Two options types can bind from one section without conflict | task: extract-azure-blob-print-sink-options, Step 6 (test asserts both keys bind correctly) |
| FR-3: `AzureAdapterModule` reads via `IOptions<AzureBlobPrintSinkOptions>` | task: extract-azure-blob-print-sink-options, Step 5 |
| FR-3: No behavior change to `BlobContainerClient` construction | task: extract-azure-blob-print-sink-options, Step 6 (asserts `AccountName`/`Name`) |
| FR-3: `CombinedPrintQueueSinkRegistrationTests` passes unmodified | task: extract-azure-blob-print-sink-options, Step 9 |
| FR-4: `PrintSink` dispatch untouched, no replacement | task: full-verification, Step 2 |
| FR-5: Regression guard test added | task: extract-azure-blob-print-sink-options, Steps 1, 6 |
| NFR-1: Zero behavior change across all four print-sink modes | task: extract-azure-blob-print-sink-options, Step 9 (all existing sink tests green) |
| NFR-2: No deployment prerequisites, no appsettings edits | task: full-verification, Step 3 |
| NFR-3: Layering enforced (compiler-checked) | task: extract-azure-blob-print-sink-options, Step 8 |

## Open items deferred to PR review

None — this plan requires no Key Vault provisioning, no environment-specific sequencing, and no human decision points before merge (contrast with the `FileStorageOptions` precedent's Prerequisites P1–P5).
