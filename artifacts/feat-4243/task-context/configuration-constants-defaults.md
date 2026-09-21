### task: configuration-constants-defaults

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Configuration/ApplicationConfigurationTests.cs`
- Edit: `backend/src/Anela.Heblo.Domain/Features/Configuration/ApplicationConfiguration.cs` (lines 24–27)

Steps:

- [ ] **Step 1 — Write the regression test.** Create `backend/test/Anela.Heblo.Tests/Features/Configuration/ApplicationConfigurationTests.cs` with this exact content:

  ```csharp
  using Anela.Heblo.Domain.Features.Configuration;
  using FluentAssertions;

  namespace Anela.Heblo.Tests.Features.Configuration;

  public class ApplicationConfigurationTests
  {
      [Fact]
      public void CreateWithDefaults_WithNullVersionAndEnvironment_FallsBackToConfigurationConstantsDefaults()
      {
          // Act
          var config = ApplicationConfiguration.CreateWithDefaults(null, null, false);

          // Assert
          config.Version.Should().Be(ConfigurationConstants.DEFAULT_VERSION);
          config.Environment.Should().Be(ConfigurationConstants.DEFAULT_ENVIRONMENT);
      }

      [Fact]
      public void CreateWithDefaults_WithProvidedVersionAndEnvironment_PassesThroughUnchanged()
      {
          // Act
          var config = ApplicationConfiguration.CreateWithDefaults("9.9.9", "Staging", true);

          // Assert
          config.Version.Should().Be("9.9.9");
          config.Environment.Should().Be("Staging");
          config.UseMockAuth.Should().BeTrue();
      }
  }
  ```

- [ ] **Step 2 — Run the new test (expect: PASS).** This is not a red→green TDD cycle — the production code already returns these exact values via the hardcoded literals, which currently equal the constants. This run confirms the test compiles, targets the right method, and passes before you touch production code, so any later failure is attributable only to your edit.

  ```bash
  cd /home/user/worktrees/feature-4243-Arch-Review-Configuration-Createwithdefaults-Hardc/backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ApplicationConfigurationTests"
  ```

  Expected output: both new tests pass, e.g. `Passed!  - Failed: 0, Passed: 2, Skipped: 0, Total: 2`.

- [ ] **Step 3 — Implement the fix.** In `backend/src/Anela.Heblo.Domain/Features/Configuration/ApplicationConfiguration.cs`, replace lines 22–29 (the entire `CreateWithDefaults` method) with:

  ```csharp
      /// <summary>
      /// Creates configuration with fallback values
      /// </summary>
      public static ApplicationConfiguration CreateWithDefaults(string? version, string? environment, bool useMockAuth)
      {
          return new ApplicationConfiguration(
              version ?? ConfigurationConstants.DEFAULT_VERSION,
              environment ?? ConfigurationConstants.DEFAULT_ENVIRONMENT,
              useMockAuth
          );
      }
  ```

  The only substantive change is on the two former lines 25–26: `"1.0.0"` becomes `ConfigurationConstants.DEFAULT_VERSION`, and `"Production"` becomes `ConfigurationConstants.DEFAULT_ENVIRONMENT`. Nothing else in the file changes.

- [ ] **Step 4 — Run the new test again (expect: PASS, unchanged).**

  ```bash
  cd /home/user/worktrees/feature-4243-Arch-Review-Configuration-Createwithdefaults-Hardc/backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ApplicationConfigurationTests"
  ```

  Expected output: `Passed!  - Failed: 0, Passed: 2, Skipped: 0, Total: 2`.

- [ ] **Step 5 — Run the existing caller's test suite (regression check).** Confirms `GetConfigurationHandler`, the one call site, is unaffected.

  ```bash
  cd /home/user/worktrees/feature-4243-Arch-Review-Configuration-Createwithdefaults-Hardc/backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConfigurationHandlerTests"
  ```

  Expected output: `Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5`.

- [ ] **Step 6 — Build and format check.**

  ```bash
  cd /home/user/worktrees/feature-4243-Arch-Review-Configuration-Createwithdefaults-Hardc/backend
  dotnet build
  dotnet format --verify-no-changes
  ```

  Expected: `dotnet build` succeeds with no new warnings; `dotnet format --verify-no-changes` reports no formatting issues (or run `dotnet format` without `--verify-no-changes` first if it reports diffs, then re-verify).

- [ ] **Step 7 — Commit.**

  ```bash
  cd /home/user/worktrees/feature-4243-Arch-Review-Configuration-Createwithdefaults-Hardc
  git add backend/src/Anela.Heblo.Domain/Features/Configuration/ApplicationConfiguration.cs backend/test/Anela.Heblo.Tests/Features/Configuration/ApplicationConfigurationTests.cs
  git commit -m "$(cat <<'EOF'
  Reference ConfigurationConstants defaults in ApplicationConfiguration.CreateWithDefaults

  Replace hardcoded "1.0.0"/"Production" literals with ConfigurationConstants.DEFAULT_VERSION/DEFAULT_ENVIRONMENT to remove the duplication flagged by architecture review. No behavioral change.

  Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01WeWEhwYoDHCs6K5hCnbWbr
  EOF
  )"
  ```

  Expected: commit succeeds; `git status` shows a clean working tree for these two files.

---

## Self-Review

- **Spec FR-1 mapped:** ✅ covered by Step 3 (the literal-to-constant swap) and Steps 1–2, 4 (test asserting `Version`/`Environment` equal the constants).
- **Acceptance criterion "no hardcoded literals remain":** ✅ Step 3 removes both literals.
- **Acceptance criterion "CreateWithDefaults(null, null, false) unchanged observable behavior":** ✅ asserted in Step 1's first test.
- **Acceptance criterion "non-null version/environment pass through unchanged":** ✅ asserted in Step 1's second test.
- **Acceptance criterion "no other members/files/call sites modified":** ✅ only `ApplicationConfiguration.cs` (production) and the new test file are touched; `GetConfigurationHandlerTests.cs` and `GetConfigurationHandler.cs` are untouched and re-verified in Step 5.
- **Acceptance criterion "dotnet build / dotnet format succeed with no new warnings":** ✅ Step 6.
- **Acceptance criterion "existing tests covering CreateWithDefaults continue to pass unmodified":** ✅ no prior test file existed for this method (verified: no `ApplicationConfigurationTests.cs` present before this plan); `GetConfigurationHandlerTests.cs` is the closest existing coverage and is re-run unmodified in Step 5.
- **Placeholder scan:** none — all code blocks are complete and copy-pasteable; no "TBD", no "add appropriate error handling", no undefined symbols.
- **Type/signature consistency:** `CreateWithDefaults(string? version, string? environment, bool useMockAuth)` signature is identical before and after; test calls match it exactly (`ApplicationConfiguration.CreateWithDefaults(null, null, false)` and `ApplicationConfiguration.CreateWithDefaults("9.9.9", "Staging", true)`).
- **Out-of-scope items respected:** no changes to `ConfigurationConstants` values, no changes to the `ApplicationConfiguration` constructor, no changes to callers.
