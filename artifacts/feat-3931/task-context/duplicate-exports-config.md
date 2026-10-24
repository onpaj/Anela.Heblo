### task: duplicate-exports-config

Settle the `Name|default` convention via config (FR-5).

Files: `frontend/knip.json`, optionally a short note in `docs/architecture/development_guidelines.md` only if a suitable section exists.

- [ ] Add `"rules": { "duplicates": "off" }` (or the equivalent `ignoreIssues`/`exclude: ["duplicates"]` supported by the installed knip version; check `npx knip --help` / knip docs for the installed version).
- [ ] Add a note recording the decision (knip.json is JSON, so put the rationale in the PR description and, if appropriate, the docs note).
- [ ] Run knip; the duplicates section must be empty.
- [ ] Commit: `chore(frontend): allow intentional named+default dual exports in knip (#3931)`.

Verify: duplicates section empty; no source files changed.

