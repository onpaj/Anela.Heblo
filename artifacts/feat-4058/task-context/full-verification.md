### task: full-verification

**Files:** none (verification only)

- [ ] **Step 1: Run the full backend test suite**

Run: `cd backend && dotnet build && dotnet format --verify-no-changes && dotnet test`
Expected: Build succeeds, `dotnet format` reports no changes needed, all tests pass (including the new and modified tests from every prior task in this plan).

- [ ] **Step 2: Manually re-read the diff against the spec**

Confirm each of spec.r1.md's FR-1 through FR-5 and NFR-1/NFR-2 is satisfied by the committed changes:
- FR-1/FR-2 (validate + unwrap): `TryParseAndValidate` + `ExtractEmbeddedJsonObject`.
- FR-3 (bounded retry): `MaxAttempts = 3` loop in `ExtractAsync`.
- FR-4 (loud, distinct failure): `MeetingTaskExtractionFailedException` thrown only after exhausting retries, with raw response + attempt count logged.
- FR-5 (no silent task loss): both callers now catch the exception and report failure instead of persisting an empty-but-"successful" transcript; `PlaudPollingJob` counts failures distinctly.
- NFR-1 (happy path unaffected): a valid first-attempt response makes exactly one chat-client call, same as before.
- NFR-2 (observability): every attempt is logged (warning on retry, error on final failure with raw response + attempt count).

- [ ] **Step 3: No commit for this task** — it is verification-only. If Step 1 or Step 2 surfaces a gap, fix it within the relevant prior task's files and amend that task's commit before proceeding (do not create a new "fixup" task).
