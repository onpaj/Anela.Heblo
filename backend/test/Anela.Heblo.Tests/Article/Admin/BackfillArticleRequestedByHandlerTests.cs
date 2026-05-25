using Anela.Heblo.Application.Features.Article.Admin;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Article;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Tests.Article.Admin;

public class BackfillArticleRequestedByHandlerTests
{
    private const string GroupId = "marketing-group-id";

    private readonly Mock<IArticleAdminRepository> _repository = new();
    private readonly Mock<IGraphService> _graph = new();

    private BackfillArticleRequestedByHandler CreateHandler() =>
        new(_repository.Object, _graph.Object, NullLogger<BackfillArticleRequestedByHandler>.Instance);

    private static DomainArticle Row(string requestedBy)
        => new() { Id = Guid.NewGuid(), Topic = "Topic", RequestedBy = requestedBy };

    private static UserDto Member(string id, string displayName)
        => new() { Id = id, DisplayName = displayName, Email = $"{displayName}@example.com" };

    [Fact]
    public async Task Handle_MissingGroupId_ReturnsValidationError()
    {
        var request = new BackfillArticleRequestedByCommand { GroupId = "", DryRun = true };

        var response = await CreateHandler().Handle(request, default);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task Handle_SkipsGuidShapedRowsAsAlreadyMigrated()
    {
        var alreadyMigrated = Row(Guid.NewGuid().ToString());
        _repository.Setup(r => r.ListWithRequestedByAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DomainArticle> { alreadyMigrated });
        _graph.Setup(g => g.GetGroupMembersAsync(GroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserDto>());

        var response = await CreateHandler().Handle(
            new BackfillArticleRequestedByCommand { GroupId = GroupId, DryRun = true }, default);

        response.AlreadyMigrated.Should().Be(1);
        response.Resolved.Should().Be(0);
        response.UnresolvedRows.Should().BeEmpty();
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SkipsEmailShapedRowsAsAlreadyMigrated()
    {
        var alreadyMigrated = Row("john@example.com");
        _repository.Setup(r => r.ListWithRequestedByAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DomainArticle> { alreadyMigrated });
        _graph.Setup(g => g.GetGroupMembersAsync(GroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserDto>());

        var response = await CreateHandler().Handle(
            new BackfillArticleRequestedByCommand { GroupId = GroupId, DryRun = true }, default);

        response.AlreadyMigrated.Should().Be(1);
        response.Resolved.Should().Be(0);
    }

    [Fact]
    public async Task Handle_UniqueDisplayNameMatch_ResolvesRow()
    {
        var row = Row("Jan Novák");
        _repository.Setup(r => r.ListWithRequestedByAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DomainArticle> { row });
        _graph.Setup(g => g.GetGroupMembersAsync(GroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserDto> { Member("jan-oid", "Jan Novák") });

        var response = await CreateHandler().Handle(
            new BackfillArticleRequestedByCommand { GroupId = GroupId, DryRun = false }, default);

        response.Resolved.Should().Be(1);
        response.WasDryRun.Should().BeFalse();
        row.RequestedBy.Should().Be("jan-oid");
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AmbiguousDisplayName_LeavesRowAndReports()
    {
        var row = Row("Jan Novák");
        _repository.Setup(r => r.ListWithRequestedByAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DomainArticle> { row });
        _graph.Setup(g => g.GetGroupMembersAsync(GroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserDto>
            {
                Member("jan-oid-a", "Jan Novák"),
                Member("jan-oid-b", "Jan Novák"),
            });

        var response = await CreateHandler().Handle(
            new BackfillArticleRequestedByCommand { GroupId = GroupId, DryRun = false }, default);

        response.Ambiguous.Should().Be(1);
        response.Resolved.Should().Be(0);
        response.UnresolvedRows.Should().ContainSingle(u =>
            u.ArticleId == row.Id && u.OriginalValue == "Jan Novák" && u.Reason.Contains("ambiguous"));
        row.RequestedBy.Should().Be("Jan Novák");
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownDisplayName_LeavesRowAndReports()
    {
        var row = Row("Ghost User");
        _repository.Setup(r => r.ListWithRequestedByAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DomainArticle> { row });
        _graph.Setup(g => g.GetGroupMembersAsync(GroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserDto> { Member("someone-oid", "Someone Else") });

        var response = await CreateHandler().Handle(
            new BackfillArticleRequestedByCommand { GroupId = GroupId, DryRun = false }, default);

        response.Unresolved.Should().Be(1);
        response.UnresolvedRows.Should().ContainSingle(u =>
            u.OriginalValue == "Ghost User" && u.Reason.Contains("no match"));
        row.RequestedBy.Should().Be("Ghost User");
    }

    [Fact]
    public async Task Handle_DryRun_DoesNotSaveResolvedRows()
    {
        var row = Row("Jan Novák");
        _repository.Setup(r => r.ListWithRequestedByAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DomainArticle> { row });
        _graph.Setup(g => g.GetGroupMembersAsync(GroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserDto> { Member("jan-oid", "Jan Novák") });

        var response = await CreateHandler().Handle(
            new BackfillArticleRequestedByCommand { GroupId = GroupId, DryRun = true }, default);

        response.Resolved.Should().Be(1);
        response.WasDryRun.Should().BeTrue();
        // Dry-run must not mutate entities — entity state unchanged
        row.RequestedBy.Should().Be("Jan Novák");
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_MixedSet_CountsCorrectly()
    {
        var rows = new List<DomainArticle>
        {
            Row(Guid.NewGuid().ToString()),    // already migrated (GUID)
            Row("ondra@example.com"),           // already migrated (email)
            Row("Jan Novák"),                  // resolved
            Row("Petra Dvořáková"),            // ambiguous
            Row("Ghost User"),                  // unresolved
        };
        _repository.Setup(r => r.ListWithRequestedByAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        _graph.Setup(g => g.GetGroupMembersAsync(GroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserDto>
            {
                Member("jan-oid", "Jan Novák"),
                Member("petra-oid-1", "Petra Dvořáková"),
                Member("petra-oid-2", "Petra Dvořáková"),
            });

        var response = await CreateHandler().Handle(
            new BackfillArticleRequestedByCommand { GroupId = GroupId, DryRun = false }, default);

        response.Total.Should().Be(5);
        response.AlreadyMigrated.Should().Be(2);
        response.Resolved.Should().Be(1);
        response.Ambiguous.Should().Be(1);
        response.Unresolved.Should().Be(1);
        response.UnresolvedRows.Should().HaveCount(2);
    }
}
