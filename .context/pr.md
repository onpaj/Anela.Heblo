# PR Context

- **PR**: #2963 — feat: Decouple FileStorage Module from ExpeditionList Configuration
- **URL**: https://github.com/onpaj/Anela.Heblo/pull/2963
- **Branch**: `feat-arch-review-filestorage-module-reads-exp` → `main`
- **State**: OPEN
- **Author**: onpaj
- **Changes**: +1665 / -23 across 20 files
- **Absorbed**: backmerged with `main` (2026-06-14), test fixed, all tests passing

## Description

Decouples `FileStorageModule` from the `ExpeditionList` configuration namespace. The module previously read `configuration["ExpeditionList:BlobConnectionString"]`, a hidden cross-module coupling flagged by the arch-review routine on 2026-06-05. The fix introduces a `FileStorageOptions` class, binds it via the standard options pattern, and adds fail-fast startup validation in non-Development environments so a missing `FileStorage:BlobConnectionString` key surfaces as an `OptionsValidationException` at boot rather than silently routing writes to the storage emulator in production.

**IMPORTANT: Key Vault secrets must be provisioned in staging (`kv-heblo-stg`) and production before this PR is merged.** The production vault name must be confirmed and recorded here. See the "Module-owned Key Vault Secrets" table in `docs/architecture/environments.md` for the exact secret names.

## Absorb notes

- Backmerge from main (2026-06-14): one conflict in `answers.md` — combined both Q&A sets (FileStorage Q1-3 and Npgsql resilience Q1-6).
- Test fix: `FileStorageModuleTests.cs:163` was using `ProductExportOptions` (moved to Catalog module by PR #2972 with different fields) — changed to `FileDownloadOptions`.
