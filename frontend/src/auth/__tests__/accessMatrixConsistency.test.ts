import { readFileSync } from "fs";
import { resolve } from "path";
import { ACCESS_ROUTES } from "../accessMatrix.generated";

/**
 * Bidirectional consistency check between access-matrix.json's menuPaths
 * (surfaced as ACCESS_ROUTES) and the routes actually mounted in App.tsx.
 *
 * Keys prefixed with '#' are virtual identifiers for external onClick items
 * (terminal, hangfire, baleni-external) — they intentionally have no React
 * Router route, so they are excluded from the App.tsx side of the check.
 *
 * "Guarded" means wrapped in guard() or directly in <RequireMenuPath path="...">.
 */
describe("access matrix ↔ App.tsx consistency", () => {
  const appSource = readFileSync(
    resolve(__dirname, "../../App.tsx"),
    "utf-8",
  );

  const guardedRoutes = [
    ...Array.from(
      appSource.matchAll(/guard\(\s*["']([^"']+)["']/g),
      (m) => m[1],
    ),
    ...Array.from(
      appSource.matchAll(/RequireMenuPath\s+path=["']([^"']+)["']/g),
      (m) => m[1],
    ),
  ];

  const matrixKeys = Object.keys(ACCESS_ROUTES);

  it("every guard() call in App.tsx has an ACCESS_ROUTES entry", () => {
    const missing = guardedRoutes.filter((r) => !(r in ACCESS_ROUTES));
    if (missing.length > 0) {
      throw new Error(`App.tsx guards routes not present in access-matrix.json: ${missing.join(", ")}`);
    }
    expect(missing).toHaveLength(0);
  });

  it("every non-virtual ACCESS_ROUTES key is guarded in App.tsx", () => {
    const stale = matrixKeys
      .filter((k) => !k.startsWith("#"))
      .filter((k) => !guardedRoutes.includes(k));
    if (stale.length > 0) {
      throw new Error(`access-matrix.json declares routes not guarded in App.tsx: ${stale.join(", ")}`);
    }
    expect(stale).toHaveLength(0);
  });

  it("at least one guarded route exists (regression guard)", () => {
    expect(guardedRoutes.length).toBeGreaterThan(0);
  });
});
