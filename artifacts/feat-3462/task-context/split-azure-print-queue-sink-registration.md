### task: split-azure-print-queue-sink-registration

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs`
- Modify: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`
- Modify (add tests): `backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs`
- Test project: `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`

**Context for whoever executes this task:**

Today `AzureAdapterModule.AddAzurePrintQueueSink` (lines 14-27 of `AzureAdapterModule.cs`) does two things in one method: registers Azure Blob infrastructure (`BlobContainerClient` singleton factory) and binds a non-keyed `IPrintQueueSink -> AzureBlobPrintQueueSink` singleton. The `"Combined"` case in `ServiceCollectionExtensions.AddPrintQueueSink` (lines 422-434) calls this same method purely to get the infrastructure, then adds its own keyed (`"azure"`, `"cups"`) and non-keyed (`CombinedPrintQueueSink` factory) registrations on top — leaving a phantom, unwanted non-keyed `IPrintQueueSink` singleton (`AzureBlobPrintQueueSink`) in the container alongside the real `CombinedPrintQueueSink` non-keyed scoped registration. `GetService<IPrintQueueSink>()` happens to resolve the last-registered one (`CombinedPrintQueueSink`) correctly today, but `GetServices<IPrintQueueSink>()` / `IEnumerable<IPrintQueueSink>` would incorrectly yield both.

The fix: split the method so the `"Combined"` case can register only the infrastructure, with no non-keyed `IPrintQueueSink` side effect. `AzureBlobPrintQueueSink`'s constructor (`backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs:15-23`) takes `BlobContainerClient`, `TimeProvider`, `ILogger<AzureBlobPrintQueueSink>` — all singleton-safe — so it is registered as a concrete singleton inside the infrastructure method, and every consumer (the `"AzureBlob"` non-keyed binding, and the `"Combined"` mode's `"azure"` keyed binding) resolves to that same shared instance via a factory delegate, rather than constructing separate instances.

- [ ] 1. Write a failing regression test in `backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs` that proves the phantom registration exists today. Add this test method inside the `CombinedPrintQueueSinkRegistrationTests` class (after `Combined_KeyedCupsSlot_ResolvesCupsPrintQueueSink`, before `FileSystem_ResolvesFileSystemPrintQueueSink`):

  ```csharp
      [Fact]
      public void Combined_NonKeyedIPrintQueueSink_HasExactlyOneRegistration_AndItIsCombined()
      {
          // Arrange
          using var provider = BuildProvider("Combined");
          using var scope = provider.CreateScope();

          // Act
          var sinks = scope.ServiceProvider.GetServices<IPrintQueueSink>().ToList();

          // Assert — only the non-keyed CombinedPrintQueueSink factory should be visible here;
          // keyed registrations ("azure", "cups") are invisible to GetServices<IPrintQueueSink>()
          // by design and must not appear in this collection.
          var sink = Assert.Single(sinks);
          Assert.IsType<CombinedPrintQueueSink>(sink);
      }
  ```

  This requires `using System.Linq;` in the test file for `.ToList()` — add it if not already present.

- [ ] 2. Run the new test and confirm it FAILS against the current (unfixed) code, proving it would have caught the bug:

  ```bash
  cd /home/user/worktrees/feature-3462-Arch-Review-Expeditionlist-Addazureprintqueuesink/backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CombinedPrintQueueSinkRegistrationTests.Combined_NonKeyedIPrintQueueSink_HasExactlyOneRegistration_AndItIsCombined"
  ```

  Expected: test fails (the assertion `Assert.Single(sinks)` throws because two items are present — the phantom `AzureBlobPrintQueueSink` singleton and the `CombinedPrintQueueSink` factory).

- [ ] 3. Commit the failing test on its own:

  ```bash
  cd /home/user/worktrees/feature-3462-Arch-Review-Expeditionlist-Addazureprintqueuesink
  git add backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs
  git commit -m "test: add failing regression test for phantom IPrintQueueSink singleton in Combined mode"
  ```

