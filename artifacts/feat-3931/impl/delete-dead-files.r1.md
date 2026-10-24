# delete-dead-files r1 -- implementation summary

Deleted 7 verified-dead files (zero importers in src/test/public; no own tests/stories):
useManualCatalogRefresh.ts, useRecalculatePurchasePrice.ts, authConfig.ts, weatherIcons.tsx (byte-identical .ts twin stays; consumers and its test import './weatherIcons'), KnowledgeBaseAskTab.tsx, KnowledgeBaseSearchTab.tsx, CreateManufactureOrderModal.tsx.

Docs updated (stale references): docs/processes/feed-purchase-price-recalculation.md (owns entry + prose), module-purchase.md, flow-knowledge-base-ask.md, docs/architecture/module-map.md. `check.py index` regenerated clean.

Knip "Unused files" now only featureFlags.ts and useFeatureFlag.ts (reserved for feature-flag-files task).
Verification: build passes; lint 267 problems (unchanged from baseline); tests 422 suites / 3738 passed, 5 skipped.
