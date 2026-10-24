### task: add-orgchart-wiring-test

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartModuleWiringTests.cs`

- [ ] Step 1: Inspect `backend/src/Anela.Heblo.Application/ApplicationModule.cs` (line ~68-112) and an existing test in `backend/test/Anela.Heblo.Tests` that calls `AddApplicationServices` (grep) to learn the stub `IHostEnvironment` approach.
- [ ] Step 2: Write `OrgChartModuleWiringTests` with a `[Fact]` that calls `AddApplicationServices` on a fresh `ServiceCollection` with in-memory config (no `OrgChart:DataSourceUrl`) and a Development `IHostEnvironment` stub, then asserts a service descriptor of type `IValidateOptions<OrgChartOptions>` is registered. Prefer descriptor inspection; no provider build.
- [ ] Step 3: Run `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~OrgChart"`; expect all OrgChart tests (existing 3 + new) pass.
- [ ] Step 4: Mutation check: temporarily comment out `services.AddOrgChartServices(configuration);` in `ApplicationModule.cs`, confirm the new test fails, then restore the line.
- [ ] Step 5: Run `dotnet format` on the test project and `dotnet build`; confirm clean.
- [ ] Step 6: Commit: `test(orgchart): guard AddOrgChartServices wiring in ApplicationModule (#4471)`. PR body should note the issue premise was stale: wiring already exists at ApplicationModule.cs:112, so Program.cs was intentionally not changed.
