# Implementation: add-url-redactor (r1)
- Added `backend/src/Anela.Heblo.Domain/Features/FileStorage/UrlRedactor.cs` (static `Redact(string?)`, per design.r1.md).
- Added `backend/test/Anela.Heblo.Tests/Features/FileStorage/UrlRedactorTests.cs` (query, fragment, user-info, relative/invalid, null/empty/whitespace, plain URL).
- `dotnet test --filter UrlRedactorTests`: 9 passed, 0 failed.
