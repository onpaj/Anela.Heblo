Plan saved to `docs/superpowers/plans/2026-06-01-relocate-backgroundrefresh-dtos.md`.

## Summary

The plan decomposes the pure-refactor work into 11 bite-sized tasks:

1. **Pre-flight verification** — baseline namespace check, target folder/namespace confirmation, clean build, TS client checksum snapshot.
2–4. **Move each of the three DTOs** via `git mv` + one-line namespace update, with rename-detection verification after each.
5. **Update `BackgroundRefreshController`** with a single new `using` directive (alphabetically ordered) and verify the build now succeeds with zero warnings.
6. **Scoped `dotnet format`** on the four touched files only, with `--verify-no-changes` confirmation.
7. **Backend tests** — full `dotnet test` (arch review confirmed no test consumers; failures would surface hidden ones).
8. **Regenerate TS client + diff** against the baseline SHA — expected no-op per NSwag's default `schemaNameGenerator`; commit only if diff is benign.
9. **Frontend build + lint** plus verification that no unintended frontend file changed.
10. **Final repo-wide grep** anchored to the three symbol names (per arch review amendment, not the broader namespace), plus confirmation no `*Dto.cs` remains under the API project and `git log --follow` works.
11. **Single conventional-commit** with rename-detection verification.

Each spec requirement (FR-1 through FR-5, NFR-1 through NFR-4) is mapped to a specific task step in the self-review table.