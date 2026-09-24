using Anela.Heblo.Application.Features.ProcessDocs;
using Anela.Heblo.Application.Features.ProcessDocs.UseCases.GetProcessDoc;
using Anela.Heblo.Application.Features.ProcessDocs.UseCases.ListProcesses;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProcessDocs;

public class ProcessDocsHandlersTests
{
    private static string Doc(string name, string kind) => $"""
        ---
        process: {name}
        kind: {kind}
        summary: Summary of {name}.
        owns: [x/**]
        verified_at: "abcdef1"
        related: []
        ---

        # {name}

        ## Purpose
        Body of {name}.
        """;

    private static readonly EmbeddedProcessDocStore Store = new(
        [("calc-margins", Doc("calc-margins", "calculation")),
         ("sync-flexi-analytics", Doc("sync-flexi-analytics", "sync")),
         ("calc-stock-up", Doc("calc-stock-up", "calculation"))],
        NullLogger<EmbeddedProcessDocStore>.Instance);

    [Fact]
    public async Task ListProcesses_NoFilter_ReturnsAllSummariesWithoutMarkdown()
    {
        var result = await new ListProcessesHandler(Store).Handle(new ListProcessesRequest(), default);

        Assert.Equal(["calc-margins", "calc-stock-up", "sync-flexi-analytics"], result.Processes.Select(p => p.Name));
        Assert.Equal("Summary of calc-margins.", result.Processes[0].Summary);
    }

    [Fact]
    public async Task ListProcesses_KindFilter_IsCaseInsensitive()
    {
        var result = await new ListProcessesHandler(Store).Handle(new ListProcessesRequest { Kind = "SYNC" }, default);

        Assert.Equal(["sync-flexi-analytics"], result.Processes.Select(p => p.Name));
    }

    [Fact]
    public async Task GetProcessDoc_Found_ReturnsMarkdown()
    {
        var result = await new GetProcessDocHandler(Store).Handle(new GetProcessDocRequest { Name = "calc-margins" }, default);

        Assert.NotNull(result.Doc);
        Assert.Contains("Body of calc-margins.", result.Doc!.Markdown);
        Assert.Equal("abcdef1", result.Doc.VerifiedAt);
    }

    [Fact]
    public async Task GetProcessDoc_Misspelled_ReturnsNullDocAndCloseMatches()
    {
        var result = await new GetProcessDocHandler(Store).Handle(new GetProcessDocRequest { Name = "margins" }, default);

        Assert.Null(result.Doc);
        Assert.Contains("calc-margins", result.Suggestions);
    }

    [Fact]
    public async Task GetProcessDoc_BlankName_ReturnsNullDocAndNoSuggestions()
    {
        var result = await new GetProcessDocHandler(Store).Handle(new GetProcessDocRequest { Name = "   " }, default);

        Assert.Null(result.Doc);
        Assert.Empty(result.Suggestions);
    }
}
