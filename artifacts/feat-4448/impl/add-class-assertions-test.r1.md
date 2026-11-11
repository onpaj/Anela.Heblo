# add-class-assertions-test r1

Added a `dark mode classes` describe block to frontend/src/features/leaflet-generator/__tests__/LeafletUploadTab.test.tsx: drop zone default (dark:border-graphite-border, dark:bg-graphite-surface), dragOver (dark:border-graphite-accent, dark:bg-graphite-accent/10), and "Čeká" status label (dark:text-graphite-faint). Test-only change. Jest run (CI=true react-scripts test; filter was ignored by the runner so the whole suite ran): 422 suites passed, 3741 tests passed.
