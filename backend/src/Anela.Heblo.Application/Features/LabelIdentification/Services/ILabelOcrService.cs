namespace Anela.Heblo.Application.Features.LabelIdentification.Services;

/// <summary>Raised when the uploaded photo cannot be decoded as an image.</summary>
public sealed class LabelOcrException : Exception
{
    public LabelOcrException(string message) : base(message) { }
}

public interface ILabelOcrService
{
    /// <summary>
    /// Transcribes the ingredient list from a label photo. Returns an empty string when
    /// the model finds nothing readable. Throws <see cref="LabelOcrException"/> when the
    /// photo cannot be decoded; transport failures propagate as-is.
    /// </summary>
    Task<string> ReadIngredientsAsync(Stream photo, CancellationToken cancellationToken);
}
