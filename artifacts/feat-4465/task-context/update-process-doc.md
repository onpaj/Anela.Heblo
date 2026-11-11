### task: update-process-doc
**Files:** `docs/processes/feed-marketing-action-to-outlook.md`.

- [ ] Replace the "Folder type labels do not match enum names" quirk with the new mapping (Images=Obrázky, Texts=Texty, Videos=Videa, Graphics=Grafika, Other=Ostatní; ints 0-3, 99).
- [ ] Bump `verified_at`; run `python3 scripts/process-docs/check.py index`.
- [ ] Final check: `grep -rnE "MarketingFolderType\.(General|Seasonal|ProductLine|Campaign)" backend frontend/src` returns nothing; commit.
