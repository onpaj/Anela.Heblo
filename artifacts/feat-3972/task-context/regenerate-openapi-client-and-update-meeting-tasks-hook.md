### task: regenerate-openapi-client-and-update-meeting-tasks-hook

**Files:**
- Modify (auto-regenerated): `frontend/src/api/generated/api-client.ts`
- Modify: `frontend/src/api/hooks/useMeetingTasks.ts` (line 44, inside the `MeetingTranscriptDto` interface)

Reference files read to produce this task (do not modify):
- `useMeetingTasks.ts` — confirmed this file hand-rolls its **own** `MeetingTranscriptDto`
  TypeScript interface (lines 28-46) and fetches via a local `fetchJson` helper (lines 114-122)
  built on `getAuthenticatedApiClient()` + `${(apiClient as any).baseUrl}${path}` — it never
  imports from the generated `api-client.ts`, despite the stale `// TODO: migrate to generated
  client...` comment at line 1. This means regenerating the OpenAPI client alone does **not**
  surface the new field to either page — this task's manual interface edit is required in
  addition, not instead of, regeneration.
- `docs/development/api-client-generation.md` — confirmed `npm run generate-client` is the
  regeneration command, and that it also runs automatically as a `prebuild` step before `npm run
  build`.

This is a plumbing task (a generated-file regeneration plus a one-field interface addition) with
no meaningful unit to test-first — TDD is explicitly skipped here; correctness is verified by the
frontend build's type-checking in the next two tasks, which will fail to compile if this field is
missing when the page components reference it.

Steps:

- [ ] **Step 1: Regenerate the OpenAPI TypeScript client** (now that the backend `MeetingTranscriptDto`
  carries `TasksExtractionDegraded`, from the previous task):
  ```bash
  cd frontend
  npm run generate-client
  ```
  This updates the generated `MeetingTranscriptDto` class in `api-client.ts` for consistency (it
  is not directly consumed by `useMeetingTasks.ts`, but keeping it in sync avoids future drift for
  any consumer that does use the generated client).

- [ ] **Step 2: Add the field to the hand-written interface.**
  Edit `useMeetingTasks.ts` — insert after line 44 (`accessLevel: 'Private' | 'Public' | 'Restricted';`)
  and before line 45 (`accessGrants: MeetingAccessGrantDto[];`):
  ```typescript
    accessLevel: 'Private' | 'Public' | 'Restricted';
    accessGrants: MeetingAccessGrantDto[];
    tasksExtractionDegraded: boolean;
  ```
  (Field ordering here is not significant — appending after `accessGrants` and before the closing
  `}` of the interface, at what is currently line 46, is equally correct; keep it adjacent to the
  other transcript-level flags for readability.)

- [ ] **Step 3: Commit.**
  ```bash
  git add frontend/src/api/generated/api-client.ts frontend/src/api/hooks/useMeetingTasks.ts
  git commit -m "Expose tasksExtractionDegraded on the meeting transcript DTO/client"
  ```

---
