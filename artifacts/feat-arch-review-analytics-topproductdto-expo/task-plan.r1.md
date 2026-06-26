Plan saved to `docs/superpowers/plans/2026-06-10-remove-topproductdto-shims.md`.

**Plan summary (9 tasks):**

1. **Pre-flight scan** — repo-wide grep to confirm the three-file mental model (DTO + generated client + one test fixture) has no missed consumers.
2. **Delete shims** — remove `ProductCode`/`ProductName` getters + comment from `TopProductDto.cs`, `dotnet build` + `dotnet format`.
3. **Regenerate TS client** — `npm run generate-client` from `frontend/`, with `dotnet msbuild ... GenerateFrontendClientManual` fallback.
4. **Verify generated diff** — confirm `api-client.ts` diff is scoped to `TopProductDto` / `ITopProductDto` only; reset and retry if drift appears.
5. **Update test fixture** — rename two keys in the `topProducts` array of `ProductMarginSummary.test.tsx` (lines 67–68 only); guardrails to keep `MonthlyProductSegmentDto` entries on lines 36–37 / 49–50 untouched.
6. **Backend tests** — `dotnet test` against the full suite.
7. **Frontend lint + build + tests** — `npm run lint`, `npm run build`, targeted Jest run, then full `npm test`.
8. **UI smoke check** — confirm `ProductMarginSummary.tsx` reads canonical fields via grep; optional dev-server run if uncertain.
9. **Atomic commit** — stage exactly the three files, single conventional-commit message noting the out-of-scope sort-key follow-up.

Covers all five FRs and all four arch-review spec amendments. Each step has the exact command, expected output, and full code blocks where applicable — no placeholders.