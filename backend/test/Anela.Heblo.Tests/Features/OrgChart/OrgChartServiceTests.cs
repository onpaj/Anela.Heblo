using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.OrgChart;
using Anela.Heblo.Application.Features.OrgChart;
using Anela.Heblo.Application.Features.OrgChart.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.OrgChart;

public class OrgChartServiceTests
{
    private const string TestDataSourceUrl = "https://example.test/orgchart.json";

    private readonly Mock<ILogger<OrgChartService>> _loggerMock = new();
    private readonly IOptions<OrgChartOptions> _options =
        Options.Create(new OrgChartOptions { DataSourceUrl = TestDataSourceUrl });

    [Fact]
    public async Task GetOrganizationStructureAsync_WrapsHttpRequestException_AndDoesNotLogError()
    {
        // Arrange
        var inner = new HttpRequestException("network is unreachable");
        var service = CreateService(StubHttpMessageHandler.ThrowsOnSend(inner));

        // Act
        var act = async () => await service.GetOrganizationStructureAsync(CancellationToken.None);

        // Assert: wrap preserved
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().StartWith("Failed to fetch organizational structure: ");
        thrown.Which.InnerException.Should().BeSameAs(inner);

        // Assert: service must not log Error (controller is the single owner)
        VerifyNoErrorLog();
    }

    [Fact]
    public async Task GetOrganizationStructureAsync_WrapsJsonException_AndDoesNotLogError()
    {
        // Arrange: 200 OK with a body that is not valid JSON
        var service = CreateService(StubHttpMessageHandler.Returns(HttpStatusCode.OK, "{ this is not json"));

        // Act
        var act = async () => await service.GetOrganizationStructureAsync(CancellationToken.None);

        // Assert: wrap preserved
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().StartWith("Failed to parse organizational structure: ");
        thrown.Which.InnerException.Should().BeOfType<JsonException>();

        // Assert: service must not log Error (controller is the single owner)
        VerifyNoErrorLog();
    }

    [Fact]
    public async Task GetOrganizationStructureAsync_RethrowsGenericException_AndDoesNotLogError()
    {
        // Arrange: handler throws a non-Http, non-Json exception so it lands in the generic catch
        var inner = new InvalidProgramException("unexpected transport-layer failure");
        var service = CreateService(StubHttpMessageHandler.ThrowsOnSend(inner));

        // Act
        var act = async () => await service.GetOrganizationStructureAsync(CancellationToken.None);

        // Assert: generic exception is re-thrown unwrapped (same instance, same type)
        var thrown = await act.Should().ThrowAsync<InvalidProgramException>();
        thrown.Which.Should().BeSameAs(inner);

        // Assert: service must not log Error (controller is the single owner)
        VerifyNoErrorLog();
    }

    [Fact]
    public async Task GetOrganizationStructureAsync_ThrowsOnNullDeserialization_AndDoesNotLogError()
    {
        // Arrange: 200 OK with a body of literal "null" — System.Text.Json returns null,
        // which triggers the in-method null guard (not a catch block).
        var service = CreateService(StubHttpMessageHandler.Returns(HttpStatusCode.OK, "null"));

        // Act
        var act = async () => await service.GetOrganizationStructureAsync(CancellationToken.None);

        // Assert: typed wrap preserved
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Be("Failed to deserialize organizational structure");
        thrown.Which.InnerException.Should().BeNull();

        // Assert: service must not log Error (controller is the single owner)
        VerifyNoErrorLog();
    }

    [Fact]
    public async Task GetOrganizationStructureAsync_MapsFullJsonGraphToResponseContracts()
    {
        // Arrange: a representative external JSON payload covering every mapped field,
        // including one position with a null Employees list and one employee with a null Url,
        // to exercise the null-coalescing mapping rules from design.r1.md.
        const string json = """
        {
          "organization": {
            "name": "Anela Heblo",
            "positions": [
              {
                "id": "pos-1",
                "title": "CEO",
                "description": "Chief Executive Officer",
                "level": 1,
                "parentPositionId": null,
                "department": "Executive",
                "url": "https://example.test/pos-1",
                "employees": [
                  {
                    "id": "emp-1",
                    "name": "Jana Nováková",
                    "email": "jana.novakova@anela.cz",
                    "startDate": "2020-01-15",
                    "isPrimary": true,
                    "url": "https://example.test/emp-1"
                  }
                ]
              },
              {
                "id": "pos-2",
                "title": "CFO",
                "description": "Chief Financial Officer",
                "level": 2,
                "parentPositionId": "pos-1",
                "department": "Finance",
                "url": null,
                "employees": null
              }
            ]
          }
        }
        """;
        var service = CreateService(StubHttpMessageHandler.Returns(HttpStatusCode.OK, json));

        // Act
        var result = await service.GetOrganizationStructureAsync(CancellationToken.None);

        // Assert: response envelope defaults (never populated from the JSON model)
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.Params.Should().BeNull();

        // Assert: organization + full position/employee graph, in order, all fields
        result.Organization.Name.Should().Be("Anela Heblo");
        result.Organization.Positions.Should().HaveCount(2);

        var pos1 = result.Organization.Positions[0];
        pos1.Id.Should().Be("pos-1");
        pos1.Title.Should().Be("CEO");
        pos1.Description.Should().Be("Chief Executive Officer");
        pos1.Level.Should().Be(1);
        pos1.ParentPositionId.Should().Be(string.Empty);
        pos1.Department.Should().Be("Executive");
        pos1.Url.Should().Be("https://example.test/pos-1");
        pos1.Employees.Should().HaveCount(1);

        var emp1 = pos1.Employees[0];
        emp1.Id.Should().Be("emp-1");
        emp1.Name.Should().Be("Jana Nováková");
        emp1.Email.Should().Be("jana.novakova@anela.cz");
        emp1.StartDate.Should().Be("2020-01-15");
        emp1.IsPrimary.Should().BeTrue();
        emp1.Url.Should().Be("https://example.test/emp-1");

        var pos2 = result.Organization.Positions[1];
        pos2.Id.Should().Be("pos-2");
        pos2.Title.Should().Be("CFO");
        pos2.Description.Should().Be("Chief Financial Officer");
        pos2.Level.Should().Be(2);
        pos2.ParentPositionId.Should().Be("pos-1");
        pos2.Department.Should().Be("Finance");
        pos2.Url.Should().Be(string.Empty);
        pos2.Employees.Should().BeEmpty();

        // Assert: service must not log Error (controller is the single owner)
        VerifyNoErrorLog();
    }

    private OrgChartService CreateService(HttpMessageHandler handler) =>
        new(new HttpClient(handler), _options, _loggerMock.Object);

    private void VerifyNoErrorLog() =>
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _factory;

        private StubHttpMessageHandler(Func<HttpResponseMessage> factory) => _factory = factory;

        public static StubHttpMessageHandler Returns(HttpStatusCode status, string body) =>
            new(() => new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });

        public static StubHttpMessageHandler ThrowsOnSend(Exception toThrow) =>
            new(() => throw toThrow);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_factory());
    }
}
