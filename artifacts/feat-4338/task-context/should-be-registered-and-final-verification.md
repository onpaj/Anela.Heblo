### task: should-be-registered-and-final-verification

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Infrastructure/Authentication/E2ETestAuthenticationMiddlewareTests.cs` (append inside the class, after the exception-path test from the previous task, before the final closing `}`)

This task adds the `ShouldBeRegistered` guard-matrix tests (FR-8), then runs the full validation suite (build, format, full test run) to confirm the coverage target from NFR-4 is met and nothing else broke.

- [ ] **Step 1: Add the `ShouldBeRegistered` theory**

Insert this immediately before the class's closing `}` (after `InvokeAsync_TokenValidatorThrows_Returns500AndDoesNotCallNext`):

```csharp

    // ── ShouldBeRegistered guard matrix (FR-8) ────────────────────────────

    [Theory]
    [InlineData("Development", null, true)]
    [InlineData("Staging", null, true)]
    [InlineData("Production", null, false)]
    [InlineData("Staging", true, false)]
    [InlineData("Development", true, false)]
    [InlineData("Staging", false, true)]
    [InlineData("IntegrationTest", null, false)]
    [InlineData("IntegrationTest", true, false)]
    public void ShouldBeRegistered_ReturnsExpected(string environmentName, bool? useMockAuth, bool expected)
    {
        var configData = new Dictionary<string, string?>();
        if (useMockAuth.HasValue)
            configData["UseMockAuth"] = useMockAuth.Value.ToString();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();

        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(environmentName);

        var result = E2ETestAuthenticationMiddleware.ShouldBeRegistered(configuration, environment.Object);

        result.Should().Be(expected);
    }
```

- [ ] **Step 2: Run the full new test class to verify everything passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~E2ETestAuthenticationMiddlewareTests"`
Expected: 18 tests total (10 from before + 8 `ShouldBeRegistered_ReturnsExpected` rows), all PASS.

- [ ] **Step 3: Build the whole backend solution**

Run: `cd backend && dotnet build`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (or the baseline warning count already present on the branch — no NEW warnings introduced by this file).

- [ ] **Step 4: Run `dotnet format` and confirm no diffs**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: exits 0 with no changes reported. If it reports formatting issues in the new file, run `dotnet format` (without `--verify-no-changes`) to auto-fix, review the diff is confined to `E2ETestAuthenticationMiddlewareTests.cs`, then re-run `--verify-no-changes` to confirm.

- [ ] **Step 5: Run the full backend test suite to confirm nothing else regressed**

Run: `cd backend && dotnet test`
Expected: all existing tests plus the 18 new tests pass; no pre-existing test starts failing.

- [ ] **Step 6: Confirm the coverage target was met (NFR-4)**

If the repo's coverage tooling is available locally (check `docs/architecture/testing-strategy.md` for the exact command used in CI), run it scoped to the changed file and confirm `E2ETestAuthenticationMiddleware.cs` line coverage is now >= 60%. If no local coverage command is readily available, this is also verified automatically by the coverage-gap routine's next scheduled run against CI — note in the PR description that local verification of the exact percentage was not performed, only that every branch enumerated in the spec's FR-1 through FR-8 has an asserting test.

- [ ] **Step 7: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Infrastructure/Authentication/E2ETestAuthenticationMiddlewareTests.cs
git commit -m "test(feat-4338): add ShouldBeRegistered guard matrix tests, complete coverage for E2ETestAuthenticationMiddleware"
```

---

## Self-Review

**Spec coverage:** FR-1 through FR-8 and NFR-1 through NFR-4 are each mapped to a task in the table above; no spec requirement is without a task.

**Placeholder scan:** No "TBD"/"TODO"/"add appropriate handling" placeholders — every step contains complete, compilable C# test code. Step 6 of the last task is the one place execution depends on discovering the repo's actual coverage-tool invocation (not specified in the spec or found during architecture review), so it is written as a best-effort check with an explicit fallback note rather than a fabricated command.

**Type consistency:** `CreateEnvironment`, `CreateMiddleware`, `CreateFailingCookieAuthService`, `BuildServices`, `CreateContext`, and `ReadResponseBodyAsync` are defined once in task `scaffold-file-and-environment-guard-tests` and used with identical signatures in every subsequent task. `IServicePrincipalTokenValidator.ValidateAsync(string) : Task<bool>` and `IE2ESessionService.CreateSyntheticUserClaims(string) : Claim[]` match the real interfaces read from `backend/src/Anela.Heblo.API/Infrastructure/Authentication/ServicePrincipalTokenValidator.cs` and `E2ESessionService.cs`.
