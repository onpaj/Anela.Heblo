namespace Anela.Heblo.Adapters.Flexi.Manufacture;

public static class FlexiExtensions
{
    public static string RemoveCodePrefix(this string code)
    {
        if (code == null) return null;
        
        // First trim the string
        var trimmed = code.Trim();
        
        // Only remove first occurrence of "code:" if it's at the beginning
        if (trimmed.StartsWith("code:"))
        {
            return trimmed.Substring(5).Trim();
        }
        
        return trimmed;
    }
}