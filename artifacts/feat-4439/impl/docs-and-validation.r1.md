# Implementation: docs-and-validation

Updated docs/processes/module-expedition-list-archive.md: new Known quirks line (download of missing blob -> 404 BlobNotFound 1805, invalid path -> 400, Reprint still propagates blob exceptions); verified_at bumped. `check.py index` run (INDEX.md unchanged). `dotnet build Anela.Heblo.sln`: 0 errors. `dotnet test --filter ExpeditionListArchive`: 43 passed. `dotnet format --verify-no-changes` on changed .cs files: clean.

## Status
DONE
