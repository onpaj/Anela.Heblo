# Review: migrate-manufacture-tests-to-snapshot (r1)

- No assertions weakened; changes are type/fixture substitutions plus the two forced adaptations (difficulty value, init-only Planned).
- Adapter tests were strengthened to check mapping rather than reference identity.
- Remaining failures are environmental (Docker) or owned by the next task (module boundary rule).

**Status:** PASS
