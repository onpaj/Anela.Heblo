using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Embeddings;
using MeaiOptions = Microsoft.Extensions.AI.EmbeddingGenerationOptions;

namespace Anela.Heblo.Adapters.OpenAI.Tests;

public class OpenAiEmbeddingGeneratorTests
{
    private static OpenAiEmbeddingGenerator BuildGenerator(HttpMessageHandler handler, int dimensions = 3)
    {
        var options = Options.Create(new OpenAiEmbeddingOptions
        {
            ApiKey = "test-key",
            EmbeddingModel = "text-embedding-3-small",
            EmbeddingDimensions = dimensions,
        });
        var client = new EmbeddingClient(
            options.Value.EmbeddingModel,
            new ApiKeyCredential(options.Value.ApiKey),
            new OpenAIClientOptions { Transport = new HttpClientPipelineTransport(new HttpClient(handler)) });

        return new OpenAiEmbeddingGenerator(options, NullLogger<OpenAiEmbeddingGenerator>.Instance, client);
    }

    // Reads the "input" array off the outgoing request body and returns embeddings whose
    // vector encodes the numeric suffix of each input string (e.g. "text-7" -> [7,7,7]),
    // so assertions can tell "the embedding for input N" apart from array position alone.
    // When capturedModels/capturedDimensions are supplied, the "model"/"dimensions" fields of
    // the same request body are recorded so per-call override tests can assert on what was sent.
    private static HttpResponseMessage BuildEmbeddingResponse(
        HttpRequestMessage request,
        bool reverseOrder = false,
        List<string>? capturedModels = null,
        List<int?>? capturedDimensions = null)
    {
        var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(body);

        capturedModels?.Add(doc.RootElement.GetProperty("model").GetString()!);
        capturedDimensions?.Add(
            doc.RootElement.TryGetProperty("dimensions", out var dimensionsElement)
                ? dimensionsElement.GetInt32()
                : null);

        var inputs = doc.RootElement.GetProperty("input").EnumerateArray()
            .Select(e => e.GetString()!)
            .ToList();

        var items = Enumerable.Range(0, inputs.Count).Select(i =>
        {
            var id = double.Parse(inputs[i].Split('-').Last());
            return new { index = i, id };
        }).ToList();

        if (reverseOrder)
            items.Reverse();

        var payload = new
        {
            @object = "list",
            data = items.Select(item => new
            {
                @object = "embedding",
                index = item.index,
                embedding = new[] { item.id, item.id, item.id },
            }),
            model = "text-embedding-3-small",
            usage = new { prompt_tokens = inputs.Count, total_tokens = inputs.Count },
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json"),
        };
    }

    private static List<string> MakeInputs(int count) =>
        Enumerable.Range(0, count).Select(i => $"text-{i}").ToList();

    private sealed class StatefulHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        private int _callCount;
        public int CallCount => _callCount;

