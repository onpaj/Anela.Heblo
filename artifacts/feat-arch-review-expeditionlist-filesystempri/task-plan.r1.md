Plan saved to `docs/superpowers/plans/2026-06-10-relocate-filesystem-print-queue-sink-to-adapter.md`.

Summary: 9 tasks, each broken into 2–5 minute steps with full code, exact file paths, exact line numbers, and explicit expected outputs. The plan applies all six spec amendments flagged by the arch review (config-key names, sln path, line 428, Shoptet test csproj reference, no over-specified PackageReferences, docs wording) and adds:

- A "Source-of-truth facts" preamble so an executor following the spec literally cannot regress to the wrong key names or sln path.
- Task 3 commits an intentionally-broken build so the relocation is reviewable as a discrete event, then Tasks 5/6/7 each restore one consumer.
- Task 9 final-validation greps that pin the exact list of expected `FileSystemPrintQueueSink` reference sites (5) — a regression guard against a missed consumer.
- Task 5 also removes the now-unused `using Anela.Heblo.Application.Features.ExpeditionList.Services;` from the composition root (verified safe — no other `.Services` types are referenced in that file).