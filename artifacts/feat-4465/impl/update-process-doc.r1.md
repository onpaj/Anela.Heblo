# update-process-doc r1
- Replaced the "Folder type labels do not match enum names" quirk in docs/processes/feed-marketing-action-to-outlook.md with the new enum-to-label mapping (Images=0 Obrázky, Texts=1 Texty, Videos=2 Videa, Graphics=3 Grafika, Other=99 Ostatní).
- Bumped verified_at; ran `python3 scripts/process-docs/check.py index` (no INDEX change).
- `grep -rnE "MarketingFolderType\.(General|Seasonal|ProductLine|Campaign)" backend frontend/src` returns nothing.
