namespace Anela.Heblo.Adapters.Flexi.Manufacture;

public static class FlexiExtensions
{
    public static string RemoveCodePrefix(this string code)=> code.Replace("code:", "").Trim();
}