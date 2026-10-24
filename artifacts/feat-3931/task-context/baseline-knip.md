### task: baseline-knip

Establish a fresh baseline (the 2026-08-15 report is stale).

Files: none modified (scratch output only, in the scratchpad dir).

- [ ] `cd frontend && npm ci`
- [ ] `npm run knip 2>&1 | tee <scratchpad>/knip-baseline.txt`; also try `npx knip --reporter json` for machine-readable counts.
- [ ] Record per-section counts (files, deps, devDeps, unlisted, unresolved, exports, types, duplicates). Confirm `useTransportBoxTransitions.ts` is absent (covered by #3889).
- [ ] Confirm `npm run build`, `npm run lint`, `npm test -- --watchAll=false` pass before any change, so later failures are attributable.
- [ ] No commit (nothing changed).

Verify: baseline file exists; counts noted for the PR description.

