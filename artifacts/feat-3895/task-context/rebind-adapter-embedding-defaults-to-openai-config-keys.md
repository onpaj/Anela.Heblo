### task: rebind-adapter-embedding-defaults-to-openai-config-keys


Implements FR-5. Every call site now passes its own options, so the adapter's DI-time binding is a pure fallback for future consumers and must stop being named after the KnowledgeBase feature. Binds from `OpenAI:EmbeddingModel` / `OpenAI:EmbeddingDimensions`, matching the adjacent `OpenAI:ApiKey` line and `AnthropicAdapterServiceCollectionExtensions`'s `Anthropic:*` convention. Neither key exists in `appsettings*.json`, so the class defaults (`"text-embedding-3-large"` / `1536`) apply — the same values the old `KnowledgeBase:*` binding resolved to.

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiAdapterServiceCollectionExtensions.cs:16-17`
- Modify: `backend/test/Anela.Heblo.Adapters.OpenAI.Tests/Anela.Heblo.Adapters.OpenAI.Tests.csproj`
- Create: `backend/test/Anela.Heblo.Adapters.OpenAI.Tests/OpenAiAdapterServiceCollectionExtensionsTests.cs`

- [ ] **Step 1: Add the two packages the binding test needs**

The test project transitively has `Microsoft.Extensions.Configuration.Abstractions`/`.Binder` and `Microsoft.Extensions.DependencyInjection.Abstractions`, but not the concrete `ConfigurationBuilder` / `ServiceCollection` types. Versions match those already used elsewhere in `backend/`.

In `backend/test/Anela.Heblo.Adapters.OpenAI.Tests/Anela.Heblo.Adapters.OpenAI.Tests.csproj`, replace:

```xml
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.1" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="8.0.2" />
```

with:

```xml
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.1" />
    <PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.1" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="8.0.2" />
```

- [ ] **Step 2: Write the failing tests**

Create `backend/test/Anela.Heblo.Adapters.OpenAI.Tests/OpenAiAdapterServiceCollectionExtensionsTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.OpenAI.Tests;

public class OpenAiAdapterServiceCollectionExtensionsTests
{
    // Resolves only IOptions<OpenAiEmbeddingOptions>; the IEmbeddingGenerator registration is left
    // unresolved on purpose so no ILoggerFactory/ILogger registration is needed here.
    private static OpenAiEmbeddingOptions BindOptions(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

        var services = new ServiceCollection();
        services.AddOpenAiAdapter(configuration);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<OpenAiEmbeddingOptions>>().Value;
    }

    [Fact]
    public void AddOpenAiAdapter_NoEmbeddingKeys_UsesClassDefaults()
    {
        var options = BindOptions(("OpenAI:ApiKey", "test-key"));

        options.ApiKey.Should().Be("test-key");
        options.EmbeddingModel.Should().Be("text-embedding-3-large");
        options.EmbeddingDimensions.Should().Be(1536);
    }

    [Fact]
    public void AddOpenAiAdapter_OpenAiEmbeddingKeys_OverrideClassDefaults()
    {
        var options = BindOptions(
            ("OpenAI:ApiKey", "test-key"),
            ("OpenAI:EmbeddingModel", "text-embedding-3-small"),
            ("OpenAI:EmbeddingDimensions", "512"));

        options.EmbeddingModel.Should().Be("text-embedding-3-small");
        options.EmbeddingDimensions.Should().Be(512);
    }

    [Fact]
    public void AddOpenAiAdapter_KnowledgeBaseEmbeddingKeys_AreIgnored()
    {
        var options = BindOptions(
            ("OpenAI:ApiKey", "test-key"),
            ("KnowledgeBase:EmbeddingModel", "text-embedding-3-small"),
            ("KnowledgeBase:EmbeddingDimensions", "512"));

        options.EmbeddingModel.Should().Be(
            "text-embedding-3-large",
            "the adapter fallback must no longer be scoped to the KnowledgeBase feature's config");
        options.EmbeddingDimensions.Should().Be(1536);
    }
}
```

- [ ] **Step 3: Run the tests to verify the `KnowledgeBase` test fails**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.OpenAI.Tests/Anela.Heblo.Adapters.OpenAI.Tests.csproj \
  --filter "FullyQualifiedName~OpenAiAdapterServiceCollectionExtensionsTests"
```

Expected: `AddOpenAiAdapter_NoEmbeddingKeys_UsesClassDefaults` PASSES, `AddOpenAiAdapter_OpenAiEmbeddingKeys_OverrideClassDefaults` FAILS (`Expected options.EmbeddingModel to be "text-embedding-3-small", but found "text-embedding-3-large".`), `AddOpenAiAdapter_KnowledgeBaseEmbeddingKeys_AreIgnored` FAILS (`Expected options.EmbeddingModel to be "text-embedding-3-large" … but found "text-embedding-3-small".`).

- [ ] **Step 4: Change the binding source**

In `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiAdapterServiceCollectionExtensions.cs`, replace lines 16-17:

```csharp
            opts.EmbeddingModel = configuration["KnowledgeBase:EmbeddingModel"] ?? opts.EmbeddingModel;
            opts.EmbeddingDimensions = configuration.GetValue("KnowledgeBase:EmbeddingDimensions", opts.EmbeddingDimensions);
```

with:

```csharp
            // Adapter-wide fallback only — every current call site passes its own per-feature
            // EmbeddingGenerationOptions (see RagFeatureOptions.ToEmbeddingOptions). Keys are
            // deliberately neutral (OpenAI:*, like OpenAI:ApiKey above) rather than named after
            // any one feature. Absent from appsettings*.json, so the class defaults apply.
            opts.EmbeddingModel = configuration["OpenAI:EmbeddingModel"] ?? opts.EmbeddingModel;
            opts.EmbeddingDimensions = configuration.GetValue("OpenAI:EmbeddingDimensions", opts.EmbeddingDimensions);
```

- [ ] **Step 5: Run the adapter test project to verify everything passes**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.OpenAI.Tests/Anela.Heblo.Adapters.OpenAI.Tests.csproj
```

Expected: PASS — 16 tests.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiAdapterServiceCollectionExtensions.cs \
        backend/test/Anela.Heblo.Adapters.OpenAI.Tests/Anela.Heblo.Adapters.OpenAI.Tests.csproj \
        backend/test/Anela.Heblo.Adapters.OpenAI.Tests/OpenAiAdapterServiceCollectionExtensionsTests.cs
git commit -m "refactor(openai): bind adapter embedding fallback from OpenAI:* instead of KnowledgeBase:*"
```

---