        public StatefulHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            try
            {
                return Task.FromResult(_handler(request));
            }
            catch (Exception ex)
            {
                return Task.FromException<HttpResponseMessage>(ex);
            }
        }
    }

    [Fact]
    public async Task GenerateAsync_SingleBatch_IssuesOneCallAndReturnsResultsInInputOrder()
    {
        var handler = new StatefulHandler(req => BuildEmbeddingResponse(req));
        var generator = BuildGenerator(handler);
        var inputs = MakeInputs(5);

        var result = await generator.GenerateAsync(inputs);

        handler.CallCount.Should().Be(1);
        result.Count.Should().Be(5);
        for (var i = 0; i < 5; i++)
            result[i].Vector.Span[0].Should().Be((float)i);
    }

    [Fact]
    public async Task GenerateAsync_OversizedBatch_ChunksAndPreservesOrder()
    {
        var handler = new StatefulHandler(req => BuildEmbeddingResponse(req));
        var generator = BuildGenerator(handler);
        var inputs = MakeInputs(2500);

        var result = await generator.GenerateAsync(inputs);

        handler.CallCount.Should().Be(2, "2500 inputs need ceil(2500/2048) = 2 chunk requests");
        result.Count.Should().Be(2500);
        for (var i = 0; i < 2500; i++)
            result[i].Vector.Span[0].Should().Be((float)i);
    }

    [Fact]
    public async Task GenerateAsync_ResponseItemsOutOfOrder_AreReorderedByIndex()
    {
        var handler = new StatefulHandler(req => BuildEmbeddingResponse(req, reverseOrder: true));
        var generator = BuildGenerator(handler);
        var inputs = MakeInputs(2);

        var result = await generator.GenerateAsync(inputs);

        result.Count.Should().Be(2);
        result[0].Vector.Span[0].Should().Be(0f, "index-based ordering must correct the shuffled response array");
        result[1].Vector.Span[0].Should().Be(1f);
    }

    [Fact]
    public async Task GenerateAsync_EmptyInput_ReturnsEmptyWithoutCallingApi()
    {
        var handler = new StatefulHandler(req => BuildEmbeddingResponse(req));
        var generator = BuildGenerator(handler);

        var result = await generator.GenerateAsync(Array.Empty<string>());

        handler.CallCount.Should().Be(0);
        result.Count.Should().Be(0);
    }

    [Fact]
    public async Task GenerateAsync_TransientFailureThenSuccess_RecoversAndReturnsCorrectResult()
    {
        var attempt = 0;
        var handler = new StatefulHandler(req =>
        {
            attempt++;
            if (attempt == 1)
                throw new HttpRequestException("transient failure");
            return BuildEmbeddingResponse(req);
        });
        var generator = BuildGenerator(handler);
        var inputs = MakeInputs(1);

        var result = await generator.GenerateAsync(inputs);

        handler.CallCount.Should().Be(2);
        result.Count.Should().Be(1);
        result[0].Vector.Span[0].Should().Be(0f);
    }

    [Fact]
    public async Task GenerateAsync_RetriesExhausted_ThrowsWithoutPartialResult()
    {
        var handler = new StatefulHandler(_ => throw new HttpRequestException("permanent failure"));
        var generator = BuildGenerator(handler);
        var inputs = MakeInputs(1);

        var act = async () => await generator.GenerateAsync(inputs);

        await act.Should().ThrowAsync<Exception>();
        handler.CallCount.Should().Be(4, "the OpenAI SDK's transport pipeline retries HttpRequestException up to 3 times (4 total attempts) before the failure surfaces");
    }

    [Fact]
    public async Task GenerateAsync_CalledTwice_ReusesSameClient()
    {
        var handler = new StatefulHandler(req => BuildEmbeddingResponse(req));
        var generator = BuildGenerator(handler);

        var first = await generator.GenerateAsync(MakeInputs(1));
        var second = await generator.GenerateAsync(new List<string> { "text-1" });

        handler.CallCount.Should().Be(2);
        first[0].Vector.Span[0].Should().Be(0f);
        second[0].Vector.Span[0].Should().Be(1f);
    }

    [Fact]
    public async Task GenerateAsync_DimensionsOverride_SendsOverriddenDimensions()
    {
        var dimensions = new List<int?>();
        var handler = new StatefulHandler(req => BuildEmbeddingResponse(req, capturedDimensions: dimensions));
        var generator = BuildGenerator(handler, dimensions: 3);

        await generator.GenerateAsync(MakeInputs(1), new MeaiOptions { Dimensions = 3072 });

        dimensions.Should().ContainSingle().Which.Should().Be(3072);
    }

    [Fact]
    public async Task GenerateAsync_NoDimensionsOverride_SendsConfiguredDimensions()
    {
        var dimensions = new List<int?>();
        var handler = new StatefulHandler(req => BuildEmbeddingResponse(req, capturedDimensions: dimensions));
        var generator = BuildGenerator(handler, dimensions: 7);

        await generator.GenerateAsync(MakeInputs(1));

        dimensions.Should().ContainSingle().Which.Should().Be(7);
    }
}
