### task: final-validation
Full validation and docs. Depends on: cleanup-architecture-allowlist.

- [ ] `cd backend && dotnet build` expect no errors; `dotnet format` and check `git diff` only touches intended files.
- [ ] `dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~GiftPackage|FullyQualifiedName~ModuleBoundaries|FullyQualifiedName~Logistics|FullyQualifiedName~Manufacture"` expect pass; also verify the DI container resolves `IGiftPackageManufactureService` (existing module/DI tests, or a quick check in `GiftPackageManufactureModuleTests`).
- [ ] Check `docs/processes/INDEX.md` for a gift-package process doc owning the touched code; if present, bump `verified_at` (refactor, no behaviour change) and run `python3 scripts/process-docs/check.py index`.
- [ ] Optionally add a note to `docs/architecture/development_guidelines.md` only if it lists remaining violations of this pattern for Logistics (do not otherwise touch docs).
- [ ] Commit any remaining changes: `chore: validation and docs for IGiftPackageBomSource`.
