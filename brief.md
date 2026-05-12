# Coverage Gap Routine — Design Spec

**Date:** 2026-05-12  
**Status:** Approved  

## Problem

Raw line coverage numbers are a poor quality signal on their own. A controller that calls `mediator.Send()` and a handler with five business-rule branches both contribute lines, but only one benefits from tests. A routine that blindly flags "module X is at 54%" would produce noise — coverage for coverage's sake.

## Goal

A weekly remote Claude Code routine that:
1. Uses CodeCov coverage data (via GitHub Actions artifacts) to identify numerically low-coverage files
2. Reads the actual source code for each candidate
3. Qualifies whether the gap represents missing verification of real logic
4. Files GitHub issues only for meaningful gaps, with specific, actionable suggestions

## Architecture

### Part 1 — CI workflow change

File: `.github/workflows/ci-main-branch.yml`

Two `actions/upload-artifact` steps added immediately after the corresponding CodeCov upload steps:

**After backend CodeCov upload:**
```yaml
- name: 📦 Persist backend coverage artifact
  uses: actions/upload-artifact@v4
  with:
    name: coverage-backend
    path: ./coverage/**/*.cobertura.xml
    retention-days: 7
```

**After frontend CodeCov upload:**
```yaml
- name: 📦 Persist frontend coverage artifact
  uses: actions/upload-artifact@v4
  with:
    name: coverage-frontend
    path: ./frontend/coverage/lcov.info
    retention-days: 7
```

7-day retention is sufficient — the routine runs weekly and always uses the latest successful run.

### Part 2 — Remote Claude Code routine

**Schedule:** Weekly, Monday 6am Prague CEST (`0 4 * * 1` UTC)  
**Model:** `claude-sonnet-4-6`  
**Allowed tools:** `Bash`, `Read`, `Glob`, `Grep`  
**Repo:** `https://github.com/onpaj/Anela.Heblo`

#### Routine flow

```
1. Find latest successful main-branch CI run
       gh run list --workflow=ci-main-branch.yml --branch=main --status=success

2. Download both coverage artifacts
       gh run download <run-id> --name coverage-backend --dir /tmp/cov-be
       gh run download <run-id> --name coverage-frontend --dir /tmp/cov-fe

3. Parse backend Cobertura XML
       Extract line-rate per class/file
       Group by Feature module (parse path: Features/<MODULE>/...)
       Collect files where line-rate < 0.60

4. Parse frontend LCOV
       Parse SF:/LH:/LF: records
       Compute line coverage per file
       Group by src/ subfolder
       Collect files where coverage < 60%

5. For each low-coverage file:
       Read the actual source file
       Qualify: does this gap represent untested meaningful logic?

6. For qualifying gaps:
       Check for duplicate open issues
       File GitHub issue

7. Output summary
```

#### Coverage threshold

Initial filter: **< 60% line coverage** per file. This is deliberately low — the goal is to surface candidates for Claude to read, not to enforce a hard number. Claude's qualitative judgment is the real gate.

#### Qualification logic (Step 5)

Claude reads each low-coverage file and answers: *would a test for the uncovered lines actually verify meaningful behavior?*

**Skip (do not file issue):**
- MVC controllers that only call `_mediator.Send()` or `_mediator.Publish()`
- DTO / request / response classes with no behavior
- Startup, configuration, DI registration code
- Simple property accessors, auto-mapped fields
- Trivial passthrough methods

**File issue (meaningful gap):**
- Handlers with multiple conditional branches (validation, business rules)
- Domain logic with state transitions or status flows
- Business calculations (financial totals, margins, stock quantities)
- Error / exception paths where the failure shape is never asserted
- Cross-module service contracts where the integration is untested

#### GitHub issue format

```
Title: [coverage-gap] <Module/File>: <specific untested logic description>
Labels: coverage-gap, tech-debt

## Module / File
<path>

## Coverage
Line coverage: X% (filter threshold: 60%)

## What's not tested
<Specific description — which branches, which conditions, which error paths>

## Why it matters
<What could silently break if this logic regresses>

## Suggested approach
<Concrete: what type of test (unit/integration), what scenario to cover, ~effort estimate>

---
_Filed by weekly coverage-gap routine on <date>. Based on CI run #<id>._
```

#### Labels

- `coverage-gap` — created by routine if absent (`#e99695`)
- `tech-debt` — already exists in repo

#### Deduplication

Before filing, check:
```bash
gh issue list --repo onpaj/Anela.Heblo --label coverage-gap --state open \
  --search "<file-or-module> in:title" --limit 20
```
Skip if a matching open issue already exists.

## What this does NOT do

- Does not run tests or measure coverage itself — it consumes what CI already produces
- Does not enforce a hard coverage threshold — Claude's judgment is the gate
- Does not file issues for coverage that doesn't serve a purpose
- Does not make code changes

## Files touched

| File | Change |
|---|---|
| `.github/workflows/ci-main-branch.yml` | Add 2 `upload-artifact` steps (~10 lines) |
| `docs/routines/weekly-coverage-gap.md` | New routine documentation |
| Claude Code remote routine | New scheduled routine via RemoteTrigger API |
