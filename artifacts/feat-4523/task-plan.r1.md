# Plan: EcomailOptionsValidator tests

### task: add-validator-tests
**Files:** create `backend/test/Anela.Heblo.Tests/Ecomail/EcomailOptionsValidatorTests.cs`

- [ ] Write the test class per `design.r1.md` (namespace `Anela.Heblo.Tests.Ecomail`; usings: `Anela.Heblo.Domain.Features.Ecomail`, `FluentAssertions`, `Xunit`).
- [ ] Cover every requirement 1-10 in `spec.r1.md`, including today/tomorrow/2019-12-31/2020-01-01 boundaries and the else-if independence.
- [ ] Run `dotnet test backend/test/Anela.Heblo.Tests --filter EcomailOptionsValidatorTests`; all pass.
- [ ] Run `dotnet format` on the test project and `dotnet build`.
- [ ] Commit: `test(ecomail): cover EcomailOptionsValidator BackfillFrom guards`.