- [ ] 4. Replace the full contents of `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs` with:

  ```csharp
  // backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs
  using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;
  using Anela.Heblo.Application.Features.ExpeditionList;
  using Anela.Heblo.Application.Shared.Printing;
  using Azure.Storage.Blobs;
  using Microsoft.Extensions.Configuration;
  using Microsoft.Extensions.DependencyInjection;
  using Microsoft.Extensions.Options;

  namespace Anela.Heblo.Adapters.Azure;

  public static class AzureAdapterModule
  {
      /// <summary>
      /// Registers Azure Blob print-queue infrastructure (BlobContainerClient, AzureBlobPrintQueueSink
      /// as a concrete singleton) without binding a non-keyed IPrintQueueSink. Use this when the caller
      /// will register its own (e.g. keyed) IPrintQueueSink binding, such as the "Combined" print-sink mode.
      /// </summary>
      public static IServiceCollection AddAzurePrintQueueSinkInfrastructure(
          this IServiceCollection services,
          IConfiguration configuration)
      {
          services.AddSingleton(provider =>
          {
              var options = provider.GetRequiredService<IOptions<PrintPickingListOptions>>().Value;
              return new BlobContainerClient(options.BlobConnectionString, options.BlobContainerName);
          });

          services.AddSingleton<AzureBlobPrintQueueSink>();

          return services;
      }

      /// <summary>
      /// Registers Azure Blob print-queue infrastructure and binds the non-keyed IPrintQueueSink
      /// singleton to AzureBlobPrintQueueSink. Use this for the "AzureBlob" print-sink mode, where
      /// AzureBlobPrintQueueSink is the sole, directly-resolvable IPrintQueueSink implementation.
      /// </summary>
      public static IServiceCollection AddAzurePrintQueueSink(
          this IServiceCollection services,
          IConfiguration configuration)
      {
          services.AddAzurePrintQueueSinkInfrastructure(configuration);
          services.AddSingleton<IPrintQueueSink>(provider => provider.GetRequiredService<AzureBlobPrintQueueSink>());
          return services;
      }
  }
  ```

- [ ] 5. In `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`, replace the `"Combined"` case (currently lines 422-434) — including the workaround comment — with:

  ```csharp
              case "Combined":
                  services.AddAzurePrintQueueSinkInfrastructure(configuration);
                  services.AddKeyedSingleton<IPrintQueueSink>("azure",
                      (provider, _) => provider.GetRequiredService<AzureBlobPrintQueueSink>());
                  services.AddKeyedScoped<IPrintQueueSink, CupsPrintQueueSink>("cups");
                  services.AddScoped<IPrintQueueSink>(provider =>
                  {
                      var azure = provider.GetRequiredKeyedService<IPrintQueueSink>("azure");
                      var cups = provider.GetRequiredKeyedService<IPrintQueueSink>("cups");
                      return new Anela.Heblo.API.Features.ExpeditionList.CombinedPrintQueueSink(azure, cups);
                  });
                  break;
  ```

  This removes the two-line workaround comment (`// AddAzurePrintQueueSink registers a non-keyed IPrintQueueSink as a side effect; ...`), switches the infrastructure call from `AddAzurePrintQueueSink` to `AddAzurePrintQueueSinkInfrastructure`, and replaces `services.AddKeyedScoped<IPrintQueueSink, AzureBlobPrintQueueSink>("azure");` with the singleton factory registration that shares the one instance registered by the infrastructure method. Do not touch the `"AzureBlob"` case (line 415-417), the `"Cups"` case (418-421), or the `default` case (435-437) — they are unchanged.

- [ ] 6. Add the `AzureBlobPrintQueueSink` type reference check: confirm `ServiceCollectionExtensions.cs` already has a `using` for `Anela.Heblo.Adapters.Azure.Features.ExpeditionList;` (needed for the unqualified `AzureBlobPrintQueueSink` reference in the new `"azure"` factory). Check with:

  ```bash
  cd /home/user/worktrees/feature-3462-Arch-Review-Expeditionlist-Addazureprintqueuesink
  grep -n "^using" backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs | grep -i "Adapters.Azure"
  ```

  If the grep returns no output, add this line to the `using` block at the top of `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` (alongside the other `using` statements, keeping alphabetical order consistent with the existing block):

  ```csharp
  using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;
  ```

