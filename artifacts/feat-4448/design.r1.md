# Design: Dark-mode support for LeafletUploadTab

## UX/UI Design
No layout change. Dark appearance on Graphite theme:

```
+- drop zone: bg graphite-surface, dashed border graphite-border (hover/drag: graphite-accent) -+
|   [Upload icon faint]  "Přetáhněte soubory sem" (graphite-text)                              |
|   "nebo" (muted)  "Vybrat soubory" (graphite-accent)  hint (faint)                           |
+-----------------------------------------------------------------------------------------------+
+- file list: border/dividers graphite-border ------------------------------------------------+
| [FileText faint] filename (graphite-text)        status label   [X faint -> text on hover]  |
+-----------------------------------------------------------------------------------------------+
[Nahrát vše (unchanged blue)]  [Zrušit vše: graphite-border, graphite-text, hover white/5]
```

## Component Design
Single component, className edits only. Class mapping (light classes kept, dark appended):

| Element | Added dark classes |
|---|---|
| status: uploading / done / error / waiting | `dark:text-blue-400` / `dark:text-emerald-300` / `dark:text-red-400` / `dark:text-graphite-faint` |
| drop zone default | `dark:border-graphite-border dark:hover:border-graphite-accent dark:bg-graphite-surface` |
| drop zone dragOver | `dark:border-graphite-accent dark:bg-graphite-accent/10` |
| Upload icon (drop zone) | `dark:text-graphite-faint` |
| "Přetáhněte soubory sem" | `dark:text-graphite-text` |
| "nebo" | `dark:text-graphite-muted` |
| "Vybrat soubory" | `dark:text-graphite-accent` |
| hint | `dark:text-graphite-faint` |
| file list container | `dark:border-graphite-border dark:divide-graphite-border` |
| FileText icon | `dark:text-graphite-faint` |
| filename | `dark:text-graphite-text` |
| remove button | `dark:text-graphite-faint dark:hover:text-graphite-text` |
| "Zrušit vše" | `dark:border-graphite-border dark:text-graphite-text dark:hover:bg-white/5` |

## Data Schemas
None.
