Self-review against the spec:

**Spec coverage** — FR-1 (use generated method) → Task 2.1; FR-2 (preserve queryKey/enabled/options + signature) → Task 2.1 explicit table, Definition of Done; FR-3 (match peer pattern) → Task 2.1 mirrors `useOrgChart.ts` exactly; NFR-1 (no `as any`, lint clean) → Task 3.2, DoD; NFR-2 (behavioural parity) → Task 2.1 table, DoD; NFR-3 (tests) → Task 1 (test migration), addresses arch-review Amendment 2; NFR-4 (build + lint) → Tasks 3.2 and 3.3. Arch-review amendments 1-4 all internalised in "Spec amendments to internalise".

**Placeholder scan** — no TBDs, no "implement appropriate X", all code blocks complete.

**Type consistency** — `useResponsiblePersonsQuery`, `GetGroupMembersResponse`, `userManagement_GetGroupMembers`, `QUERY_KEYS.userManagement` used consistently throughout.

Plan saved to `artifacts/feat-arch-review-usermanagement-useusermanage/plan.r1.md`. It decomposes the refactor into three TDD-ordered tasks (failing tests first, refactor, validate) with exact file paths, full code, and explicit out-of-scope guardrails to keep the change surgical.