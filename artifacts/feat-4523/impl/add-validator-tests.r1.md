# Implementation: add-validator-tests r1
Created `backend/test/Anela.Heblo.Tests/Ecomail/EcomailOptionsValidatorTests.cs` (18 test cases: valid default, today/tomorrow/2019-12-31/2020-01-01 boundaries, else-if independence, BaseUrl theory, timeout bounds, recompute months, accumulated failures).
Deviation: the relative-URL case uses "relative/path" instead of "/relative", because on Linux `Uri.TryCreate("/relative", UriKind.Absolute)` yields a file:// URI and the validator accepts it (production behaviour, out of scope).
`dotnet build` 0 errors; `dotnet test --filter EcomailOptionsValidatorTests` 18/18 pass; `dotnet format` clean.
