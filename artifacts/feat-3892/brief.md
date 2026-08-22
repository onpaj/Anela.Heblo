**File:** `docs/routines/daily-arch-review.md`

**What it claims:** a Claude Code cloud routine (`trig_01TDp4EDif36TJBkrMR2R7v2`) that selects today's module **deterministically** — `modules[(dayOfYear - 1) % 29]` (line 20) — rotating over a fixed list of **29** business modules (lines 22-40), and labels issues from `tech-debt, refactoring, code-quality, architecture, complexity` (line 55).

**What actually runs today:**
- `docs/architecture/module-map.md` (added 2026-07-30, `6bc4bafd`) has **52** parts across four groups (business domain, platform/cross-cutting, integration adapters, delivery/tooling), not 29 flat business modules.
- `.claude/skills/arch-review/pick-module.sh` (added 2026-08-04, `505fb82c`) selects a part by **uniform random draw with replacement**, per the skill's own notes in `.claude/skills/arch-review/SKILL.md` ("Selection is uniform random with replacement... some parts drawn several times before others are drawn once"). That is the opposite of the doc's deterministic day-of-year rotation, which by construction cannot repeat a module within a 29-day cycle.
- The reviewer persona `.agents/arch-review.md:167-170` mandates exactly one topical label from `architecture, tech-debt, maintainability, design-patterns, antipattern, code-quality, duplication, documentation` and one severity from `critical, major, moderate, minor`. A real filed issue confirms this (`gh issue view 3891` → labels `major, antipattern, agent, arch-review`). The doc's `refactoring` and `complexity` labels aren't in the current allowlist — using them today would have them silently dropped.

**Why it matters:** this doc is the operational reference for debugging/tuning the automated review routine. Someone following it to understand observed behavior — e.g. why the same part was reviewed twice in a short span — would reach the wrong conclusion, because the doc describes a rotation that structurally cannot repeat within a cycle while the real mechanism can and does. It also documents a routine ID and label set that no longer match what the caller (`.claude/skills/arch-review/SKILL.md` step 5c) actually enforces.

**Suggested direction:** rewrite the doc to describe the current map-driven, random-with-replacement mechanism (module-map.md + pick-module.sh + the arch-review skill/persona), or retire it if the cloud routine `trig_01TDp4EDif36TJBkrMR2R7v2` itself has been superseded by whatever is invoking this skill now.

