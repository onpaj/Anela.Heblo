`design.r1.md` written to `artifacts/feat-phase-2-leaflet-generation-persistence-f/design.r1.md`.

Here's what it covers:

**UX/UI Design** — ASCII wireframes for the leaflet result panel in all three feedback states (idle, success, already-submitted). Component hierarchy showing `LeafletGenerateTab → LeafletResult → RagFeedbackForm` and the KB tab refactor. `RagFeedbackForm` prop contract `{ onSubmit, isSubmitting, alreadySubmitted, isSuccess }`. State flow in `LeafletGenerateTab` showing when `generationId` resets (on each new generation attempt) vs. when it's populated (on success only).

**Component Design** — Frontend: `RagFeedbackForm` (new, no feature imports), `LeafletResult` (adds `generationId?` prop, owns mutation + feedback state), `LeafletGenerateTab` (adds `generationId` state, wires response), `KnowledgeBaseSearchAskTab` (replaces inline form). Backend: `LeafletGenerationLoggingBehavior` (call `next()` outside try/catch, use `CancellationToken.None` for save), `SubmitLeafletFeedbackHandler` (404→403→409 guard sequence), `GetLeafletFeedbackListHandler`, `GetLeafletGenerationHandler`, repository additions, module registration, and the three controller actions.

**Data Schemas** — Full `public."LeafletGenerations"` table with all columns and index DDL. EF Core configuration with schema and filtered index. All DTO classes (never records). Complete request/response JSON shapes for all three endpoints. `ErrorCodes` additions `2502`–`2504`. Frontend types and i18n keys for both locales.