### task: update-process-doc
Bump process doc for CI gate.
1. In `docs/processes/flow-transport-box.md` set `verified_at` to today; add the new interface path to owned code list if the front matter lists individual files.
2. Run `python3 scripts/process-docs/check.py index` and fix any reported issue.
**Acceptance:** check.py passes.
