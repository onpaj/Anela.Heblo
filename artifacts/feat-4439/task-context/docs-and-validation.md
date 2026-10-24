### task: docs-and-validation
Files: `docs/processes/module-expedition-list-archive.md`.
- [ ] Add a Known quirks/runtime-facts line: missing blob on download returns 404 `BlobNotFound` (1805); Reprint still propagates blob exceptions. Bump `verified_at`.
- [ ] Run `python3 scripts/process-docs/check.py index`.
- [ ] Run `dotnet build`, `dotnet format`, and `dotnet test` for `ExpeditionListArchive` and controller tests; all green.
- [ ] Commit.
