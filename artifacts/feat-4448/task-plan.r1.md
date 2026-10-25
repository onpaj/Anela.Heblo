# Implementation Plan: Dark-mode support for LeafletUploadTab

Only `frontend/src/features/leaflet-generator/LeafletUploadTab.tsx` (and its test) change. Light classes stay byte-identical; `dark:` classes are appended. Class mapping is in `design.r1.md`.

### task: add-dark-variants
Edit `LeafletUploadTab.tsx`:
1. `statusLabel` spans: append `dark:text-blue-400` (uploading), `dark:text-emerald-300` (done), `dark:text-red-400` (error), `dark:text-graphite-faint` (waiting).
2. Drop zone: default branch append `dark:border-graphite-border dark:hover:border-graphite-accent dark:bg-graphite-surface`; dragOver branch append `dark:border-graphite-accent dark:bg-graphite-accent/10`.
3. Drop zone content: Upload icon `dark:text-graphite-faint`; primary text `dark:text-graphite-text`; "nebo" `dark:text-graphite-muted`; "Vybrat soubory" `dark:text-graphite-accent`; hint `dark:text-graphite-faint`.
4. File list: container `dark:border-graphite-border dark:divide-graphite-border`; FileText icon `dark:text-graphite-faint`; filename `dark:text-graphite-text`; remove button `dark:text-graphite-faint dark:hover:text-graphite-text`.
5. "Zrušit vše": `dark:border-graphite-border dark:text-graphite-text dark:hover:bg-white/5`. Leave "Nahrát vše" unchanged.

Verify: `grep -n "text-gray\|text-blue\|border-gray" LeafletUploadTab.tsx` shows each match has a `dark:` sibling; light strings unchanged in `git diff`.

### task: add-class-assertions-test
Extend `frontend/src/features/leaflet-generator/__tests__/LeafletUploadTab.test.tsx` with assertions that the drop zone (default and dragOver) and a status label carry the expected `dark:` classes. No behaviour/testid changes.

Verify: `cd frontend && npx jest LeafletUploadTab --watchAll=false` (or repo's test command) passes.

### task: validate-frontend
Run `cd frontend && npm run build && npm run lint` and the leaflet-generator tests. Optionally eyeball dark mode on the dev instance. No process-doc change needed (UI styling only).
