### task: move-di-registration-to-sharedragmodule

[Change AddSharedRagModule to accept IConfiguration and own all IDocumentTextExtractor/IOneDriveService registrations; strip them from KnowledgeBaseModule; update the one ApplicationModule.cs call site]

- [ ] Step 1: Rewrite `SharedRagModule.cs`.

  File: `backend/src/Anela.Heblo.Application/Shared/Rag/SharedRagModule.cs`

  Current content:
  ```csharp
  using Microsoft.Extensions.DependencyInjection;

  namespace Anela.Heblo.Application.Shared.Rag;

  public static class SharedRagModule
  {
      public static IServiceCollection AddSharedRagModule(this IServiceCollection services)
      {
          services.AddScoped<IWordWindowChunker, WordWindowChunker>();
          services.AddScoped<IRagQueryExpander, RagQueryExpander>();
          return services;
      }
  }
  ```

  Replace with:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase;
  using Anela.Heblo.Application.Shared.Rag.DocumentExtractors;
  using Anela.Heblo.Application.Shared.Rag.OneDrive;
  using Anela.Heblo.Domain.Shared;
  using Microsoft.Extensions.Configuration;
  using Microsoft.Extensions.DependencyInjection;

  namespace Anela.Heblo.Application.Shared.Rag;

  public static class SharedRagModule
  {
      public static IServiceCollection AddSharedRagModule(
          this IServiceCollection services,
          IConfiguration configuration)
      {
          services.AddScoped<IWordWindowChunker, WordWindowChunker>();
          services.AddScoped<IRagQueryExpander, RagQueryExpander>();

          services.AddScoped<IDocumentTextExtractor, PdfTextExtractor>();
          services.AddScoped<IDocumentTextExtractor, WordDocumentExtractor>();
          services.AddScoped<IDocumentTextExtractor, PlainTextExtractor>();

          // OneDrive service — use real Graph service only when SharePoint drives are configured
          // AND real authentication is active. Mock auth has no Azure AD token so Graph calls
          // would fail; MockOneDriveService is used in those environments instead.
          // Moved verbatim from KnowledgeBaseModule — the Graph-vs-Mock check intentionally still
          // only inspects the "KnowledgeBase" configuration section (not "Leaflet"), matching
          // today's behavior exactly. This is a pre-existing latent gap (Leaflet's own
          // OneDriveFolderMappings are never consulted here), tracked separately — not fixed as
          // part of this refactor (NFR-1: zero behavioral change).
          var kbOptions = new KnowledgeBaseOptions();
          configuration.GetSection("KnowledgeBase").Bind(kbOptions);
          var sharePointConfigured = kbOptions.OneDriveFolderMappings.Any(m => !string.IsNullOrWhiteSpace(m.DriveId));
          var useMockAuth = configuration.GetValue<bool>("UseMockAuth", false);
          var bypassJwtValidation = configuration.GetValue<bool>(InfrastructureConfigurationKeys.BYPASS_JWT_VALIDATION, false);

          if (sharePointConfigured && !useMockAuth && !bypassJwtValidation)
          {
              services.AddHttpClient("MicrosoftGraph");
              services.AddMemoryCache();
              services.AddScoped<IOneDriveService, GraphOneDriveService>();
          }
          else
          {
              services.AddScoped<IOneDriveService, MockOneDriveService>();
          }

          return services;
      }
  }
  ```

  (`KnowledgeBaseOptions` lives in namespace `Anela.Heblo.Application.Features.KnowledgeBase` — confirmed by reading `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseOptions.cs` — hence the new `using Anela.Heblo.Application.Features.KnowledgeBase;`. `InfrastructureConfigurationKeys` lives in `Anela.Heblo.Domain.Shared` — confirmed by reading `backend/src/Anela.Heblo.Domain/Shared/InfrastructureConfigurationKeys.cs` — hence `using Anela.Heblo.Domain.Shared;`. No `ModuleBoundariesTests` rule inspects the `Anela.Heblo.Application.Shared.Rag` namespace as a consumer, so this reference does not trip any existing boundary test.)

- [ ] Step 2: Update the single call site in `ApplicationModule.cs`.

  File: `backend/src/Anela.Heblo.Application/ApplicationModule.cs`

  Change:
  ```csharp
  services.AddSharedRagModule();
  ```
  to:
  ```csharp
  services.AddSharedRagModule(configuration);
  ```
  (`configuration` is already an in-scope parameter of `AddApplicationServices` — no other changes needed on this line. This is the only call site in the repository; confirm with `grep -rn "AddSharedRagModule(" backend/src backend/test` after this step — it must show exactly one match, in this file, now with the `configuration` argument.)

- [ ] Step 3: Strip the moved registrations out of `KnowledgeBaseModule.cs`, keeping the `KnowledgeBaseOptions` binding.

  File: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`

  Remove these lines entirely:
  ```csharp
  services.AddScoped<IDocumentTextExtractor, PdfTextExtractor>();
  services.AddScoped<IDocumentTextExtractor, WordDocumentExtractor>();
  services.AddScoped<IDocumentTextExtractor, PlainTextExtractor>();
  ```
  and the whole OneDrive selection block:
  ```csharp
  // OneDrive service — use real Graph service only when SharePoint drives are configured
  // AND real authentication is active. Mock auth has no Azure AD token so Graph calls
  // would fail; MockOneDriveService is used in those environments instead.
  var kbOptions = new KnowledgeBaseOptions();
  configuration.GetSection("KnowledgeBase").Bind(kbOptions);
  var sharePointConfigured = kbOptions.OneDriveFolderMappings.Any(m => !string.IsNullOrWhiteSpace(m.DriveId));
  var useMockAuth = configuration.GetValue<bool>("UseMockAuth", false);
  var bypassJwtValidation = configuration.GetValue<bool>(InfrastructureConfigurationKeys.BYPASS_JWT_VALIDATION, false);

  if (sharePointConfigured && !useMockAuth && !bypassJwtValidation)
  {
      services.AddHttpClient("MicrosoftGraph");
      services.AddMemoryCache();
      services.AddScoped<IOneDriveService, GraphOneDriveService>();
  }
  else
  {
      services.AddScoped<IOneDriveService, MockOneDriveService>();
  }
  ```

  Keep everything else unchanged, in particular:
  ```csharp
  services.AddOptions<KnowledgeBaseOptions>()
      .Bind(configuration.GetSection(KnowledgeBaseOptions.SectionName))
      .ValidateDataAnnotations()
      .ValidateOnStart();
  ```
  (this is KnowledgeBase's own options binding, unrelated to the OneDrive Graph/Mock selection that just moved) and all the other registrations (`ChatTranscriptPreprocessor`, `IChunkSummarizer`, `IConversationTopicSummarizer`, `IIndexingStrategy` x2, `IDocumentIndexingService`, the `ILeafletKnowledgeSource`/`IArticleStyleGuideSource`/`IArticleKnowledgeSource` adapter bindings, `IKnowledgeBaseRepository`, `IProductEnrichmentCache`, the `QuestionLoggingBehavior` pipeline behavior registration).

  Now remove the `using Anela.Heblo.Application.Shared.Rag.DocumentExtractors;` and `using Anela.Heblo.Application.Shared.Rag.OneDrive;` lines added in the previous two tasks **only if** nothing else in this file still needs them — check first:
  ```bash
  grep -n "PdfTextExtractor\|WordDocumentExtractor\|PlainTextExtractor\|GraphOneDriveService\|MockOneDriveService" backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs
  ```
  If this returns no matches (expected, since those registrations were just deleted), remove the two now-unused `using` lines. Keep `using Anela.Heblo.Application.Shared.Rag;` if anything in the file still references `IDocumentTextExtractor` or `IOneDriveService` by type name (it will not, after the registrations are deleted, unless another line in the file separately depends on them — verify with the same grep pattern extended to `IDocumentTextExtractor\|IOneDriveService`); if that also returns no matches, remove that using too.

- [ ] Step 4: `dotnet build` the whole solution and confirm success.

  ```bash
  cd backend && dotnet build Anela.Heblo.sln 2>&1 | tail -40
  ```
  Expect: `Build succeeded.` with 0 errors and no new warnings (compare warning count against Step 12 of the previous task if unsure).

- [ ] Step 5: Run the composition-root smoke tests to confirm DI still resolves both services end-to-end (this is the acceptance-criteria check for FR-4 — "KnowledgeBase and Leaflet both continue to resolve `IDocumentTextExtractor` and `IOneDriveService` successfully via DI").

  ```bash
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~ApplicationStartupTests" \
    2>&1 | tail -60
  ```
  Expect `Application_Should_Start_Successfully` and every `Controller_Should_Be_Resolvable` case to pass — a missing/duplicate DI registration for `IOneDriveService` or `IDocumentTextExtractor` would surface here as a startup or controller-resolution failure.

- [ ] Step 6: Also re-run the full set of tests touched by the previous two tasks, to catch any regression from the registration move.

  ```bash
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~Shared.Rag|FullyQualifiedName~KnowledgeBase|FullyQualifiedName~Leaflet" \
    2>&1 | tail -80
  ```
  Expect 0 failures.

- [ ] Step 7: Commit.

  ```bash
  git add -A
  git commit -m "Move IDocumentTextExtractor/IOneDriveService DI registration from KnowledgeBaseModule to SharedRagModule"
  ```

---

