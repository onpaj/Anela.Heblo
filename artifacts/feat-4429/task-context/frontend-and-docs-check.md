### task: frontend-and-docs-check
Grep `frontend/src` (`api/hooks/useTransportBoxes.ts`, `components/terminal`, `components/transport`) for logic branching on literal HTTP 400 from the five endpoints; none is expected (hooks throw on any non-ok); fix only if found. Update `docs/processes/flow-transport-box.md`: note failures map to HTTP status via ErrorCodes, bump `verified_at`, then run `python3 scripts/process-docs/check.py index`.
Final validation: `cd backend && dotnet build && dotnet format --verify-no-changes` and the TransportBox tests pass.
