# validate r1
- `npm run build` (NODE_OPTIONS=--max-old-space-size=6144): success.
- `npm run lint`: repo-wide reports 267 problems in files not touched by this change (pre-existing); eslint on the two changed marketing files is clean.
- Marketing tests: src/components/marketing 42 suites / 464 tests pass; MarketingCalendarPage 27 pass.
- No process-doc change needed.
