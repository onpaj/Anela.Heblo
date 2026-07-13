## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

No leftover `file.Length` reference — it was only used for the removed assignment, matching FR-2's acceptance criteria. The change is a clean, complete dead-code removal with no other call sites, no test impact, and no external contract change.
