# Review: trim-changelog-and-remaining-exports r1

Self-review in the orchestrating session (subagent tooling unavailable). Checked: knip clean, tsc clean, build OK, eslint clean on changed files, tests for touched dirs pass. Only removals of `export`, dead symbols and orphaned imports; no behaviour change. The `@public` annotation on two authRecovery functions is justified (dynamic require in tests) and narrow.

**Status:** PASS
