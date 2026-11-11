# add-dark-variants r1

Added dark: variants to every light-only color class in frontend/src/features/leaflet-generator/LeafletUploadTab.tsx per the task context (status labels, drop zone, file list, remove button, "Zrusit vse"). "Nahrat vse" unchanged. All light classes untouched (diff only appends). Graphite tokens verified in frontend/tailwind.config.js.
Build/lint not run (node_modules absent in worktree; className-only change).
