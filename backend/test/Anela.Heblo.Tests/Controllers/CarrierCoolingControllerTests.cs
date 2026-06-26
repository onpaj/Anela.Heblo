using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Features.CarrierCooling.UseCases.GetCarrierCoolingMatrix;
using Anela.Heblo.Application.Features.CarrierCooling.UseCases.SetCarrierCooling;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Shared;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

public class CarrierCoolingControllerTests : IClassFixture<HebloWebApplicationFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HebloWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CarrierCoolingControllerTests(HebloWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync() => await _factory.ClearDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Get_ReturnsMatrixWithThreeGroups()
    {
        var response = await _client.GetAsync("/api/carrier-cooling");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GetCarrierCoolingMatrixResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Groups.Should().HaveCount(3);
        body.Groups.Should().Contain(g => g.Carrier == Carriers.Zasilkovna);
        body.Groups.Should().Contain(g => g.Carrier == Carriers.PPL);
        body.Groups.Should().Contain(g => g.Carrier == Carriers.GLS);
    }

    [Fact]
    public async Task Get_DefaultsCoolingToNone_WhenNothingStored()
    {
        var response = await _client.GetAsync("/api/carrier-cooling");
        var body = await response.Content.ReadFromJsonAsync<GetCarrierCoolingMatrixResponse>(JsonOptions);

        body!.Groups.SelectMany(g => g.Rows).Should().AllSatisfy(row =>
            row.Cooling.Should().Be(Cooling.None));
    }

    [Fact]
    public async Task Put_StoresCooling_AndGetReturnsUpdatedValue()
    {
        var putRequest = new SetCarrierCoolingRequest
        {
            Carrier = Carriers.PPL,
            DeliveryHandling = DeliveryHandling.NaRuky,
            Cooling = Cooling.L1,
        };

        var putResponse = await _client.PutAsJsonAsync("/api/carrier-cooling", putRequest, JsonOptions);

        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await _client.GetAsync("/api/carrier-cooling");
        var body = await getResponse.Content.ReadFromJsonAsync<GetCarrierCoolingMatrixResponse>(JsonOptions);
        var pplGroup = body!.Groups.First(g => g.Carrier == Carriers.PPL);
        var naRukyRow = pplGroup.Rows.First(r => r.DeliveryHandling == DeliveryHandling.NaRuky);
        naRukyRow.Cooling.Should().Be(Cooling.L1);
    }

    [Fact]
    public async Task Put_ReturnsBadRequest_WhenCarrierHandlingComboIsUnavailable()
    {
        var putRequest = new SetCarrierCoolingRequest
        {
            Carrier = Carriers.Osobak,
            DeliveryHandling = DeliveryHandling.NaRuky,
            Cooling = Cooling.L1,
        };

        var response = await _client.PutAsJsonAsync("/api/carrier-cooling", putRequest, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
