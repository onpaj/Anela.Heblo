using System.Data.Common;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Article;
using DotNet.Testcontainers.Configurations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Anela.Heblo.Tests.Article.Persistence;

[Trait("Category", "Integration")]
public class ArticleRepositoryFeedbackProjectionSqlTests : IAsyncLifetime
{
    static ArticleRepositoryFeedbackProjectionSqlTests()
    {
        // Required on macOS with Podman: the Ryuk ResourceReaper container
        // cannot bind to the Docker socket and throws a NullReferenceException.
        TestcontainersSettings.ResourceReaperEnabled = false;
    }

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .Build();

    private readonly CapturingCommandInterceptor _interceptor = new();
    private ApplicationDbContext _context = null!;
    private ArticleRepository _repository = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Minimal schema — only the columns the projection query touches plus the
        // required NOT NULL columns from ArticleConfiguration. HtmlContent is
        // included so the test would actually fail if the production query still
        // pulled it.
        await using (var conn = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE public."Articles" (
                    "Id"              uuid                     NOT NULL PRIMARY KEY,
                    "Topic"           varchar(2000)            NOT NULL,
                    "Scope"           varchar(50)              NOT NULL,
                    "Audience"        varchar(500),
                    "Angle"           varchar(500),
                    "Length"          varchar(50)              NOT NULL,
                    "LanguageNote"    varchar(500),
                    "UsedKnowledgeBase" boolean                NOT NULL DEFAULT false,
                    "UsedWebSearch"   boolean                  NOT NULL DEFAULT false,
                    "StyleGuideDriveId" varchar(500),
                    "StyleGuideItemPath" varchar(500),
                    "Title"           text,
                    "HtmlContent"     text,
                    "Status"          integer                  NOT NULL,
                    "ErrorMessage"    varchar(2000),
                    "RequestedBy"     varchar(200),
                    "PrecisionScore"  integer,
                    "StyleScore"      integer,
                    "FeedbackComment" text,
                    "CreatedAt"       timestamptz              NOT NULL,
                    "GeneratedAt"     timestamptz
                );
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .AddInterceptors(_interceptor)
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new ArticleRepository(_context);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task GetFeedbackPagedAsync_DoesNotSelectHtmlContent()
    {
        _interceptor.Reset();

        await _repository.GetFeedbackPagedAsync(
            hasFeedback: null,
            requestedBy: null,
            sortBy: "CreatedAt",
            descending: true,
            page: 1,
            pageSize: 20,
            ct: CancellationToken.None);

        // The COUNT(*) and the SELECT-with-Skip/Take both fire; inspect the row-returning
        // SELECT (the one that contains a column list, not just COUNT).
        var rowSelect = _interceptor.Commands
            .FirstOrDefault(c => c.Contains("FROM \"Articles\"", StringComparison.OrdinalIgnoreCase)
                && !c.Contains("COUNT(*)", StringComparison.OrdinalIgnoreCase));

        rowSelect.Should().NotBeNull("the repository must issue a row-returning SELECT against Articles");
        rowSelect!.Should().NotContain("\"HtmlContent\"",
            "HtmlContent is multi-KB per row and is never read by the feedback list handler");
    }

    [Fact]
    public async Task GetFeedbackPagedAsync_SelectsExactlyTheProjectedColumns()
    {
        _interceptor.Reset();

        await _repository.GetFeedbackPagedAsync(
            hasFeedback: null,
            requestedBy: null,
            sortBy: "CreatedAt",
            descending: true,
            page: 1,
            pageSize: 20,
            ct: CancellationToken.None);

        var rowSelect = _interceptor.Commands
            .First(c => c.Contains("FROM \"Articles\"", StringComparison.OrdinalIgnoreCase)
                && !c.Contains("COUNT(*)", StringComparison.OrdinalIgnoreCase));

        // Every projected column appears in the SELECT.
        foreach (var column in new[]
                 {
                     "\"Id\"", "\"Title\"", "\"Topic\"", "\"RequestedBy\"",
                     "\"CreatedAt\"", "\"PrecisionScore\"", "\"StyleScore\"", "\"FeedbackComment\""
                 })
        {
            rowSelect.Should().Contain(column,
                $"projected column {column} must appear in the SELECT");
        }
    }

    private sealed class CapturingCommandInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = new();

        public void Reset() => Commands.Clear();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
