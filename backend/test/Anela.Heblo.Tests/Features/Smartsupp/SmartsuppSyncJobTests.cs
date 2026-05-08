using Anela.Heblo.Application.Features.Smartsupp.Infrastructure.Jobs;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SmartsuppSyncJobTests
{
    private readonly Mock<ISmartsuppApiClient> _apiClient = new();
    private readonly Mock<ISmartsuppRepository> _repo = new();

    private SmartsuppSyncJob CreateJob() =>
        new(_apiClient.Object, _repo.Object, NullLogger<SmartsuppSyncJob>.Instance);

    [Fact]
    public async Task ExecuteAsync_UpsertsSinglePage_WhenAfterIsNull()
    {
        // Arrange
        var syncState = new SmartsuppSyncState { LastUpdatedAtSeen = null };
        _repo.Setup(r => r.GetOrCreateSyncStateAsync(default)).ReturnsAsync(syncState);

        var searchResult = new SmartsuppSearchResult
        {
            Total = 1,
            After = null,
            Items = new List<SmartsuppConversationData>
            {
                new()
                {
                    Id = "c1",
                    Status = "open",
                    Unread = true,
                    CreatedAt = DateTime.UtcNow.AddHours(-1),
                    UpdatedAt = DateTime.UtcNow,
                    ContactName = "Petra"
                }
            }
        };
        _apiClient.Setup(c => c.SearchConversationsAsync(null, null, 50, default))
                  .ReturnsAsync(searchResult);

        _apiClient.Setup(c => c.GetConversationMessagesAsync("c1", default))
                  .ReturnsAsync(new List<SmartsuppMessageData>());

        _repo.Setup(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), default))
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.UpsertMessagesAsync("c1", It.IsAny<List<SmartsuppMessage>>(), default))
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.SaveChangesAsync(default)).Returns(Task.CompletedTask);
        _repo.Setup(r => r.SetSyncWatermarkAsync(It.IsAny<DateTime>(), default)).Returns(Task.CompletedTask);

        var job = CreateJob();

        // Act
        await job.ExecuteAsync();

        // Assert
        _repo.Verify(r => r.UpsertConversationAsync(
            It.Is<SmartsuppConversation>(c => c.Id == "c1" && c.ContactName == "Petra"),
            default), Times.Once);
        _repo.Verify(r => r.SetSyncWatermarkAsync(It.IsAny<DateTime>(), default), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_PagesThrough_WhenAfterIsNotNull()
    {
        // Arrange
        var syncState = new SmartsuppSyncState { LastUpdatedAtSeen = null };
        _repo.Setup(r => r.GetOrCreateSyncStateAsync(default)).ReturnsAsync(syncState);

        _apiClient.SetupSequence(c => c.SearchConversationsAsync(null, null, 50, default))
                  .ReturnsAsync(new SmartsuppSearchResult
                  {
                      Total = 2,
                      After = "cursor-page2",
                      Items = new List<SmartsuppConversationData>
                      {
                          new() { Id = "c1", Status = "open", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
                      }
                  });

        _apiClient.Setup(c => c.SearchConversationsAsync(null, "cursor-page2", 50, default))
                  .ReturnsAsync(new SmartsuppSearchResult
                  {
                      Total = 2,
                      After = null,
                      Items = new List<SmartsuppConversationData>
                      {
                          new() { Id = "c2", Status = "open", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
                      }
                  });

        _apiClient.Setup(c => c.GetConversationMessagesAsync(It.IsAny<string>(), default))
                  .ReturnsAsync(new List<SmartsuppMessageData>());
        _repo.Setup(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), default)).Returns(Task.CompletedTask);
        _repo.Setup(r => r.UpsertMessagesAsync(It.IsAny<string>(), It.IsAny<List<SmartsuppMessage>>(), default)).Returns(Task.CompletedTask);
        _repo.Setup(r => r.SaveChangesAsync(default)).Returns(Task.CompletedTask);
        _repo.Setup(r => r.SetSyncWatermarkAsync(It.IsAny<DateTime>(), default)).Returns(Task.CompletedTask);

        var job = CreateJob();

        // Act
        await job.ExecuteAsync();

        // Assert — both pages were processed
        _repo.Verify(r => r.UpsertConversationAsync(It.Is<SmartsuppConversation>(c => c.Id == "c1"), default), Times.Once);
        _repo.Verify(r => r.UpsertConversationAsync(It.Is<SmartsuppConversation>(c => c.Id == "c2"), default), Times.Once);
    }
}
