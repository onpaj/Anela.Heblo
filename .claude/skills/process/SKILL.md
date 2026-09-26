---
name: process
description: Answer "where does this data come from / how is this calculated / when does this update" questions about Heblo from the agent-facing process docs. Use when the user says "/process", asks about the origin of a number, a sync, a calculation, a data feed, or how a Heblo process works.
---

# Process docs lookup

Heblo's syncs, calculations and data feeds are documented for agents in `docs/processes/`.

1. Read `docs/processes/INDEX.md` and pick the process(es) that match the question.
2. Read the full doc(s). Follow `related:` links when the question is about an upstream source
   (e.g. a cost used in a margin comes from another process).
3. Answer from the doc. Cite the doc name and its `verified_at` commit. Treat *Runtime facts* as true
   only as of the date written next to each fact — say so if it matters for the answer.
4. If the user wants to go deeper, open the files under *Code entry points*.
5. If no doc covers the question, or the doc contradicts the code you read, say so explicitly and
   offer to write/fix the doc (template: `docs/processes/_TEMPLATE.md`; then run
   `python3 scripts/process-docs/check.py index`).

Non-developers get the same docs through the Heblo MCP tools `ListProcesses` and `GetProcessDoc`.
