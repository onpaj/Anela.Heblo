### task: add-url-redactor
Create `backend/src/Anela.Heblo.Domain/Features/FileStorage/UrlRedactor.cs` with `public static string Redact(string? url)` per design.r1.md (strip query, fragment, user-info; `[redacted]` on null/empty/invalid; never throws).
TDD: add `backend/test/Anela.Heblo.Tests/Features/FileStorage/UrlRedactorTests.cs` first covering: query stripped (`?token=abc`), fragment stripped, user-info stripped, relative/invalid string -> `[redacted]`, null/empty -> `[redacted]`, plain URL unchanged.
Verify: `dotnet test backend/test/Anela.Heblo.Tests --filter UrlRedactorTests`.

