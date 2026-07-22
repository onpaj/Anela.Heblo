using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Anela.Heblo.API.Infrastructure;

/// <summary>
/// Model binder for nullable <see cref="DateTime"/> query parameters that parses the raw
/// value as UTC, independent of the server's local timezone offset.
/// </summary>
/// <remarks>
/// The default ASP.NET Core <see cref="DateTime"/> binder parses values containing a
/// UTC/offset designator (e.g. "2026-01-01T00:00:00.000Z") and converts them to the
/// server's local time (<see cref="DateTimeKind.Local"/>), which can shift the resulting
/// <c>.Date</c> by a day depending on the server's timezone. This binder instead uses
/// <see cref="DateTimeStyles.AdjustToUniversal"/> combined with
/// <see cref="DateTimeStyles.AssumeUniversal"/> so the parsed value is always UTC-normalized
/// regardless of the server's local offset.
///
/// Apply this via <c>[ModelBinder(BinderType = typeof(UtcDateTimeModelBinder))]</c> on
/// individual action parameters — it is intentionally not registered as a global
/// <see cref="IModelBinderProvider"/> for <see cref="DateTime"/>, to avoid changing the
/// behavior of the other controllers that use <c>[FromQuery] DateTime?</c>.
/// </remarks>
public class UtcDateTimeModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        if (bindingContext == null)
            throw new ArgumentNullException(nameof(bindingContext));

        var modelName = bindingContext.ModelName;
        var valueProviderResult = bindingContext.ValueProvider.GetValue(modelName);

        if (valueProviderResult == ValueProviderResult.None)
        {
            // No value supplied — leave as not-bound so the parameter's default (null) applies.
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(modelName, valueProviderResult);

        var value = valueProviderResult.FirstValue;

        if (string.IsNullOrWhiteSpace(value))
        {
            bindingContext.Result = ModelBindingResult.Success(null);
            return Task.CompletedTask;
        }

        if (TryParseUtc(value, out var parsed))
        {
            bindingContext.Result = ModelBindingResult.Success((DateTime?)parsed);
            return Task.CompletedTask;
        }

        bindingContext.ModelState.TryAddModelError(
            modelName,
            $"The value '{value}' is not valid for {modelName}.");
        bindingContext.Result = ModelBindingResult.Failed();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Parses a raw query-string value as UTC, ignoring the server's local timezone offset.
    /// Exposed as internal for direct unit testing of the parsing logic.
    /// </summary>
    internal static bool TryParseUtc(string value, out DateTime result)
    {
        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out result);
    }
}
