using System.Globalization;
using System.Text;

namespace Anela.Heblo.Xcc;

public static class StringExtensions
{
    /// <summary>
    /// Gets a substring of a string from beginning of the string.
    /// </summary>
    /// <exception cref="T:System.ArgumentNullException">Thrown if <paramref name="str" /> is null</exception>
    /// <exception cref="T:System.ArgumentException">Thrown if <paramref name="len" /> is bigger that string's length</exception>
    public static string Left(this string str, int len)
    {
        Check.NotNull<string>(str, nameof(str));
        if (str.Length < len)
            throw new ArgumentException("len argument can not be bigger than given string's length!");
        return str.Substring(0, len);
    }


    /// <summary>Gets a substring of a string from end of the string.</summary>
    /// <exception cref="T:System.ArgumentNullException">Thrown if <paramref name="str" /> is null</exception>
    /// <exception cref="T:System.ArgumentException">Thrown if <paramref name="len" /> is bigger that string's length</exception>
    public static string Right(this string str, int len)
    {
        Check.NotNull<string>(str, nameof(str));
        if (str.Length < len)
            throw new ArgumentException("len argument can not be bigger than given string's length!");
        return str.Substring(str.Length - len, len);
    }

    /// <summary>
    /// Normalizes a string for search by removing diacritics and converting to lowercase.
    /// This method removes Czech diacritics like č→c, š→s, ž→z, ř→r, etc.
    /// </summary>
    /// <param name="text">The text to normalize</param>
    /// <returns>Normalized text without diacritics in lowercase, or empty string if input is null/whitespace</returns>
    public static string NormalizeForSearch(this string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Odstranit diakritiku pomocí Unicode normalizace
        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder();

        foreach (var c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
}