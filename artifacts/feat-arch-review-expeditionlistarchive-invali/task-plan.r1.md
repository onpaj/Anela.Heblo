Plan saved to `docs/superpowers/plans/2026-06-04-expedition-list-archive-invalid-input.md`.

The plan implements the arch-review's canonical-pattern recommendation (not the spec's literal "mirror the siblings" approach), structured as 5 sequential, TDD-driven tasks:

1. **Failing handler test** for invalid date (`[Theory]` covering `"not-a-date"`, slash/dash-swapped formats, `null`, empty) — asserts `Success=false`, `ErrorCode=InvalidFormat`, the exact `Params` keys, and that `IBlobStorageService.ListBlobsAsync` is never invoked.
2. **Handler fix** — replace the silent-empty early return at `GetExpeditionListsByDateHandler.cs:21-24` with a canonical failure response (`ErrorCode = ErrorCodes.InvalidFormat`, `Params = { Field: "Date", ExpectedFormat: "yyyy-MM-dd" }`). No DTO changes, no `Fail(string)` factory.
3. **Controller change** — flip `Ok(response)` to `HandleResponse(response)` in `ExpeditionListArchiveController.GetByDate` so the existing `[HttpStatusCode(BadRequest)]` on `InvalidFormat` maps to HTTP 400 automatically.
4. **Full validation gate** — `dotnet test`, `dotnet build`, `dotnet format`, with explicit guidance to revert any out-of-scope formatter changes.
5. **Frontend reconnaissance** (read-only) — confirm the FE consumer handles 400 gracefully via global error handling, per the arch-review's medium-severity regression risk.

Each step has exact paths, exact code, exact commands, expected output, and a commit. Skipping the execution handoff prompt as instructed.