- [ ] 7. Build the backend to confirm the split compiles cleanly:

  ```bash
  cd /home/user/worktrees/feature-3462-Arch-Review-Expeditionlist-Addazureprintqueuesink/backend
  dotnet build
  ```

  Expected: build succeeds with no errors.

- [ ] 8. Run the full `CombinedPrintQueueSinkRegistrationTests` suite (all five tests, including the new regression test from step 1) and confirm all pass:

  ```bash
  cd /home/user/worktrees/feature-3462-Arch-Review-Expeditionlist-Addazureprintqueuesink/backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CombinedPrintQueueSinkRegistrationTests"
  ```

  Expected: 5 tests pass — `Combined_ResolvesCombinedPrintQueueSink`, `Combined_KeyedAzureSlot_ResolvesAzureBlobPrintQueueSink`, `Combined_KeyedCupsSlot_ResolvesCupsPrintQueueSink`, `FileSystem_ResolvesFileSystemPrintQueueSink`, and the new `Combined_NonKeyedIPrintQueueSink_HasExactlyOneRegistration_AndItIsCombined`.

- [ ] 9. Run the full backend test suite to check for unrelated regressions:

  ```bash
  cd /home/user/worktrees/feature-3462-Arch-Review-Expeditionlist-Addazureprintqueuesink/backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
  ```

  Expected: all tests pass.

- [ ] 10. Run `dotnet format` to ensure formatting compliance, then verify it produced no unexpected diffs beyond the intended changes:

  ```bash
  cd /home/user/worktrees/feature-3462-Arch-Review-Expeditionlist-Addazureprintqueuesink/backend
  dotnet format --verify-no-changes || dotnet format
  git diff --stat
  ```

  If `dotnet format` makes changes, review `git diff` to confirm they only touch whitespace/style in the files already modified by this task, then proceed.

- [ ] 11. Final NFR-3 gate — re-verify `AddAzurePrintQueueSink` has no other call sites beyond its own definition and the `"AzureBlob"` case:

  ```bash
  cd /home/user/worktrees/feature-3462-Arch-Review-Expeditionlist-Addazureprintqueuesink
  grep -rn "AddAzurePrintQueueSink\b" backend/
  ```

  Expected output: three lines total —
  1. The definition of `AddAzurePrintQueueSink` in `AzureAdapterModule.cs`.
  2. The definition of `AddAzurePrintQueueSinkInfrastructure` in `AzureAdapterModule.cs` (this line matches too, because `AddAzurePrintQueueSinkInfrastructure` starts with the literal substring `AddAzurePrintQueueSink` even though the `\b` word-boundary anchor is only at the start of the pattern, not the end).
  3. The `"AzureBlob"` case call site in `ServiceCollectionExtensions.cs` (`services.AddAzurePrintQueueSink(configuration);`).

  Confirm the `"Combined"` case line shows `services.AddAzurePrintQueueSinkInfrastructure(configuration);` and that no other file in the repo references either method. If any unexpected call site appears, stop and investigate before proceeding.

- [ ] 12. Stage and commit the implementation changes:

  ```bash
  cd /home/user/worktrees/feature-3462-Arch-Review-Expeditionlist-Addazureprintqueuesink
  git add backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs
  git commit -m "fix: split AddAzurePrintQueueSink into infrastructure and full registration to remove phantom IPrintQueueSink singleton in Combined mode"
  ```

- [ ] 13. Confirm the working tree is clean and both commits are present:

  ```bash
  cd /home/user/worktrees/feature-3462-Arch-Review-Expeditionlist-Addazureprintqueuesink
  git status
  git log --oneline -3
  ```

  Expected: working tree clean, most recent two commits are the test commit (step 3) and the implementation commit (step 12).
