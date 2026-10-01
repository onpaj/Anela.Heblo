using Anela.Heblo.Application.Features.ProcessDocs;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProcessDocs;

public class EmbeddedProcessDocStoreTests
{
    private static string Doc(string name, string kind = "calculation") => $"""
        ---
        process: {name}
        kind: {kind}
        summary: Summary of {name}.
        owns: [x/**]
        verified_at: "abcdef1"
        related: []
        ---

        # {name}
        """;

    [Fact]
    public void RealEmbeddedDocs_AllParse()
    {
        var store = new EmbeddedProcessDocStore(NullLogger<EmbeddedProcessDocStore>.Instance);

        Assert.Empty(store.LoadErrors);
        Assert.True(store.All.Count >= 3, $"expected >= 3 embedded process docs, got {store.All.Count}");
    }

    [Fact]
    public void MalformedDoc_IsSkippedAndOthersStillServed()
    {
        var store = new EmbeddedProcessDocStore(
            [("calc-good", Doc("calc-good")), ("calc-bad", "no frontmatter")],
            NullLogger<EmbeddedProcessDocStore>.Instance);

        Assert.Single(store.All);
        Assert.Equal("calc-good", store.All[0].Name);
        Assert.Single(store.LoadErrors);
        Assert.Contains("calc-bad", store.LoadErrors[0]);
    }

    [Fact]
    public void Find_IsCaseInsensitive_AndReturnsNullWhenMissing()
    {
        var store = new EmbeddedProcessDocStore([("calc-good", Doc("calc-good"))],
            NullLogger<EmbeddedProcessDocStore>.Instance);

        Assert.NotNull(store.Find("CALC-GOOD"));
        Assert.Null(store.Find("calc-missing"));
    }

    [Fact]
    public void All_IsSortedByName()
    {
        var store = new EmbeddedProcessDocStore(
            [("sync-b", Doc("sync-b", "sync")), ("calc-a", Doc("calc-a"))],
            NullLogger<EmbeddedProcessDocStore>.Instance);

        Assert.Equal(["calc-a", "sync-b"], store.All.Select(d => d.Name));
    }
}
