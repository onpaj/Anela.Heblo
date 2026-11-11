# Architecture review
- Test-only change; no architectural impact. Place at `backend/test/Anela.Heblo.Tests/Ecomail/EcomailOptionsValidatorTests.cs`, namespace `Anela.Heblo.Tests.Ecomail`, matching sibling tests (xUnit + FluentAssertions).
- Validator is pure and has no dependencies; no mocks needed. Instantiate directly.
- Time dependence: validator uses `DateTime.UtcNow` directly. Compute test dates relative to `DateTime.UtcNow` (today / tomorrow). Midnight-rollover flake risk is negligible; use today-minus/plus offsets and avoid asserting on exact today boundary across rollover by capturing date once (accept tiny risk) -- do not refactor production code to inject a clock.
- Follow CLAUDE.md: surgical change. No process doc needed (no behavior change).
