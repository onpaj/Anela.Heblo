### task: update-process-doc

**Files:**
- Modify: `docs/processes/module-configuration.md`

- [ ] Step 1: In the version table row replace `` `"1.0.0"` (`ConfigurationConstants.DEFAULT_VERSION`) `` with `` `"1.0.0"` (`DefaultVersion` const in the handler) ``; environment row `(`DEFAULT_ENVIRONMENT`)` -> `(`DefaultEnvironment`)`.
- [ ] Step 2: Remove the `ConfigurationConstants.cs` bullet under "Code entry points"; adjust the handler bullet to say "value resolution and fallbacks".
- [ ] Step 3: Bump `verified_at` per `docs/processes/_TEMPLATE.md` convention (current short SHA of HEAD).
- [ ] Step 4: Run `python3 scripts/process-docs/check.py index` -> passes.
- [ ] Step 5: Commit `docs(processes): update module-configuration after removing ConfigurationConstants`.
