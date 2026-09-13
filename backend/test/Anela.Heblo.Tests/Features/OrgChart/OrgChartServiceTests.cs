using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.OrgChart;
using Anela.Heblo.Application.Features.OrgChart;
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
    public async Task GetOrganizationStructureAsync_ReturnsMappedResponse_WhenJsonIsValid()
    {
        // Arrange: 200 OK with a valid JSON body describing one position with one nested employee,
        // so the test exercises the full deserialization tree, not just the top-level shell.
        const string json = """
        {
          "organization": {
            "name": "Anela",
            "positions": [
              {
                "id": "pos-1",
                "title": "CEO",
                "description": "Chief Executive Officer",
                "level": 1,
                "department": "Executive",
                "employees": [
                  {
                    "id": "emp-1",
                    "name": "Jana Novakova",
                    "email": "jana.novakova@anela.cz",
                    "startDate": "2020-01-01",
                    "isPrimary": true
                  }
                ]
              }
            ]
          }
        }
        """;
        var service = CreateService(StubHttpMessageHandler.Returns(HttpStatusCode.OK, json));

        // Act
        var result = await service.GetOrganizationStructureAsync(CancellationToken.None);

        // Assert: base response shape (BaseResponse defaults via OrgChartResponse()'s parameterless ctor)
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();

        // Assert: top-level organization mapping
        result.Organization.Name.Should().Be("Anela");
        result.Organization.Positions.Should().HaveCount(1);

        // Assert: nested position mapping (guards against a PositionDto field rename going undetected)
        var position = result.Organization.Positions[0];
        position.Id.Should().Be("pos-1");
        position.Title.Should().Be("CEO");
        position.Level.Should().Be(1);
        position.Department.Should().Be("Executive");
        position.Employees.Should().HaveCount(1);

        // Assert: nested employee mapping (guards against an EmployeeDto field rename going undetected)
        var employee = position.Employees[0];
        employee.Name.Should().Be("Jana Novakova");
        employee.IsPrimary.Should().BeTrue();

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
