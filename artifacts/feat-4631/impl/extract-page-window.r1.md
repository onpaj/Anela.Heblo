# extract-page-window r1
Created pageWindow.ts (getPageWindow per design), pageWindow.test.ts (12-case it.each table), and replaced the inline page-window computation in KnowledgeBaseDocumentsTab.tsx with getPageWindow(...).map. Button JSX unchanged.
Verification: jest knowledge-base suites 3/3 pass (32 tests); eslint clean; tsc clean for src (only pre-existing node_modules typing errors).
