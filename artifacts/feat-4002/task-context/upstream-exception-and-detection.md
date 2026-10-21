### task: upstream-exception-and-detection

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/PlaudUpstreamUnavailableException.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientRunTests.cs` (and `PlaudCliClientParserTests.cs` for the static helper)

- [ ] Write failing tests first: `TryParseUpstreamStatus` returns true/status for `Error: API error: 500 Internal Server Error`, 502, 503, 504; false for `API error: 404 Not Found`, `AUTH_FAILED`, empty and null. Using the existing fake-CLI approach in `PlaudCliClientRunTests`, a CLI exiting 1 with the issue's stderr throws `PlaudUpstreamUnavailableException` (StatusCode 500, message contains the trimmed stderr); `AUTH_FAILED` still throws `PlaudAuthExpiredException`; 404 stderr throws plain `InvalidOperationException` (not the new type); no token refresh is attempted on 5xx.
- [ ] Create the exception: `public sealed class PlaudUpstreamUnavailableException : InvalidOperationException` with `int StatusCode` and ctor `(int statusCode, string message)`.
- [ ] In `PlaudCliClient` add `private static readonly Regex UpstreamStatusRegex = new(@"API error:\s*(5\d\d)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);` and `public static bool TryParseUpstreamStatus(string? stderr, out int status)`.
- [ ] In `RunCliCoreAsync` restructure the non-zero-exit branch: compute `trimmed` first; `AUTH_FAILED` -> LogError + `PlaudAuthExpiredException` (unchanged); 5xx -> `LogWarning("Plaud CLI upstream error {Status}: {Error}")` + throw `PlaudUpstreamUnavailableException(status, $"Plaud CLI exited with code {code}: {trimmed}")`; else LogError + existing `InvalidOperationException`. Keep the existing message format for the unchanged branches.
- [ ] Run `dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests`; expect all pass.
- [ ] Commit: `feat(plaud): classify upstream 5xx as PlaudUpstreamUnavailableException`.

