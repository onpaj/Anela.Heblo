using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace Anela.Heblo.Application.Features.LabelIdentification.Services;

public sealed class AnthropicLabelOcrService : ILabelOcrService
{
    private const int JpegQuality = 90;

    // A byte cap on the upload does not bound pixel count: a highly compressible image
    // can decode to gigabytes of raw pixels well under any reasonable size limit. ~50 MP
    // is far above any phone camera's actual output but far below what would OOM the
    // container decoding it.
    private const long MaxDecodedPixels = 50_000_000;

    // Constrained to one job the model is good at: read text. Labels on a roll are all
    // the same product, so rotation, blur, and ghost text bleeding in from neighbouring
    // stickers are expected and harmless.
    private const string Prompt =
        "This is a photo of a cosmetic product label on a roll of stickers. " +
        "Return the INCI ingredient list of ONE label as a single comma-separated line. " +
        "All stickers on the roll are the same product. Ignore rotation, blur, and any " +
        "partial text bleeding in from neighbouring stickers. " +
        "Return only the ingredient list — no preamble, no explanation, no 'Ingredients:' prefix. " +
        "If no ingredients are legible, return nothing at all.";

    private readonly IChatClient _chatClient;
    private readonly LabelIdentificationOptions _options;
    private readonly ILogger<AnthropicLabelOcrService> _logger;

    public AnthropicLabelOcrService(
        IChatClient chatClient,
        IOptions<LabelIdentificationOptions> options,
        ILogger<AnthropicLabelOcrService> logger)
    {
        _chatClient = chatClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> ReadIngredientsAsync(Stream photo, CancellationToken cancellationToken)
    {
        var jpeg = Downscale(photo);

        var message = new ChatMessage(ChatRole.User, new List<AIContent>
        {
            new DataContent(jpeg, "image/jpeg"),
            new TextContent(Prompt),
        });

        var response = await _chatClient.GetResponseAsync(new[] { message }, cancellationToken: cancellationToken);
        var text = response.Messages.FirstOrDefault()?.Text ?? string.Empty;

        _logger.LogDebug("Label OCR returned {Length} characters", text.Length);

        return text.Trim();
    }

    private byte[] Downscale(Stream photo)
    {
        // Inspect dimensions from the header before decoding pixels. SKCodec.Create only
        // parses enough of the file to report SKCodec.Info — it does not allocate the full
        // pixel buffer, so a highly compressible image with attacker-controlled huge
        // dimensions is rejected before it can OOM the process.
        using var codec = SKCodec.Create(photo)
            ?? throw new LabelOcrException("Photo could not be decoded as an image.");

        if ((long)codec.Info.Width * codec.Info.Height > MaxDecodedPixels)
        {
            throw new LabelOcrException("Photo dimensions exceed the supported limit.");
        }

        using var original = SKBitmap.Decode(codec)
            ?? throw new LabelOcrException("Photo could not be decoded as an image.");

        var longestEdge = Math.Max(original.Width, original.Height);
        var bitmap = original;
        SKBitmap? resized = null;

        if (longestEdge > _options.MaxImageEdge)
        {
            var scale = (double)_options.MaxImageEdge / longestEdge;
            var width = (int)Math.Round(original.Width * scale);
            var height = (int)Math.Round(original.Height * scale);

            resized = original.Resize(new SKImageInfo(width, height), SKFilterQuality.Medium)
                ?? throw new LabelOcrException("Photo could not be resized.");
            bitmap = resized;
        }

        try
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
            return data.ToArray();
        }
        finally
        {
            resized?.Dispose();
        }
    }
}
