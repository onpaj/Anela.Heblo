# Merge review — PR #3843

**Title:** `[arch-review] AiAdapters: SerpApi retry pipeline is dead for 429/503 because EnsureSuccessStatusCode runs outside the Polly pipeline`
**Base:** `main` · **Head:** `harness/tsk_210556fdfd8d401a` · **Closes:** #3841
**Reported size:** 116 files, +4851 / −17

## Verdict: REJECT

The intended fix is correct and well-executed, but the branch carries a large, undisclosed, unrelated feature. Merging this PR would land ~4,600 lines that neither the PR body nor issue #3841 mention, unreviewed, into `main`. That is disqualifying for an unattended merge on scope grounds alone (review criterion 1), independent of code quality.

## What the PR *says* it does

Fix `SerpApiWebSearchClient` so the HTTP success check runs *inside* the Polly pipeline delegate (mirroring `AnthropicChatClient`), making the 429/503/network retry predicate reachable. Add a `BuildPipeline(TimeSpan)` test seam + optional ctor pipeline param, and 4 retry tests.

## What the diff *actually* contains

`git diff --name-only origin/main..HEAD`, grouped:

| Area | Files | Related to #3841? |
|------|-------|-------------------|
| `backend/.../SerpApiWebSearchClient.cs` | 1 | ✅ the fix |
| `backend/test/.../SerpApiWebSearchClientTests.cs` | 1 | ✅ the tests |
| `.artifacts/tsk_210556fdfd8d401a/**` | 5 | ⚪ harness pipeline artifacts (repo already tracks `.artifacts/`) |
| `docs/routines/test-health/**` | 107 | ❌ **unrelated feature** |
| `docs/superpowers/{plans,specs}/2026-08-02-test-health-routine*` | 2 | ❌ **unrelated feature** |

`origin/main` contains **zero** `docs/routines/test-health` files today, so all 109 test-health files land on merge. The PR body does not mention "test-health" a single time. The git log confirms a long series of `feat(test-health): …` commits interleaved on this branch ahead of the SerpApi work.

The test-health payload is not inert documentation — it includes executable shell scripts and an installer with real blast radius:
- `docs/routines/test-health/harness/install.sh`
- `docs/routines/test-health/{gh-api,rp-query,test-health-digest}.sh` (+ their `.test.sh`)
- `docs/routines/test-health/harness/test-health.{agent,process}.json`
- `docs/routines/test-health/test-health-digest.sh` (627 lines) and a 1,594-line plan.

None of this was reviewed under this task, none of it is scoped to the issue this PR closes, and it should ride on its own PR with its own review.

## The intended fix, on its merits (would pass in isolation)

Reviewed `SerpApiWebSearchClient.cs`:
- The status check now happens inside `_pipeline.ExecuteAsync(...)`; on a non-success response it throws `HttpRequestException(..., statusCode)`, so the existing `ShouldHandle` predicate (`429`, `503`, `null` status) is finally reachable. This genuinely fixes the dead-retry bug described in #3841. Previously `EnsureSuccessStatusCode()` ran *after* the pipeline, so no retry could ever occur.
- Mirrors the established `AnthropicChatClient` pattern (cited and corroborated by the prior architecture step).
- New ctor param `ResiliencePipeline? pipeline = null` is optional with a default. DI registration is `AddScoped<IWebSearchClient, SerpApiWebSearchClient>()` and no `ResiliencePipeline` is registered in the container; the built-in DI container honors constructor default values, so this resolves to `null → DefaultPipeline`. No DI breakage.
- Error body is logged and surfaced in the exception message — reasonable; SerpApi error bodies are not known to contain the API key, and the key travels as a query param, not in the response body.
- Tests use `BuildPipeline(TimeSpan.Zero)` for instant retries — a clean seam that keeps the suite fast.

Had this PR contained *only* those two files (+ its artifacts), it would be a straightforward approve.

## Why reject anyway

1. **Undisclosed unrelated scope (criterion 1).** 109 files of a separate feature would merge under the banner of a retry fix. "Unrelated scope is a reason to withhold, even when the code is good."
2. **Blast radius (criterion 3).** Executable installer/shell scripts and harness process/agent config land unreviewed. A wrong or malicious line in `install.sh` or the digest scripts would reach `main` with no human glance.
3. A rejection here costs one human glance; separating the test-health work into its own PR is the correct remedy and cheap.

## Verification performed

- Read the full `origin/main..HEAD` file list and categorized it.
- Confirmed `origin/main` has 0 `docs/routines/test-health` files → the feature is introduced, not pre-existing.
- Confirmed PR body / issue #3841 make no reference to test-health.
- Read the SerpApi source diff and test file; confirmed DI registration path.
- Attempted a local test run (dotnet 8). The full-solution build failed only on an environment-specific `NETSDK1177` apphost re-signing error (`Anela.Heblo.API` / `Anela.Heblo.AccessMatrixGen`, "apphost is already signed") — a known macOS host-toolchain quirk on this box, **not** caused by the diff. There were **0 `error CS` compile errors**, so the test summary did not print locally. Correctness of the change therefore rests on my independent read of the source + tests and the prior review/development steps, which report `dotnet build` clean and 24/24 WebSearch+Anthropic tests passing including the 4 new retry tests. This does not affect the verdict, which is on scope, not correctness.

```json
{"confidence": 0.12, "reasoning": "The intended SerpApi retry fix is correct and safe, but the branch also merges ~109 files of an entirely separate, undisclosed 'test-health' feature (including executable installer/shell scripts) that is not mentioned in the PR or issue #3841 and was not reviewed here — disqualifying unrelated scope and blast radius for an unattended merge.", "risks": ["109 unrelated docs/routines/test-health files (incl. install.sh and shell scripts) land on main unreviewed under the guise of a retry fix", "PR body/issue #3841 make no mention of the test-health routine — scope is undisclosed", "executable installer + harness config JSON carry blast radius that should get its own reviewed PR"]}
```
