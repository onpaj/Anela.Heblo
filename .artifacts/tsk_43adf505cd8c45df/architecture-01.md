# Architecture review — Remove the phantom backend C# client pipeline

## Verdict

**Approved, with one required fix to the verification command in design-01.md before implementation.** Every line reference, GUID, and grep claim in plan-01.md and design-01.md was independently re-verified against the current tree (2026-07-31) and matches exactly — `.sln` project block, `ProjectConfigurationPlatforms` block, `NestedProjects` line, the `Condition="false"` disabled PostBuild target, and all doc line numbers in `api-client-generation.md`, `filesystem.md`, `module-map.md`. The delete-not-build decision is correct per the codebase's anti-speculative-design rule (`CLAUDE.md`: "Don't design for hypothetical future requirements"). One gap found: the design's own verification grep will not return empty as claimed, because a repo-documented data directory was missed by its exclusion pattern.

## Alignment with existing patterns and invariants

- **Module ownership boundary (`module-map.md` #52) is the deciding invariant here, and the design missed one directory it covers.** `module-map.md:1100` explicitly owns `artifacts/` (no leading dot) *and* `.artifacts/` together as "pipeline run outputs... treat as data, not source." Both plan-01.md's "Out of scope" list and design-01.md's Cross-file verification grep (lines 199–208) exclude only `.artifacts/` and `docs/superpowers/plans/` — they never mention the root-level `artifacts/` directory. That directory is git-tracked and currently contains six files with live phantom references (`artifacts/feat-3487/...`, `artifacts/feat-3446/design.r1.md`, `artifacts/feat-arch-review-*/...`). Confirmed via direct grep:
  ```
  ./artifacts/feat-3487/impl/regenerate-openapi-clients-and-verify.r1.md
  ./artifacts/feat-3487/task-context/regenerate-openapi-clients-and-verify.md
  ./artifacts/feat-3487/task-plan.r1.md
  ./artifacts/feat-3487/arch-review.r1.md
  ./artifacts/feat-arch-review-logistics-changetransportbox/task-plan.r1.md
  ./artifacts/feat-arch-review-logistics-changetransportbox/arch-review.r1.md
  ./artifacts/feat-arch-review-analytics-ireportbuilderserv/impl/main.r1.md
  ./artifacts/feat-arch-review-analytics-ireportbuilderserv/arch-review.r1.md
  ./artifacts/feat-arch-review-journal-journalentrydto-carr/task-plan.r1.md
  ./artifacts/feat-arch-review-analytics-marginlevel-is-str/arch-review.r1.md
  ./artifacts/feat-3446/arch-review.r1.md
  ./artifacts/feat-3446/design.r1.md
  ```
  These are frozen pipeline-run records, the same category `module-map.md` already tells readers to treat as data — editing them would violate the same "don't touch historical artifacts" boundary the plan correctly applies to `.artifacts/` and `docs/superpowers/plans/`. But the design's verification command's exclude regex (`grep -v '^\./.artifacts/\|^\./docs/superpowers/plans/'`) does not match `./artifacts/...` (that pattern requires a literal `.` immediately after `./`, which `artifacts/` doesn't have). Run as written, step 7/the Cross-file verification will report 12 non-empty hits and contradict its own "Expected: empty output" — implementers will either misdiagnose it as incomplete work or (worse) start editing frozen pipeline artifacts to silence it.
  **Fix required before implementation:** widen the exclude pattern to also skip `artifacts/`, e.g. `grep -v '^\./\.\?artifacts/\|^\./docs/superpowers/plans/'` or simply list both directories explicitly. This is a one-line correction to the verification command in design-01.md step/Cross-file verification section — no other part of the design changes.

- **`.sln` structure invariant** — confirmed the project block is exactly two lines (`Project(...)"..."EndProject`), bordered by `Anela.Heblo.Application` above and `Anela.Heblo.Persistence` below, matching design-01.md's diff precisely. `ProjectConfigurationPlatforms` has all 12 lines for the GUID, `NestedProjects` has exactly one line. No other GUID or solution folder is touched. Safe, minimal, matches how a normal `dotnet sln remove` / manual `.sln` edit looks in this repo.

- **DTO/contract rules (`development_guidelines.md`, `CLAUDE.md`)** — not implicated. This change touches no DTOs, no API contracts, no generated code paths that are actually live. The real, working frontend TypeScript pipeline (`GenerateFrontendClientManual`, `nswag.frontend.json`, `scripts/regenerate-api-client.sh`) is untouched, as required.

- **Part #42 ownership in `module-map.md`** — the design's edits (drop the "two clients (C# and TypeScript)" framing at line 940, drop the `Anela.Heblo.API.Client/` owned-path line 944, drop its half of the "deliberately unassigned... generated" note at line 1110) are internally consistent with the rest of that doc's format (bullet lists, `**Owns:**` sections) and don't touch the numbering or cross-references (`**Consumed by:**`) of adjacent parts.

- **No CI/build-matrix/Dockerfile reference** to `Anela.Heblo.API.Client` exists — checked `.github/`, root `Dockerfile`, `backend/src/Anela.Heblo.API/Dockerfile`, `.editorconfig`, `global.json`. The project is invisible to the build pipeline beyond the `.sln` entry itself, so its removal has no infrastructure blast radius.

- **`nswag-templates/README.md`** (referenced from the doc at line 250) does not itself repeat the phantom backend-client claim — checked, clean. No additional live doc site beyond the three plan-01.md already identified.

## Proposed architecture

No architecture change — this is a subtractive correction restoring doc/repo consistency. Confirmed decision: **abandon**, not restore. Rationale already established in plan-01.md and re-confirmed here: two years of git history with zero real usage, no genuine server-to-server caller (backend integration tests use in-process `WebApplicationFactory`, not HTTP), and the documented generation mechanism appears to have never been implemented rather than having decayed — building it now would be exactly the kind of speculative infrastructure `CLAUDE.md` tells us not to build.

## Implementation guidance

Follow plan-01.md's Rough Plan and design-01.md's Component Design sections 1–5 as written — every diff in design-01.md was checked character-for-character against the live files and is correct. Sequencing:

1. Delete `backend/src/Anela.Heblo.API.Client/` (both git-tracked files: `.csproj`, `Generated/.gitkeep`).
2. Edit `Anela.Heblo.sln` per design-01.md section 2 (three edits, all keyed on GUID `3D738B41-81F8-4743-8F64-98790902405F`).
3. `dotnet build` from repo root — confirms the trimmed `.sln` parses and nothing referenced the deleted project (nothing does, per repo-wide grep).
4. Edit the three docs per design-01.md sections 3–5.
5. **Run the corrected cross-file verification** — apply the exclude-pattern fix above before running it, so the "expected empty" check is actually accurate:
   ```bash
   grep -rn "GenerateBackendClient\|swagger2csclient\|AnelaHebloApiClient\|Anela.Heblo.API.Client" \
     --include="*.md" --include="*.sln" --include="*.csproj" --include="*.cs" . \
     | grep -v '^\./\.\?artifacts/\|^\./docs/superpowers/plans/'
   ```
6. `dotnet build` + `dotnet format` (BE gate per `CLAUDE.md`). FE build/lint are unaffected (no FE file touched) but cheap to run as a sanity check.

## Risks and mitigations

- **Risk:** running the Cross-file verification command exactly as written in design-01.md produces 12 false-positive hits in `artifacts/`, which could be mistaken for leftover work.
  **Mitigation:** use the corrected exclude pattern above (already folded into the guidance section). No code or doc change needed to eliminate the risk — it's a one-line fix to a verification command, not to the deletion/doc-edit plan itself.
- **Risk:** none identified around the `.sln`/`.csproj` deletion itself — zero external references confirmed by repo-wide grep excluding the project's own directory, matching plan-01.md's claim.
- **Risk:** none around doc edits — all target line ranges were re-read from the live files in this review and match the diffs in design-01.md exactly, so there's no drift between when design-01.md was written and now.

## Prerequisites before implementation begins

None. The design is implementation-ready as-is, modulo the one verification-command fix noted above (apply it during step 5, not as a blocking prerequisite).
