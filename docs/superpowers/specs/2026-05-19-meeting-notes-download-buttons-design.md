# Design: Download Transcript & Summary Buttons — Meeting Notes Detail

**Date:** 2026-05-19  
**Status:** Approved

## Summary

Add two download buttons to the meeting notes detail page header so users can export the raw transcript and AI summary to their local machine — primarily for feeding into LLMs.

## Behaviour

| Button | Label | Output file | MIME type | Content |
|---|---|---|---|---|
| Download summary | Stáhnout souhrn | `{subject}-summary.md` | `text/markdown` | `transcript.summary` verbatim |
| Download transcript | Stáhnout přepis | `{subject}-transcript.txt` | `text/plain` | `transcript.rawTranscript` verbatim |

Both are pure-browser downloads — no API call, no server involvement.

## File Naming

The subject string is sanitised before use as a filename:
1. Trim whitespace
2. Collapse runs of whitespace to a single `-`
3. Strip characters that are not word chars (`\w`) or hyphens
4. Lowercase

Example: `"Schůzka s týmem Q2"` → `schůzka-s-týmem-q2-summary.md`

## Utility

A small shared utility `downloadTextFile(content: string, filename: string, mimeType: string): void` is extracted to `frontend/src/utils/downloadTextFile.ts`. It follows the same blob + anchor-click pattern used by the existing `exportToXlsx.ts`.

## Placement

Header action bar in `MeetingTaskDetailPage.tsx`. Button order (left → right):

```
[Status badge] [Access badge] [Stáhnout souhrn] [Stáhnout přepis] [Reimport] [Spravovat přístup]
```

Icons: `Download` from lucide-react.  
Style: same secondary button style as Reimport (`border border-gray-300 hover:bg-gray-50`).

## Edge Cases

- If `transcript.summary` is empty or whitespace-only, the **Stáhnout souhrn** button is hidden.
- If `transcript.rawTranscript` is empty or whitespace-only, the **Stáhnout přepis** button is hidden.

## Scope

- Frontend only — `MeetingTaskDetailPage.tsx` + new `downloadTextFile.ts` utility
- No backend changes
- No new API endpoints

## Out of Scope

- PDF export
- Bundled ZIP download
- Custom filename input
