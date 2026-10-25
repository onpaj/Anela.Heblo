### task: validate
Run final validation.

Steps:
1. `cd frontend && npm run build && npm run lint`.
2. Run the marketing test folder and confirm green.
3. Note in the PR description that `MobileAgendaView.tsx:72` has the same pass-through but only feeds calendar display and is intentionally unchanged.

Acceptance: build, lint and tests pass. No process-doc change is needed because there is no sync, calculation or data-feed change.
