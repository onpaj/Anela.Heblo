/**
 * Shared ReportPortal configuration for the frontend test agents (Jest + Playwright).
 *
 * Opt-in: the agents stay dormant unless RP_ENABLE === 'true' AND both RP_API_KEY and
 * RP_ENDPOINT are present. That keeps local `npm test` / `npx playwright test` runs
 * completely offline — nothing tries to reach ReportPortal unless CI explicitly turns it on.
 *
 * Env contract (set by CI from GitHub variables/secrets — see reportportal/README.md):
 *   RP_ENABLE    master switch, must be exactly 'true'
 *   RP_ENDPOINT  RP REST base incl. /api/v1, e.g. https://rp.anela.cz/api/v1
 *   RP_API_KEY   API key minted in the RP UI
 *   RP_PROJECT   target project (default 'heblo')
 *   RP_LAUNCH    optional launch-name override
 *   RP_MODULE    optional module tag (E2E per-module matrix jobs set this)
 */

function rpEnabled() {
  return (
    process.env.RP_ENABLE === 'true' &&
    Boolean(process.env.RP_API_KEY) &&
    Boolean(process.env.RP_ENDPOINT)
  );
}

function buildConfig(layer, extra = {}) {
  const branch = process.env.GITHUB_HEAD_REF || process.env.GITHUB_REF_NAME;

  const attributes = [{ key: 'layer', value: layer }];
  if (process.env.RP_MODULE) {
    attributes.push({ key: 'module', value: process.env.RP_MODULE });
  }
  if (process.env.GITHUB_RUN_NUMBER) {
    attributes.push({ key: 'ci', value: process.env.GITHUB_RUN_NUMBER });
  }
  if (branch) {
    attributes.push({ key: 'branch', value: branch });
  }
  if (Array.isArray(extra.attributes)) {
    attributes.push(...extra.attributes);
  }

  return {
    apiKey: process.env.RP_API_KEY,
    endpoint: process.env.RP_ENDPOINT,
    project: process.env.RP_PROJECT || 'heblo',
    launch: process.env.RP_LAUNCH || extra.launch || `heblo-${layer}`,
    attributes,
    description: extra.description || '',
    restClientConfig: { timeout: 30000 },
  };
}

module.exports = { rpEnabled, buildConfig };
