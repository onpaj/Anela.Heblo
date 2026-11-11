### task: final-validation
**Files:** none

- [ ] Run `cd backend && dotnet build && dotnet format --verify-no-changes` and the Manufacture test filter (`--filter FullyQualifiedName~Manufacture`); all must pass.
- [ ] No process-doc change needed (behavior unchanged); if the Process Docs CI check flags `docs/processes/module-manufacture.md` or the batch-planning doc, bump `verified_at` and run `python3 scripts/process-docs/check.py index`.
