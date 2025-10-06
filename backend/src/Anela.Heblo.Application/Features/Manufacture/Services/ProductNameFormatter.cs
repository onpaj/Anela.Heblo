using System.Text.RegularExpressions;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class ProductNameFormatter : IProductNameFormatter
{
    public string ShortProductName(string productName)
    {
        // Old labels: Důvěrný pan Jasmín - jemný krémový deodorant 30ml
        var parts = productName.Split('-');

        if (parts.Length > 1)
            return parts[0].Trim();

        // New labels: Bílá noční teenka 30ml
        var match = Regex.Match(productName, @"(.+?)\s*(?:\d+\s*ml|TESTER)");
        return match.Success ? match.Groups[1].Value.Trim() : productName;
    }
}