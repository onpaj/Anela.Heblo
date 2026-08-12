import { readFileSync } from "fs";
import { resolve } from "path";

/**
 * The nightly E2E workflow's module matrix is hand-maintained and only used by the
 * nightly job — the suite runs nowhere else (no PR CI). A Playwright project added to
 * playwright.config.ts without a matching workflow matrix entry is therefore dead code:
 * it never executes anywhere, silently, even though the workflow carries a comment
 * saying to keep the two in sync. This test is that sync check, made automatic instead
 * of relying on someone remembering to update both places by hand.
 */
describe("E2E module sync: playwright.config.ts projects ↔ nightly workflow matrix", () => {
  const playwrightConfigSource = readFileSync(
    resolve(__dirname, "../../playwright.config.ts"),
    "utf-8",
  );
  const workflowSource = readFileSync(
    resolve(__dirname, "../../../.github/workflows/e2e-nightly-regression.yml"),
    "utf-8",
  );

  const configProjectNames = Array.from(
    playwrightConfigSource.matchAll(/^\s*name:\s*'([^']+)',?\s*$/gm),
    (m) => m[1],
  );

  // The matrix line embeds two JSON array string literals: the real per-module list
  // (used for scheduled/parallel runs) and the '["all"]' fallback for a sequential
  // manual run. Only the former should track playwright.config.ts's projects.
  const matrixArrayLiterals = Array.from(
    workflowSource.matchAll(/'(\[[^\]]+\])'/g),
    (m) => JSON.parse(m[1]) as string[],
  );
  const workflowModuleNames = matrixArrayLiterals.find(
    (arr) => !(arr.length === 1 && arr[0] === "all"),
  );

  it("finds at least one project in playwright.config.ts (regression guard)", () => {
    expect(configProjectNames.length).toBeGreaterThan(0);
  });

  it("finds the per-module matrix array in the nightly workflow (regression guard)", () => {
    expect(workflowModuleNames).toBeDefined();
    expect(workflowModuleNames!.length).toBeGreaterThan(0);
  });

  it("every playwright.config.ts project has a matching nightly workflow matrix entry", () => {
    const missing = configProjectNames.filter((name) => !workflowModuleNames!.includes(name));
    if (missing.length > 0) {
      throw new Error(
        `playwright.config.ts declares project(s) not present in the nightly workflow matrix ` +
          `(.github/workflows/e2e-nightly-regression.yml): ${missing.join(", ")}. ` +
          `Without a matrix entry these tests never run anywhere — add them to the matrix ` +
          `array (and to MODULES in scripts/run-playwright-tests.sh).`,
      );
    }
    expect(missing).toHaveLength(0);
  });

  it("every nightly workflow matrix entry has a matching playwright.config.ts project", () => {
    const stale = workflowModuleNames!.filter((name) => !configProjectNames.includes(name));
    if (stale.length > 0) {
      throw new Error(
        `The nightly workflow matrix (.github/workflows/e2e-nightly-regression.yml) references ` +
          `module(s) with no matching project in frontend/playwright.config.ts: ${stale.join(", ")}. ` +
          `Remove the stale entry or add the missing project.`,
      );
    }
    expect(stale).toHaveLength(0);
  });
});
