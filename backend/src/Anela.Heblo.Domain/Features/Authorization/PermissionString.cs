namespace Anela.Heblo.Domain.Features.Authorization;

/// <summary>
/// Turns Feature + AccessLevel ↔ wire string ("module.feature.level").
/// Module = portion of the enum name before '_', snake_cased.
/// Feature segment = portion after '_', snake_cased.
/// </summary>
public static class PermissionString
{
    public static string Format(Feature feature, AccessLevel level)
        => $"{ModuleSegment(feature)}.{FeatureSegment(feature)}.{LevelSegment(level)}";

    public static bool TryParse(string s, out Feature feature, out AccessLevel level)
    {
        feature = default;
        level = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var parts = s.Split('.');
        if (parts.Length != 3) return false;
        var enumName = $"{ToPascalCase(parts[0])}_{ToPascalCase(parts[1])}";
        if (!Enum.TryParse(enumName, ignoreCase: false, out feature)) return false;
        return parts[2] switch
        {
            "read" => (level = AccessLevel.Read) == AccessLevel.Read,
            "write" => (level = AccessLevel.Write) == AccessLevel.Write,
            "admin" => (level = AccessLevel.Admin) == AccessLevel.Admin,
            _ => false,
        };
    }

    /// <summary>e.g. Feature.Manufacture_BatchPlanning → "ManufactureBatchPlanning".</summary>
    public static string ConstantSuffix(Feature feature)
        => feature.ToString().Replace("_", "");

    private static string ModuleSegment(Feature f) => ToSnakeCase(f.ToString().Split('_')[0]);
    private static string FeatureSegment(Feature f) => ToSnakeCase(f.ToString().Split('_', 2)[1]);
    private static string LevelSegment(AccessLevel l) => l.ToString().ToLowerInvariant();

    private static string ToSnakeCase(string pascal)
    {
        var sb = new System.Text.StringBuilder(pascal.Length * 2);
        for (var i = 0; i < pascal.Length; i++)
        {
            if (i > 0 && char.IsUpper(pascal[i])) sb.Append('_');
            sb.Append(char.ToLowerInvariant(pascal[i]));
        }
        return sb.ToString();
    }

    private static string ToPascalCase(string snake)
    {
        var sb = new System.Text.StringBuilder(snake.Length);
        var upperNext = true;
        foreach (var ch in snake)
        {
            if (ch == '_') { upperNext = true; continue; }
            sb.Append(upperNext ? char.ToUpperInvariant(ch) : ch);
            upperNext = false;
        }
        return sb.ToString();
    }
}
