using Anela.Heblo.Application.Features.MeetingTasks;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public sealed class MeetingUserDirectoryTests : IDisposable
{
    private readonly string _tempFile;

    public MeetingUserDirectoryTests()
    {
        _tempFile = Path.GetTempFileName();
        File.WriteAllText(_tempFile, """
            [
              { "email": "andrea@anela.cz", "displayName": "Andrea Nováková", "aliases": ["Andy", "Andrea"] },
              { "email": "petr@anela.cz", "displayName": "Petr Svoboda", "aliases": [] },
              { "email": "bara@anela.cz", "displayName": "Bára Kocmánková", "aliases": ["Bára"] }
            ]
            """);
    }

    private MeetingUserDirectory CreateDirectory(string path)
    {
        var options = Options.Create(new MeetingTasksOptions { UserDirectoryPath = path });
        return new MeetingUserDirectory(options, Mock.Of<ILogger<MeetingUserDirectory>>());
    }

    [Fact]
    public void GetAll_ReturnsAllUsersFromFile()
    {
        var directory = CreateDirectory(_tempFile);
        directory.GetAll().Should().HaveCount(3);
    }

    [Fact]
    public void Resolve_MatchesAliasCaseInsensitively()
    {
        var directory = CreateDirectory(_tempFile);
        var user = directory.Resolve("andy");
        user.Should().NotBeNull();
        user!.Email.Should().Be("andrea@anela.cz");
    }

    [Fact]
    public void Resolve_MatchesDisplayName()
    {
        var directory = CreateDirectory(_tempFile);
        directory.Resolve("Petr Svoboda")!.Email.Should().Be("petr@anela.cz");
    }

    [Fact]
    public void Resolve_ReturnsNullForUnknownName()
    {
        var directory = CreateDirectory(_tempFile);
        directory.Resolve("Nobody").Should().BeNull();
    }

    [Fact]
    public void GetAll_ReturnsEmptyWhenFileMissing()
    {
        var directory = CreateDirectory(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid() + ".json"));
        directory.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_WithCompoundAmpersandAssignee_ReturnsFirstMatch()
    {
        var directory = CreateDirectory(_tempFile);
        var user = directory.Resolve("Andy & Bára");
        user.Should().NotBeNull();
        user!.Email.Should().Be("andrea@anela.cz");
    }

    [Fact]
    public void Resolve_WithCompoundCommaAssignee_ReturnsFirstMatch()
    {
        var directory = CreateDirectory(_tempFile);
        var user = directory.Resolve("Petr Svoboda, Andrea Nováková");
        user.Should().NotBeNull();
        user!.Email.Should().Be("petr@anela.cz");
    }

    [Fact]
    public void Resolve_WithSingleNameAfterSplitLogicAdded_StillReturnsCorrectUser()
    {
        var directory = CreateDirectory(_tempFile);
        var user = directory.Resolve("Bára");
        user.Should().NotBeNull();
        user!.Email.Should().Be("bara@anela.cz");
    }

    [Fact]
    public void Resolve_WithCompoundWhereNeitherMatches_ReturnsNull()
    {
        var directory = CreateDirectory(_tempFile);
        var user = directory.Resolve("Nobody & Else");
        user.Should().BeNull();
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }
}
