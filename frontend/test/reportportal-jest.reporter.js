/**
 * Thin wrapper around @reportportal/agent-js-jest so it can be used as a Jest reporter
 * passed on the CLI (`--reporters=./test/reportportal-jest.reporter.js`).
 *
 * Create React App forbids a `reporters` key in the package.json Jest config, so we can't
 * attach the RP reporter with its options there. Jest instantiates a CLI reporter with
 * `(globalConfig, reporterOptions)` and there's no way to pass options on the command line —
 * hence this wrapper, which subclasses the RP reporter and injects our env-derived config.
 *
 * CI only adds this reporter when ReportPortal is enabled (see ci-main-branch.yml), so by the
 * time this file loads, RP_ENABLE/RP_API_KEY/RP_ENDPOINT are expected to be set. If they are
 * not, we fall back to a no-op reporter rather than crashing the test run.
 */
const { rpEnabled, buildConfig } = require('../reportportal.config.js');

class NoopReporter {
  onRunComplete() {}
}

let ExportedReporter = NoopReporter;

if (rpEnabled()) {
  const mod = require('@reportportal/agent-js-jest');
  const RPReporter = mod && mod.default ? mod.default : mod;

  ExportedReporter = class HebloRPJestReporter extends RPReporter {
    constructor(globalConfig, _options) {
      super(globalConfig, buildConfig('frontend', { launch: 'heblo-frontend' }));
    }
  };
} else {
  console.log('ℹ️  ReportPortal disabled — Jest RP reporter is a no-op');
}

module.exports = ExportedReporter;
