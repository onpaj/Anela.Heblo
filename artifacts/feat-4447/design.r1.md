# Design: Leaflet dark-mode variants

## UX/UI Design
No layout change. In dark mode: labels, legends and radio text use `graphite-text`; character counter uses `graphite-muted`; the topic input uses `graphite-surface-2` background, `graphite-border` border, `graphite-text` text and `graphite-faint` placeholder; result action buttons get `graphite-border` border, `graphite-text` text and `white/5` hover.

## Component Design
`LeafletForm` and `LeafletResult` keep their props and structure. Only className strings are extended, appending dark classes after the existing light classes.

## Data Schemas
None.
