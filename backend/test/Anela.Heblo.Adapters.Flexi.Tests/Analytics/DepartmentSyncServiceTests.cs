using Anela.Heblo.Adapters.Flexi.Analytics;
using Anela.Heblo.Persistence.Analytics;
using Anela.Heblo.Persistence.Analytics.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments;
using Rem.FlexiBeeSDK.Model.Accounting.Departments;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Analytics;

public class DepartmentSyncServiceTests
{
    private static AnalyticsDbContext CreateInMemoryContext() =>
        new(new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static DepartmentSyncService CreateService(
        IDepartmentClient departmentClient,
        AnalyticsDbContext ctx)
    {
        var repo = new SyncWatermarkRepository(ctx);
        return new DepartmentSyncService(
            departmentClient,
            repo,
            ctx,
            Mock.Of<ILogger<DepartmentSyncService>>());
    }

    private static DepartmentFlexiDto MakeDepartmentDto(int id, string code, string name) =>
        new()
        {
            Id = id,
            Code = code,
            Name = name,
            LastUpdate = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc),
        };

    [Fact]
    public async Task SyncAsync_WhenNoDepartments_ReturnsSuccessWithZeroRows()
    {
        // Arrange
        var client = new Mock<IDepartmentClient>();
        await using var ctx = CreateInMemoryContext();

        client.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DepartmentFlexiDto>());

        var svc = CreateService(client.Object, ctx);

        // Act
        var result = await svc.SyncAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.RowsFetched.Should().Be(0);
        result.RowsUpserted.Should().Be(0);

        var state = await ctx.SyncStates.FindAsync("department");
        state!.LastRunStatus.Should().Be("OK");
    }

    [Fact]
    public async Task SyncAsync_WhenDepartmentsExist_UpsertsAllAndSetsOkStatus()
    {
        // Arrange
        var client = new Mock<IDepartmentClient>();
        await using var ctx = CreateInMemoryContext();

        var dtos = new List<DepartmentFlexiDto>
        {
            MakeDepartmentDto(1, "SALES", "Sales"),
            MakeDepartmentDto(2, "FINANCE", "Finance"),
        };

        client.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(dtos);

        var svc = CreateService(client.Object, ctx);

        // Act
        var result = await svc.SyncAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.RowsFetched.Should().Be(2);
        result.RowsUpserted.Should().Be(2);

        var departments = await ctx.Departments.ToListAsync();
        departments.Should().HaveCount(2);
        departments.Should().Contain(d => d.FlexiId == 1 && d.Code == "SALES");
        departments.Should().Contain(d => d.FlexiId == 2 && d.Code == "FINANCE");
        departments.Should().AllSatisfy(d => d.LastModified.Should().NotBeNull());

        var state = await ctx.SyncStates.FindAsync("department");
        state!.LastRunStatus.Should().Be("OK");
        state.LastRunRowsFetched.Should().Be(2);
        state.LastRunRowsUpserted.Should().Be(2);
    }

    [Fact]
    public async Task SyncAsync_OnClientError_RecordsFailedStatus()
    {
        // Arrange
        var client = new Mock<IDepartmentClient>();
        await using var ctx = CreateInMemoryContext();

        client.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Flexi unreachable"));

        var svc = CreateService(client.Object, ctx);

        // Act
        var result = await svc.SyncAsync();

        // Assert
        result.IsSuccess.Should().BeFalse();

        var state = await ctx.SyncStates.FindAsync("department");
        state!.LastRunStatus.Should().Be("FAILED");
        state.LastErrorMessage.Should().Contain("Flexi unreachable");
        state.Watermark.Should().BeNull();
    }
}
