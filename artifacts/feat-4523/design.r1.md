# Design
Single file `EcomailOptionsValidatorTests.cs`.
- Helper `ValidOptions()` returns `new EcomailOptions { BaseUrl = "https://api2.ecomailapp.cz", HttpTimeoutSeconds = 60, RecomputeWindowMonths = 2, BackfillFrom = new(2024,11,1) }`.
- Helper `Validate(EcomailOptions)` -> `new EcomailOptionsValidator().Validate(null, options)`.
- `[Fact]` tests for: default valid, today ok, tomorrow fails (future msg, not earliest msg), 2019-12-31 fails (earliest msg, not future), 2020-01-01 ok, multiple errors accumulate.
- `[Theory]` tests: BaseUrl ("", "  ", "/relative", "not a url") fails; HttpTimeoutSeconds (0, 601, -1) fails and (1, 600) ok; RecomputeWindowMonths (0, -1) fails and 1 ok.
- Assert via `result.Failed`, `result.Failures` containing substring.
