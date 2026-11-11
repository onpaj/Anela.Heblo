# Architecture review
- Change is local to a private static helper call within one service; no layering, DTO, contract or API impact.
- Helper is already in the same class and has the identical expression, so the swap is behaviour-preserving.
- Process docs: no behaviour change, so no docs/processes update required (optionally bump verified_at if a doc owns this file and CI demands it).
- Verdict: approved, no risks.
