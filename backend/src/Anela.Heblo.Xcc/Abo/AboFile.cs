namespace Anela.Heblo.Xcc.Abo;

// https://github.com/jakubzapletal/bank-statements/blob/master/Parser/ABOParser.php
public class AboFile
{
    public AboHeader Header { get; set; }
    public List<AboLine> Lines { get; set; } = new List<AboLine>();

    public static AboFile Parse(string data)
    {
        var file = new AboFile()
        {
            Header = GetHeader(data),
            Lines = GetLines(data)
        };

        return file;
    }

    private static List<AboLine> GetLines(string data)
    {
        var lines = data.Split(new[] { Environment.NewLine, "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

        // Skip header line and process transaction lines
        return lines.Skip(1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => new AboLine(line))
            .ToList();
    }

    private static AboHeader GetHeader(string data)
    {
        var lines = data.Split(new[] { Environment.NewLine, "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        var firstLine = lines.FirstOrDefault() ?? string.Empty;

        return new AboHeader(firstLine);
    }
}

public class AboLine
{
    public string Raw { get; }

    public AboLine(string rawLine)
    {
        Raw = rawLine ?? string.Empty;
        // ABO format parsing can be implemented here if needed for detailed transaction analysis
        // For now, we just store the raw line as FlexiBee will parse it
    }
}

public class AboHeader
{
    public string Raw { get; }

    public AboHeader(string headerLine = "")
    {
        Raw = headerLine;
        // ABO header parsing can be implemented here if needed
        // For now, we just store the raw header as FlexiBee will parse it
    }
}
