/**
 * Changelog toaster suppression for E2E tests.
 *
 * The app shows a "Co je nové v <version>" toaster whenever the frontend version is
 * newer than the one recorded in localStorage. It renders as
 * `<div class="fixed top-4 right-4 z-50 ...">` and, by design, never auto-hides
 * (see useChangelogToaster: "Do not start auto-hide - user must close manually").
 *
 * Every Playwright test gets a fresh browser context with empty localStorage, so the
 * toaster is always considered new and stays on screen for the whole test. It overlays
 * the top-right corner of the page, which is where filter/action controls usually live,
 * and intercepts their pointer events - clicks then never reach the real control.
 *
 * Rather than clicking "Zavřít" after each navigation (racy: the toaster appears only
 * once the changelog request resolves), we seed the version-tracking key with an
 * unreachably high version so `isNewVersion()` is false and the toaster never renders.
 */

// Must match VERSION_TRACKING_KEY in src/features/changelog/utils/version-tracking.ts
const VERSION_TRACKING_KEY = 'anela-heblo-version-tracking';

// Higher than any real release, so no published version ever compares as newer.
const UNREACHABLE_VERSION = '999.999.999';

/**
 * Register an init script that suppresses the changelog toaster on every page load
 * in this context. Must be called before the first navigation.
 */
export async function suppressChangelogToaster(page: any): Promise<void> {
  await page.addInitScript(
    ({ key, version }: { key: string; version: string }) => {
      try {
        localStorage.setItem(
          key,
          JSON.stringify({
            lastShownVersion: version,
            lastShownAt: new Date().toISOString(),
            seenVersions: [version],
          })
        );
      } catch {
        // localStorage unavailable (e.g. about:blank) - the toaster check will simply
        // fall back to its default behaviour; not worth failing the navigation over.
      }
    },
    { key: VERSION_TRACKING_KEY, version: UNREACHABLE_VERSION }
  );
}
