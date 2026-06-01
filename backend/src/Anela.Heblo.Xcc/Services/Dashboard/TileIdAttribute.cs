namespace Anela.Heblo.Xcc.Services.Dashboard;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class TileIdAttribute : Attribute
{
    public string Value { get; }

    public TileIdAttribute(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Tile ID must be a non-empty, non-whitespace string.", nameof(value));
        Value = value;
    }
}
