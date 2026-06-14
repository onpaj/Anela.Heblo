# PR Context

- **PR**: #2963 — feat: Decouple FileStorage Module from ExpeditionList Configuration
- **URL**: https://github.com/onpaj/Anela.Heblo/pull/2963
- **Branch**: `feat-arch-review-filestorage-module-reads-exp` → `main`
- **State**: open
- **Author**: onpaj
- **Changes**: +1643 / -23 across 19 files
- **Absorbed**: backmerged with `main` (commit 6a24dcf1), frontend build passes, pushed

## Description

Decouples `FileStorageModule` from the `ExpeditionList` configuration namespace. The module previously read `configuration["ExpeditionList:BlobConnectionString"]`, a hidden cross-module coupling flagged by the arch-review routine on 2026-06-05. The fix introduces a `FileStorageOptions` class, binds it via the standard options pattern, and adds fail-fast startup validation in non-Development environments so a missing `FileStorage:BlobConnectionString` key surfaces as an `OptionsValidationException` at boot rather than silently routing writes to the storage emulator in production.

**IMPORTANT: Key Vault secrets must be provisioned in staging (`kv-heblo-stg`) and production before this PR is merged.** The production vault name must be confirmed and recorded here. See the "Module-owned Key Vault Secrets" table in `docs/architecture/environments.md` for the exact secret names.

## Absorb notes

- CI was failing: 11 compile errors in Marketing test files (`UpdateMarketingActionHandlerTests`, `MarketingActionReplaceProductAssociationsTests`, `MarketingActionReplaceFolderLinksTests`) — all `MarketingAction.AssociateWithProduct` / `LinkToFolder` calls missing the `utcNow` parameter added to the domain on `main`.
- Backmerge from `main` was clean (no conflicts); the merge itself brought in the corrected Marketing test files.
- Backend tests could not be run locally (no .NET SDK in environment); CI will confirm.
- Frontend build: passed. Frontend lint: 126 pre-existing errors (not CI-checked, unrelated to this PR).
