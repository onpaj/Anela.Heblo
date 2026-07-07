# PR Context

- **PR**: #3451 — #3445: implementation
- **URL**: https://github.com/onpaj/Anela.Heblo/pull/3451
- **Branch**: `feature/3445-Arch-Review-Bank-Getbyidasync-Addasync-Updateasync` → `main`
- **State**: open
- **Author**: onpaj
- **Changes**: +1272 / -32 across 16 files (plus this absorb's 2-file test fix)
- **Absorbed**: already up to date with `main` (no backmerge needed); fixed a build-breaking
  test compilation failure and pushed, all tests passing (excluding pre-existing
  Docker/Testcontainers-dependent integration tests, unavailable in this sandbox)

## Description

Closes #3445

## What the issue was
`IBankStatementImportRepository.GetByIdAsync`, `AddAsync`, and `UpdateAsync` were the only three
methods on the interface without a `CancellationToken`, breaking consistency with the other four
methods and preventing `ImportBankStatementHandler`/`GetBankStatementByIdHandler` from propagating
MediatR's pipeline cancellation token into these operations.

## How it was fixed / handled
Added `CancellationToken cancellationToken = default` to all three methods on
`IBankStatementImportRepository` and its EF Core implementation, forwarding the token into
`FindAsync`/`SaveChangesAsync`, and threaded it through `GetBankStatementByIdHandler` and
`ImportBankStatementHandler` call sites.

## What pr-autoabsorb fixed

CI was failing with a backend build/compilation error (`🎯 Backend Tests` check). Root cause:

1. `ImportBankStatementHandlerTests.cs` — 4 Moq `.Setup`/`.Verify` calls to `AddAsync`/`UpdateAsync`
   still used the old single-argument signature; after the interface gained a `CancellationToken`
   parameter, the implicit default-value fill-in inside a Moq expression tree is illegal (`CS0854`).
   Fixed by adding `It.IsAny<CancellationToken>()` explicitly, matching the pattern already used
   elsewhere in the same file.
2. `GetConfigurationHandlerTests.cs` — referenced `ConfigurationConstants.APP_VERSION`, which no
   longer exists; a prior commit on `main` moved this key to `InfrastructureConfigurationKeys.APP_VERSION`
   but one test in the same file was left on the old name. Pre-existing bug on `main` (unrelated to
   this PR's own diff), exposed here because the branch already contains that commit. Fixed by
   updating the reference.

No backmerge was needed — the branch already contained the latest `main` (via merge commit
`17d444c`, from the original feature pipeline). Fix commit: `a0b0f6b`.

Result: 5414/5478 backend tests passing (64 pre-existing failures, all Docker/Testcontainers
integration tests unrelated to this change and unavailable in this sandbox — matches the PR's
own documented baseline).
